// prettier-ignore
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  //#if (IncludeLocalization)
  inject,
  //#endif
  input,
  model,
  output,
  signal,
} from '@angular/core';
import {
  FormField,
  email as emailValidator,
  form,
  maxLength,
  pattern,
  required,
} from '@angular/forms/signals';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';

//#if (IncludeLocalization)
import { translationReady } from '../../../../../../core/i18n/translation-ready';
//#endif
import { PASSWORD_RULE } from '../../../../../../core/validation/password-rule';
import {
  TENANT_CONNECTION_STRING_MAX_LENGTH,
  TENANT_DEFAULT_CONNECTION_NAME,
} from '../../../../../../shared/dtos/tenant-connection.dto';
import {
  CreateTenantInputDto,
  TenantOutputDto,
  UpdateTenantInputDto,
} from '../../../../../../shared/dtos/tenant.dto';

interface TenantEditFormModel {
  name: string;
  displayName: string;
  description: string;
  adminEmail: string;
  adminPassword: string;
  connectionString: string;
}

/**
 * 租户新建 / 编辑对话框。
 *
 * 新建时同时提供租户初始管理员的邮箱与密码（由后端在该租户内创建管理员账号）；
 * 编辑只涉及名称与显示名，管理员账号变更走租户内的用户管理。
 *
 * **分库只在新建时定案**，所以连接串是新建表单上的一个可选字段：填了就登记到默认名下，
 * 后端在播种之前完成登记，种子（含租户管理员）因此直接落进那个库；留空即不分库。
 * 建好之后再想分库，后端会以 409 拒绝——那时数据已经在回落库里，登记连接不会把它们搬过去，
 * 只能走停用 → 迁移数据 → 登记 → 重新启用。详情里的连接列表管的是另一件事：
 * 给**已经分库**的租户按服务补登一条或换连接串。
 */
@Component({
  selector: 'app-tenant-edit-dialog',
  // 两支各写一遍而不是在数组里插条件项：剔掉 TranslocoModule 之后这一行只有 84 字符，
  // prettier 会要求压成一行，而带上它就超过 100 必须换行——同一份写法满足不了两种取值
  //#if (IncludeLocalization)
  imports: [
    FormField,
    HlmButton,
    HlmInput,
    ...HlmDialogImports,
    ...HlmFieldImports,
    TranslocoModule,
  ],
  //#else
  imports: [FormField, HlmButton, HlmInput, ...HlmDialogImports, ...HlmFieldImports],
  //#endif
  templateUrl: './tenant-edit-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TenantEditDialog {
  readonly open = model(false);
  readonly tenant = input<TenantOutputDto | null>(null);
  readonly save = output<CreateTenantInputDto | UpdateTenantInputDto>();
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  private readonly translationReady = translationReady(this.transloco);
  //#endif

  readonly isEdit = computed(() => this.tenant() !== null);

  protected readonly formModel = signal<TenantEditFormModel>({
    name: '',
    displayName: '',
    description: '',
    adminEmail: '',
    adminPassword: '',
    connectionString: '',
  });

  readonly tenantForm = form(this.formModel, (path) => {
    //#if (IncludeLocalization)
    required(path.name, { message: this.transloco.translate('common.validation.required') });
    maxLength(path.name, 64, {
      message: this.transloco.translate('common.validation.maxLength', { max: 64 }),
    });
    maxLength(path.displayName, 128, {
      message: this.transloco.translate('common.validation.maxLength', { max: 128 }),
    });
    maxLength(path.description, 256, {
      message: this.transloco.translate('common.validation.maxLength', { max: 256 }),
    });
    // 管理员账号仅在新建模式提供并校验（编辑模式无该字段）。
    required(path.adminEmail, {
      message: this.transloco.translate('common.validation.required'),
      when: () => !this.isEdit(),
    });
    emailValidator(path.adminEmail, {
      message: this.transloco.translate('common.validation.email'),
      when: () => !this.isEdit(),
    });
    required(path.adminPassword, {
      message: this.transloco.translate('common.validation.required'),
      when: () => !this.isEdit(),
    });
    pattern(path.adminPassword, PASSWORD_RULE, {
      message: this.transloco.translate('common.validation.passwordRule'),
      when: () => !this.isEdit(),
    });
    // 连接串可选，只钉长度上限（常量与详情页的连接编辑器同源）
    maxLength(path.connectionString, TENANT_CONNECTION_STRING_MAX_LENGTH, {
      message: this.transloco.translate('common.validation.maxLength', {
        max: TENANT_CONNECTION_STRING_MAX_LENGTH,
      }),
      when: () => !this.isEdit(),
    });
    //#else
    required(path.name, { message: 'This field is required.' });
    maxLength(path.name, 64, { message: 'Must not exceed 64 characters.' });
    maxLength(path.displayName, 128, { message: 'Must not exceed 128 characters.' });
    maxLength(path.description, 256, { message: 'Must not exceed 256 characters.' });
    // 管理员账号仅在新建模式提供并校验（编辑模式无该字段）。
    required(path.adminEmail, {
      message: 'This field is required.',
      when: () => !this.isEdit(),
    });
    emailValidator(path.adminEmail, {
      message: 'Please enter a valid email address.',
      when: () => !this.isEdit(),
    });
    required(path.adminPassword, {
      message: 'This field is required.',
      when: () => !this.isEdit(),
    });
    pattern(path.adminPassword, PASSWORD_RULE, {
      message:
        'Password must be at least 12 characters (up to 256). A longer passphrase is stronger than a short complex one.',
      when: () => !this.isEdit(),
    });
    // 连接串可选，只钉长度上限（常量与详情页的连接编辑器同源）
    maxLength(path.connectionString, TENANT_CONNECTION_STRING_MAX_LENGTH, {
      message: `Must not exceed ${TENANT_CONNECTION_STRING_MAX_LENGTH} characters.`,
      when: () => !this.isEdit(),
    });
    //#endif
  });

  constructor() {
    effect(() => {
      // 打开时按当前租户重置表单：新建走空白模型，编辑回填既有值。
      if (!this.open()) {
        return;
      }

      const tenant = this.tenant();
      this.formModel.set({
        name: tenant?.name ?? '',
        displayName: tenant?.displayName ?? '',
        description: tenant?.description ?? '',
        adminEmail: '',
        adminPassword: '',
        // 连接串不留在内存里等下一次打开：它和口令同级
        connectionString: '',
      });
    });
  }

  onSubmit(): void {
    if (this.tenantForm().invalid()) {
      return;
    }

    const model = this.formModel();
    const displayName = model.displayName.trim() || undefined;
    // 清空描述要能传达到后端：编辑时传 null 而不是 undefined——
    // undefined 会被 JSON 序列化丢掉，后端读到的是"未提供"，旧值就留在库里；
    // 传 null 后端会把字段置空（而不是存成空字符串）。
    const description = model.description.trim();

    if (this.isEdit()) {
      this.save.emit({
        name: model.name.trim(),
        displayName,
        description: description || null,
      } satisfies UpdateTenantInputDto);
      return;
    }

    this.save.emit({
      name: model.name.trim(),
      displayName,
      description: description || undefined,
      adminEmail: model.adminEmail.trim(),
      adminPassword: model.adminPassword,
      // 留空即不分库：传空数组而不是空串条目。界面只收默认库一条，
      // 契约本身支持多条命名连接（多服务部署时一次登记 default、crm……）
      connections: model.connectionString.trim()
        ? [
            {
              name: TENANT_DEFAULT_CONNECTION_NAME,
              connectionString: model.connectionString.trim(),
            },
          ]
        : [],
    } satisfies CreateTenantInputDto);
  }

  //#if (IncludeLocalization)
  readonly title = computed(() => {
    this.translationReady();
    return this.transloco.translate(this.isEdit() ? 'tenants.editTitle' : 'tenants.createTitle');
  });

  readonly description = () => this.transloco.translate('tenants.editDescription');
  readonly cancelLabel = () => this.transloco.translate('common.cancel');
  readonly saveLabel = () => this.transloco.translate('common.save');
  readonly connectionHint = () => this.transloco.translate('tenants.createConnectionHint');

  fieldLabel(
    field:
      'name' | 'displayName' | 'description' | 'adminEmail' | 'adminPassword' | 'connectionString',
  ): string {
    const keys = {
      name: 'tenants.fieldName',
      displayName: 'tenants.fieldDisplayName',
      description: 'tenants.fieldDescription',
      adminEmail: 'tenants.fieldAdminEmail',
      adminPassword: 'tenants.fieldAdminPassword',
      connectionString: 'tenants.fieldConnectionString',
    } as const;
    return this.transloco.translate(keys[field]);
  }
  //#else
  fieldLabel(
    field:
      'name' | 'displayName' | 'description' | 'adminEmail' | 'adminPassword' | 'connectionString',
  ): string {
    const labels = {
      name: 'Name',
      displayName: 'Display name',
      description: 'Description',
      adminEmail: 'Admin email',
      adminPassword: 'Admin password',
      connectionString: 'Connection string',
    } as const;
    return labels[field];
  }

  readonly title = computed(() => (this.isEdit() ? 'Edit tenant' : 'New tenant'));

  readonly description = () =>
    'Name identifies the tenant at sign-in; users enter it to select their tenant.';
  readonly cancelLabel = () => 'Cancel';
  readonly saveLabel = () => 'Save';
  readonly connectionHint = () =>
    'Optional. Leave empty to keep this tenant in the database each service is already configured with. ' +
    'Sharding can only be decided here: the database must already exist and be migrated, and it cannot be changed afterwards without migrating the data.';
  //#endif
}
