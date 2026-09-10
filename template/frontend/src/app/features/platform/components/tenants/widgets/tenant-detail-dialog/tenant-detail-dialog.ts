// prettier-ignore
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  signal,
} from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmSeparator } from '@spartan-ng/helm/separator';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { catchError, EMPTY, finalize, Subscription, tap } from 'rxjs';

import { applicationErrorMessage } from '../../../../../../core/errors/application-http-error';
//#if (IncludeLocalization)
import { translationReady } from '../../../../../../core/i18n/translation-ready';
//#endif
import { SettingContextService } from '../../../../../../core/settings/setting-context-service';
import { TenantConnectionOutputDto } from '../../../../../../shared/dtos/tenant-connection.dto';
import { TenantOutputDto } from '../../../../../../shared/dtos/tenant.dto';
import { AppDate } from '../../../../../../shared/pipes/app-date-pipe';
import { TenantConnectionService } from '../../../../services/tenant-connection-service';

/**
 * 租户详情（只读）。
 *
 * 分两段：**标识**来自列表已有的租户对象，打开即可显示；**数据库放置**要额外请求
 * 连接配置。两段的失败面因此分开——配置请求失败只让配置段降级，标识段照常呈现：
 * 一个副请求失败就把整个详情打空，会让人以为租户本身出了问题。
 *
 * 两个 `*SecretReference` 是密钥**引用名**，接口从不返回连接串（见 DTO 说明）。
 *
 * 连接配置接口要求 `App.Tenants.Update`，所以 {@link canManage} 为假时**不发这个请求**、
 * 也不渲染配置段与编辑按钮：发一个必然 403 的请求只会在控制台留下红字，
 * 再配一个点不动的按钮，比直接不显示更糟。
 */
@Component({
  selector: 'app-tenant-detail-dialog',
  // prettier-ignore
  imports: [
    AppDate,
    HlmButton,
    HlmSeparator,
    HlmSpinner,
    ...HlmDialogImports,
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

  /** 是否有租户更新权限：决定配置段与编辑按钮是否出现。 */
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
  protected readonly displayTimeZone = inject(SettingContextService).timeZone;
  protected readonly displayLocale = inject(SettingContextService).displayLocale;
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  private readonly translationReady = translationReady(this.transloco);
  //#endif

  protected readonly connection = signal<TenantConnectionOutputDto | null>(null);
  protected readonly connectionLoading = signal(false);
  protected readonly connectionError = signal<string | null>(null);

  protected readonly isDedicatedDatabase = computed(
    () => this.connection()?.databaseMode === 'dedicatedDatabase',
  );

  constructor() {
    effect((onCleanup) => {
      const tenant = this.tenant();
      if (!this.open() || !tenant || !this.canManage()) {
        return;
      }

      const subscription = this.loadConnection(tenant.id);

      // 关闭弹窗或换到另一个租户时，取消上一次的连接查询。
      // 不取消的话：打开 A、关掉、打开 B，A 的慢响应会在 B 之后到达，
      // 界面就会把 B 的身份信息配上 A 的数据库模式与密钥引用——两个租户的信息拼在一屏。
      onCleanup(() => subscription.unsubscribe());
    });
  }

  private loadConnection(tenantId: string): Subscription {
    this.connection.set(null);
    this.connectionError.set(null);
    this.connectionLoading.set(true);

    return this.connectionService
      .getConnection(tenantId)
      .pipe(
        tap((connection) => this.connection.set(connection)),
        catchError((error: unknown) => {
          this.connectionError.set(applicationErrorMessage(error));
          return EMPTY;
        }),
        // 取消订阅时也会走到这里，因此关掉弹窗不会把加载态留在 true
        finalize(() => this.connectionLoading.set(false)),
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

  //#if (IncludeLocalization)
  readonly title = computed(() => {
    this.translationReady();
    return this.transloco.translate('tenants.detailTitle');
  });

  label(
    field:
      | 'name'
      | 'displayName'
      | 'description'
      | 'status'
      | 'created'
      | 'databaseMode'
      | 'runtimeSecretReference'
      | 'migrationSecretReference'
      | 'version'
      | 'sectionIdentity'
      | 'sectionPlacement'
      | 'secretHint'
      | 'edit'
      | 'close',
  ): string {
    this.translationReady();
    const keys = {
      name: 'tenants.fieldName',
      displayName: 'tenants.fieldDisplayName',
      description: 'tenants.fieldDescription',
      status: 'tenants.colStatus',
      created: 'tenants.colCreatedAt',
      databaseMode: 'tenants.fieldDatabaseMode',
      runtimeSecretReference: 'tenants.fieldRuntimeSecretReference',
      migrationSecretReference: 'tenants.fieldMigrationSecretReference',
      version: 'tenants.fieldConnectionVersion',
      sectionIdentity: 'tenants.detailSectionIdentity',
      sectionPlacement: 'tenants.detailSectionPlacement',
      secretHint: 'tenants.detailSecretHint',
      edit: 'common.edit',
      close: 'common.close',
    } as const;
    return this.transloco.translate(keys[field]);
  }

  statusLabel(isActive: boolean): string {
    this.translationReady();
    return this.transloco.translate(isActive ? 'tenants.active' : 'tenants.inactive');
  }

  databaseModeLabel(mode: TenantConnectionOutputDto['databaseMode']): string {
    this.translationReady();
    return this.transloco.translate(
      mode === 'dedicatedDatabase' ? 'tenants.dedicatedDatabase' : 'tenants.sharedDatabase',
    );
  }
  //#else
  readonly title = () => 'Tenant detail';

  label(
    field:
      | 'name'
      | 'displayName'
      | 'description'
      | 'status'
      | 'created'
      | 'databaseMode'
      | 'runtimeSecretReference'
      | 'migrationSecretReference'
      | 'version'
      | 'sectionIdentity'
      | 'sectionPlacement'
      | 'secretHint'
      | 'edit'
      | 'close',
  ): string {
    const labels = {
      name: 'Name',
      displayName: 'Display name',
      description: 'Description',
      status: 'Status',
      created: 'Created at',
      databaseMode: 'Database placement',
      runtimeSecretReference: 'Runtime Secret reference',
      migrationSecretReference: 'Migration Secret reference',
      version: 'Connection version',
      sectionIdentity: 'Identity',
      sectionPlacement: 'Database placement',
      secretHint: 'Only Secret reference names are shown; connection strings are never returned.',
      edit: 'Edit',
      close: 'Close',
    } as const;
    return labels[field];
  }

  statusLabel(isActive: boolean): string {
    return isActive ? 'Active' : 'Inactive';
  }

  databaseModeLabel(mode: TenantConnectionOutputDto['databaseMode']): string {
    return mode === 'dedicatedDatabase' ? 'Dedicated database' : 'Shared database';
  }
  //#endif
}
