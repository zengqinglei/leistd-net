import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  input,
  model,
  output,
  signal,
} from '@angular/core';
import {
  FormField,
  minLength,
  email as emailValidator,
  form,
  maxLength,
  required,
  validate,
} from '@angular/forms/signals';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';

import {
  PASSWORD_MAX_LENGTH,
  PASSWORD_MIN_LENGTH,
} from '../../../../../../core/validation/password-rule';
import {
  TENANT_CONNECTION_STRING_MAX_LENGTH,
  TENANT_DEFAULT_CONNECTION_NAME,
} from '../../../../../../shared/dtos/tenant-connection.dto';
import {
  CreateTenantInputDto,
  TENANT_NAME_MAX_LENGTH,
  TENANT_NAME_PATTERN,
  TenantOutputDto,
  UpdateTenantInputDto,
} from '../../../../../../shared/dtos/tenant.dto';
//#if (!IncludeLocalization)
import { englishText } from '../../../../../../shared/utils/english-text';
//#endif

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
  // 两支各写一遍而不是在数组里插条件项：剔掉 TranslocoDirective 之后这一行只有 84 字符，
  // prettier 会要求压成一行，而带上它就超过 100 必须换行——同一份写法满足不了两种取值
  //#if (IncludeLocalization)
  imports: [
    FormField,
    HlmButton,
    HlmInput,
    ...HlmDialogImports,
    ...HlmFieldImports,
    TranslocoDirective,
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
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif

  readonly isEdit = computed(() => this.tenant() !== null);

  /** 编辑时名称与原名逐字相同（不 trim）：视为没改名。 */
  private isUnchangedName(value: string): boolean {
    return this.isEdit() && value === this.tenant()?.name;
  }

  protected readonly formModel = signal<TenantEditFormModel>({
    name: '',
    displayName: '',
    description: '',
    adminEmail: '',
    adminPassword: '',
    connectionString: '',
  });

  readonly tenantForm = form(this.formModel, (path) => {
    required(path.name);
    // 编辑时名称原样未改（按原始字符串比，不先 trim）就不按新规则校验、提交时也原样送回：
    // 存量租户的名称可能早于这条规则（含首尾空白、64 个字符），只改显示名或描述不该被它挡住（与后端一致）
    maxLength(path.name, TENANT_NAME_MAX_LENGTH, {
      when: (ctx) => !this.isUnchangedName(ctx.value()),
    });
    // 按提交时的值（去掉首尾空白）校验；空值归 required、超长归 maxLength，这里不重复提示。
    validate(path.name, (ctx) => {
      if (this.isUnchangedName(ctx.value())) {
        return null;
      }
      const name = ctx.value().trim();
      return name && name.length <= TENANT_NAME_MAX_LENGTH && !TENANT_NAME_PATTERN.test(name)
        ? { kind: 'tenantNamePattern' }
        : null;
    });
    maxLength(path.displayName, 128);
    maxLength(path.description, 256);
    // 管理员账号仅在新建模式提供并校验（编辑模式无该字段）。
    required(path.adminEmail, { when: () => !this.isEdit() });
    emailValidator(path.adminEmail, { when: () => !this.isEdit() });
    required(path.adminPassword, { when: () => !this.isEdit() });
    minLength(path.adminPassword, PASSWORD_MIN_LENGTH, { when: () => !this.isEdit() });
    maxLength(path.adminPassword, PASSWORD_MAX_LENGTH, { when: () => !this.isEdit() });
    // 连接串可选，只钉长度上限（常量与详情页的连接编辑器同源）
    maxLength(path.connectionString, TENANT_CONNECTION_STRING_MAX_LENGTH, {
      when: () => !this.isEdit(),
    });
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
        name: this.isUnchangedName(model.name) ? model.name : model.name.trim(),
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
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'tenants.editTitle': 'Edit tenant',
  'tenants.createTitle': 'New tenant',
  'tenants.editDescription':
    'Name identifies the tenant at sign-in; users enter it to select their tenant.',
  'tenants.fieldName': 'Name',
  'tenants.fieldDisplayName': 'Display name',
  'tenants.fieldDescription': 'Description',
  'tenants.fieldAdminEmail': 'Admin email',
  'tenants.fieldAdminPassword': 'Admin password',
  'tenants.fieldConnectionString': 'Connection string',
  'tenants.createConnectionHint':
    'Optional. Leave empty to keep this tenant in the database each service is already configured with. Sharding can only be decided here: the database must already exist and be migrated, and it cannot be changed afterwards without migrating the data.',
  'common.cancel': 'Cancel',
  'common.save': 'Save',
};
//#endif
