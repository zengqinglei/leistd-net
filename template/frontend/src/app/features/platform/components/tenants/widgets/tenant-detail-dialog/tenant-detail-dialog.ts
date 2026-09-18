// prettier-ignore
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  input,
  model,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormField, form, maxLength, required, validate } from '@angular/forms/signals';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmSeparator } from '@spartan-ng/helm/separator';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { catchError, EMPTY, finalize, Subscription, tap } from 'rxjs';

import { applicationErrorMessage } from '../../../../../../core/errors/application-http-error';
import { ConfirmService } from '../../../../../../core/feedback/confirm-service';
//#if (IncludeLocalization)
import { translationReady } from '../../../../../../core/i18n/translation-ready';
//#endif
import { SettingContextService } from '../../../../../../core/settings/setting-context-service';
import {
  TENANT_CONNECTION_NAME_PATTERN,
  TENANT_CONNECTION_STRING_MAX_LENGTH,
  TenantConnectionDto,
  normalizeTenantConnectionName,
} from '../../../../../../shared/dtos/tenant-connection.dto';
import { TenantOutputDto } from '../../../../../../shared/dtos/tenant.dto';
import { AppDate } from '../../../../../../shared/pipes/app-date-pipe';
import { TenantConnectionService } from '../../../../services/tenant-connection-service';

/** 连接编辑器的三档：收起 / 添加一条 / 改某条的连接串。 */
type ConnectionEditorMode = 'idle' | 'add' | 'edit';

interface ConnectionEditorModel {
  name: string;
  connectionString: string;
}

/**
 * 租户详情。
 *
 * 分两段：**标识**来自列表已有的租户对象，打开即可显示；**数据库连接**要额外请求连接列表。
 * 两段的失败面因此分开——连接请求失败只让连接段降级，标识段照常呈现：
 * 一个副请求失败就把整个详情打空，会让人以为租户本身出了问题。
 *
 * 连接是**一张表**而不是一个标志位：一个租户在多个服务各可以登记一条，名字就是使用方
 * DbContext 的连接名。**一条都没有即该租户不单独分库**——这一档只能靠列表为空看出来，
 * 所以空列表必须显式写一句话，否则"故意不分库"与"漏登记"在界面上长得一模一样。
 *
 * 连接串只写：从不预填、也不从任何响应里读回来（见 DTO 说明）。
 *
 * 连接接口要求 `App.Tenants.Update`，所以 {@link canManage} 为假时**不发这些请求**、
 * 也不渲染连接段与编辑按钮：发一个必然 403 的请求只会在控制台留下红字，
 * 再配一个点不动的按钮，比直接不显示更糟。
 */
@Component({
  selector: 'app-tenant-detail-dialog',
  // prettier-ignore
  imports: [
    AppDate,
    FormField,
    HlmButton,
    HlmInput,
    HlmSeparator,
    HlmSpinner,
    ...HlmDialogImports,
    ...HlmFieldImports,
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
  ],
  templateUrl: './tenant-detail-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TenantDetailDialog {
  readonly open = model(false);
  readonly tenant = input<TenantOutputDto | null>(null);

  /** 是否有租户更新权限：决定连接段与编辑按钮是否出现。 */
  readonly canManage = input(false);

  /**
   * 请求编辑当前租户。
   *
   * 是 `output` 而不是 `model`：这里表达的是**动作**，不是需要双向同步的状态。
   * 用 model 时连续两次编辑同一个租户，第二次 `set` 拿到的是同一个对象引用，
   * signal 判等后不再发出变化——详情关掉了，编辑框却不会打开。
   */
  readonly edit = output<TenantOutputDto>();

  private readonly connectionService = inject(TenantConnectionService);
  private readonly confirmService = inject(ConfirmService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly displayTimeZone = inject(SettingContextService).timeZone;
  protected readonly displayLocale = inject(SettingContextService).displayLocale;
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  private readonly translationReady = translationReady(this.transloco);
  //#endif

  /** `null` = 尚未拿到列表；空数组 = 拿到了，该租户不单独分库。两者在界面上不是一回事。 */
  protected readonly connections = signal<TenantConnectionDto[] | null>(null);

  /**
   * 能不能现在登记连接。
   *
   * 分两档，判据是**该租户此前有没有任意一条登记**：
   * 一条都没有时，这一条会把它从"不分库"改成"分库"——数据落点变了，而它现有的数据
   * （含租户管理员）都在各服务自己配置的库里，登记连接不会把它们搬过去，后端因此对
   * **在用**租户以 409 拒绝。已经分库的租户补一个此前没有的名字不在此列：那个服务本来
   * 就是失败关闭的，回落库里没有它的数据，补登是修复动作。
   *
   * 这里把后端的判据照搬到界面上，是为了把"点了才报错"变成"看得见为什么点不了"。
   */
  protected readonly canAddConnection = computed(() => {
    const list = this.connections();
    if (list === null) {
      return false;
    }

    return list.length > 0 || this.tenant()?.isActive !== true;
  });
  protected readonly connectionsLoading = signal(false);
  protected readonly connectionsError = signal<string | null>(null);

  /** 写操作的失败面与列表查询分开：一条改不动，不该把已经列出来的连接一起清掉。 */
  protected readonly actionError = signal<string | null>(null);
  protected readonly submitting = signal(false);

  protected readonly editorMode = signal<ConnectionEditorMode>('idle');
  /** 'edit' 档下正在改的那条；决定提交时用哪个名字与哪个版本。 */
  private readonly editingConnection = signal<TenantConnectionDto | null>(null);

  /** 刷新令牌：写操作成功后递增，复用加载 effect 那条带取消语义的路径重新拉列表。 */
  private readonly reloadToken = signal(0);

  protected readonly editorModel = signal<ConnectionEditorModel>({
    name: '',
    connectionString: '',
  });

  readonly connectionForm = form(this.editorModel, (path) => {
    //#if (IncludeLocalization)
    required(path.name, {
      message: this.transloco.translate('common.validation.required'),
      when: () => this.editorMode() === 'add',
    });
    validate(path.name, (ctx) => this.validateName(ctx.value()));
    required(path.connectionString, {
      message: this.transloco.translate('common.validation.required'),
    });
    maxLength(path.connectionString, TENANT_CONNECTION_STRING_MAX_LENGTH, {
      message: this.transloco.translate('common.validation.maxLength', {
        max: TENANT_CONNECTION_STRING_MAX_LENGTH,
      }),
    });
    //#else
    required(path.name, {
      message: 'This field is required.',
      when: () => this.editorMode() === 'add',
    });
    validate(path.name, (ctx) => this.validateName(ctx.value()));
    required(path.connectionString, { message: 'This field is required.' });
    maxLength(path.connectionString, TENANT_CONNECTION_STRING_MAX_LENGTH, {
      message: `Must not exceed ${TENANT_CONNECTION_STRING_MAX_LENGTH} characters.`,
    });
    //#endif
  });

  constructor() {
    effect((onCleanup) => {
      const tenant = this.tenant();
      // 读一次刷新令牌，把"写完重新拉一遍"也并到这条路径上
      this.reloadToken();

      if (!this.open() || !tenant || !this.canManage()) {
        return;
      }

      const subscription = this.loadConnections(tenant.id);

      // 关闭弹窗或换到另一个租户时，取消上一次的连接查询。
      // 不取消的话：打开 A、关掉、打开 B，A 的慢响应会在 B 之后到达，
      // 界面就会把 B 的身份信息配上 A 的连接列表——两个租户的信息拼在一屏。
      onCleanup(() => subscription.unsubscribe());
    });

    // 换租户或开关弹窗时收起编辑器：留着它，下次打开就会把上一个租户的连接名
    // 摆在表单里，还可能带着刚才敲进去一半的连接串。
    effect(() => {
      this.tenant();
      this.open();
      this.closeEditor();
    });
  }

  private loadConnections(tenantId: string): Subscription {
    this.connections.set(null);
    this.connectionsError.set(null);
    this.connectionsLoading.set(true);

    return this.connectionService
      .getConnections(tenantId)
      .pipe(
        tap((connections) => this.connections.set(connections)),
        catchError((error: unknown) => {
          this.connectionsError.set(applicationErrorMessage(error));
          return EMPTY;
        }),
        // 取消订阅时也会走到这里，因此关掉弹窗不会把加载态留在 true
        finalize(() => this.connectionsLoading.set(false)),
      )
      .subscribe();
  }

  onEdit(): void {
    const tenant = this.tenant();
    if (!tenant) {
      return;
    }

    this.open.set(false);
    this.edit.emit(tenant);
  }

  startAdd(): void {
    this.actionError.set(null);
    this.editingConnection.set(null);
    this.editorModel.set({ name: '', connectionString: '' });
    this.editorMode.set('add');
  }

  startEdit(connection: TenantConnectionDto): void {
    this.actionError.set(null);
    this.editingConnection.set(connection);
    // 连接串不预填：接口从不返回它，填一个占位值只会被原样提交回去覆盖真值
    this.editorModel.set({ name: connection.name, connectionString: '' });
    this.editorMode.set('edit');
  }

  closeEditor(): void {
    this.editorMode.set('idle');
    this.editingConnection.set(null);
    // 连接串不留在内存里等下一次打开
    this.editorModel.set({ name: '', connectionString: '' });
  }

  /**
   * 提交登记或改连接串。
   *
   * 连接串留空时这里不做"沿用当前值"：`PUT` 是整串覆盖，后端没有"不传即保留"这一档，
   * 静默跳过提交会让人以为已经保存。所以留空按必填报错、保存按钮禁用，让人看得见没提交。
   */
  submitEditor(): void {
    const tenant = this.tenant();
    if (!tenant || this.editorMode() === 'idle' || this.connectionForm().invalid()) {
      return;
    }

    const editing = this.editingConnection();
    const model = this.editorModel();
    // 名字大小写不敏感：先归一化再提交，免得界面上看着是新的一条、写进去是同一行
    const name = editing ? editing.name : normalizeTenantConnectionName(model.name);
    // 首次登记预期这一条尚不存在（null）；改已有的那条必须带上读到的版本，
    // 否则后端按"忘了带版本"拒绝，而不是让后写者悄悄盖掉别人的改动。
    const expectedVersion = editing ? editing.version : null;

    this.actionError.set(null);
    this.submitting.set(true);
    this.connectionService
      .setConnection(tenant.id, name, {
        expectedVersion,
        connectionString: model.connectionString,
      })
      .pipe(
        finalize(() => this.submitting.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.closeEditor();
          this.reload();
        },
        // 失败时留着编辑器不关：连接串是手敲进来的，关掉等于让人重敲一遍
        error: (error: unknown) => this.actionError.set(applicationErrorMessage(error)),
      });
  }

  async removeConnection(connection: TenantConnectionDto): Promise<void> {
    const tenant = this.tenant();
    if (!tenant) {
      return;
    }

    const confirmed = await this.confirmService.open({
      header: this.deleteConnectionTitle(),
      message: this.deleteConnectionDescription(connection.name),
      confirmText: this.label('delete'),
      variant: 'destructive',
    });

    if (!confirmed) {
      return;
    }

    this.actionError.set(null);
    this.connectionService
      .removeConnection(tenant.id, connection.name, connection.version)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.reload(),
        error: (error: unknown) => this.actionError.set(applicationErrorMessage(error)),
      });
  }

  private reload(): void {
    this.reloadToken.update((token) => token + 1);
  }

  /** 名字只在"添加"档校验：改连接串时名字来自已登记的那条，本就合法。 */
  private validateName(value: string): { kind: string; message: string } | null {
    if (this.editorMode() !== 'add') {
      return null;
    }

    const name = normalizeTenantConnectionName(value);
    if (!name) {
      // 空值归 required 报，这里不重复提示
      return null;
    }

    if (!TENANT_CONNECTION_NAME_PATTERN.test(name)) {
      return { kind: 'connectionNamePattern', message: this.label('connectionNameInvalid') };
    }

    // 同名再"添加"一次会被后端按版本冲突拒掉，在这里就说清楚该走"改连接串"
    if (this.connections()?.some((connection) => connection.name === name)) {
      return { kind: 'connectionNameTaken', message: this.label('connectionNameTaken') };
    }

    return null;
  }

  //#if (IncludeLocalization)
  readonly title = computed(() => {
    this.translationReady();
    return this.transloco.translate('tenants.detailTitle');
  });

  label(field: DetailLabel): string {
    this.translationReady();
    return this.transloco.translate(LABEL_KEYS[field]);
  }

  statusLabel(isActive: boolean): string {
    this.translationReady();
    return this.transloco.translate(isActive ? 'tenants.active' : 'tenants.inactive');
  }

  deleteConnectionTitle(): string {
    return this.transloco.translate('tenants.deleteConnectionTitle');
  }

  deleteConnectionDescription(name: string): string {
    return this.transloco.translate('tenants.deleteConnectionDescription', { name });
  }
  //#else
  // 与本地化分支同样用 computed：两边形态一致，模板里都是 title()，
  // 也让 computed 这个 import 在两种符号取值下都有使用点
  readonly title = computed(() => 'Tenant detail');

  label(field: DetailLabel): string {
    return LABEL_TEXTS[field];
  }

  statusLabel(isActive: boolean): string {
    return isActive ? 'Active' : 'Inactive';
  }

  deleteConnectionTitle(): string {
    return 'Delete connection';
  }

  deleteConnectionDescription(name: string): string {
    return `Delete connection "${name}"? That service will go back to the database it is configured with.`;
  }
  //#endif
}

type DetailLabel =
  | 'name'
  | 'displayName'
  | 'description'
  | 'status'
  | 'created'
  | 'sectionIdentity'
  | 'sectionConnections'
  | 'connectionName'
  | 'connectionString'
  | 'connectionNameInvalid'
  | 'connectionNameTaken'
  | 'connectionsEmpty'
  | 'version'
  | 'secretHint'
  | 'addConnection'
  | 'changeConnectionString'
  | 'shardRequiresInactive'
  | 'delete'
  | 'edit'
  | 'cancel'
  | 'save'
  | 'close';

//#if (IncludeLocalization)
const LABEL_KEYS: Record<DetailLabel, string> = {
  name: 'tenants.fieldName',
  displayName: 'tenants.fieldDisplayName',
  description: 'tenants.fieldDescription',
  status: 'tenants.colStatus',
  created: 'tenants.colCreatedAt',
  sectionIdentity: 'tenants.detailSectionIdentity',
  sectionConnections: 'tenants.detailSectionConnections',
  connectionName: 'tenants.fieldConnectionName',
  connectionString: 'tenants.fieldConnectionString',
  connectionNameInvalid: 'tenants.connectionNameInvalid',
  connectionNameTaken: 'tenants.connectionNameTaken',
  connectionsEmpty: 'tenants.connectionsEmpty',
  version: 'tenants.fieldConnectionVersion',
  secretHint: 'tenants.detailSecretHint',
  addConnection: 'tenants.addConnection',
  changeConnectionString: 'tenants.changeConnectionString',
  shardRequiresInactive: 'tenants.shardRequiresInactive',
  delete: 'common.delete',
  edit: 'common.edit',
  cancel: 'common.cancel',
  save: 'common.save',
  close: 'common.close',
};
//#else
const LABEL_TEXTS: Record<DetailLabel, string> = {
  name: 'Name',
  displayName: 'Display name',
  description: 'Description',
  status: 'Status',
  created: 'Created at',
  sectionIdentity: 'Identity',
  sectionConnections: 'Database connections',
  connectionName: 'Connection name',
  connectionString: 'Connection string',
  connectionNameInvalid: 'Use lowercase letters, digits and hyphens only, 1 to 64 characters.',
  connectionNameTaken:
    'This connection name is already registered; change its connection string instead.',
  connectionsEmpty:
    'This tenant has no database of its own; every service uses the database it is configured with.',
  version: 'Version',
  secretHint:
    'The connection string is stored encrypted and never returned; saving replaces it in full.',
  addConnection: 'Add connection',
  changeConnectionString: 'Change connection string',
  shardRequiresInactive:
    'To give this tenant a database of its own, deactivate it first: its data currently lives in the database each service is configured with, and registering a connection does not move it.',
  delete: 'Delete',
  edit: 'Edit',
  cancel: 'Cancel',
  save: 'Save',
  close: 'Close',
};
//#endif
