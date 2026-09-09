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
import { HlmToggleGroupImports } from '@spartan-ng/helm/toggle-group';

//#if (IncludeLocalization)
import { translationReady } from '../../../../../../core/i18n/translation-ready';
//#endif
import { PASSWORD_RULE } from '../../../../../../core/validation/password-rule';
import {
  CreateTenantInputDto,
  TenantDatabaseMode,
  TenantOutputDto,
  UpdateTenantInputDto,
} from '../../../../../../shared/dtos/tenant.dto';

interface TenantEditFormModel {
  name: string;
  displayName: string;
  description: string;
  adminEmail: string;
  adminPassword: string;
  databaseMode: TenantDatabaseMode;
  runtimeSecretReference: string;
  migrationSecretReference: string;
}

/**
 * 租户新建 / 编辑对话框。
 *
 * 新建时同时提供租户初始管理员的邮箱与密码（由后端在该租户内创建管理员账号）；
 * 编辑只涉及名称与显示名，管理员账号变更走租户内的用户管理。
 */
@Component({
  selector: 'app-tenant-edit-dialog',
  imports: [
    FormField,
    HlmButton,
    HlmInput,
    ...HlmDialogImports,
    ...HlmFieldImports,
    ...HlmToggleGroupImports,
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
  ],
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
  readonly isDedicatedDatabase = computed(
    () => this.formModel().databaseMode === 'dedicatedDatabase',
  );

  protected readonly formModel = signal<TenantEditFormModel>({
    name: '',
    displayName: '',
    description: '',
    adminEmail: '',
    adminPassword: '',
    databaseMode: 'sharedDatabase',
    runtimeSecretReference: '',
    migrationSecretReference: '',
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
    required(path.runtimeSecretReference, {
      message: this.transloco.translate('common.validation.required'),
      when: () => !this.isEdit() && this.isDedicatedDatabase(),
    });
    required(path.migrationSecretReference, {
      message: this.transloco.translate('common.validation.required'),
      when: () => !this.isEdit() && this.isDedicatedDatabase(),
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
    required(path.runtimeSecretReference, {
      message: 'This field is required.',
      when: () => !this.isEdit() && this.isDedicatedDatabase(),
    });
    required(path.migrationSecretReference, {
      message: 'This field is required.',
      when: () => !this.isEdit() && this.isDedicatedDatabase(),
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
        databaseMode: 'sharedDatabase',
        runtimeSecretReference: '',
        migrationSecretReference: '',
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
      databaseMode: model.databaseMode,
      runtimeSecretReference: this.isDedicatedDatabase()
        ? model.runtimeSecretReference.trim()
        : undefined,
      migrationSecretReference: this.isDedicatedDatabase()
        ? model.migrationSecretReference.trim()
        : undefined,
    } satisfies CreateTenantInputDto);
  }

  setDatabaseMode(value: TenantDatabaseMode | TenantDatabaseMode[] | null | undefined): void {
    const databaseMode = Array.isArray(value) ? value[0] : value;
    if (databaseMode !== 'sharedDatabase' && databaseMode !== 'dedicatedDatabase') {
      return;
    }

    this.formModel.update((current) => ({
      ...current,
      databaseMode,
      runtimeSecretReference:
        databaseMode === 'sharedDatabase' ? '' : current.runtimeSecretReference,
      migrationSecretReference:
        databaseMode === 'sharedDatabase' ? '' : current.migrationSecretReference,
    }));
  }

  //#if (IncludeLocalization)
  readonly title = computed(() => {
    this.translationReady();
    return this.transloco.translate(this.isEdit() ? 'tenants.editTitle' : 'tenants.createTitle');
  });

  readonly description = () => this.transloco.translate('tenants.editDescription');
  readonly cancelLabel = () => this.transloco.translate('common.cancel');
  readonly saveLabel = () => this.transloco.translate('common.save');

  fieldLabel(
    field:
      | 'name'
      | 'displayName'
      | 'description'
      | 'adminEmail'
      | 'adminPassword'
      | 'runtimeSecretReference'
      | 'migrationSecretReference',
  ): string {
    const keys = {
      name: 'tenants.fieldName',
      displayName: 'tenants.fieldDisplayName',
      description: 'tenants.fieldDescription',
      adminEmail: 'tenants.fieldAdminEmail',
      adminPassword: 'tenants.fieldAdminPassword',
      runtimeSecretReference: 'tenants.fieldRuntimeSecretReference',
      migrationSecretReference: 'tenants.fieldMigrationSecretReference',
    } as const;
    return this.transloco.translate(keys[field]);
  }
  //#else
  fieldLabel(
    field:
      | 'name'
      | 'displayName'
      | 'description'
      | 'adminEmail'
      | 'adminPassword'
      | 'runtimeSecretReference'
      | 'migrationSecretReference',
  ): string {
    const labels = {
      name: 'Name',
      displayName: 'Display name',
      description: 'Description',
      adminEmail: 'Admin email',
      adminPassword: 'Admin password',
      runtimeSecretReference: 'Runtime Secret reference',
      migrationSecretReference: 'Migration Secret reference',
    } as const;
    return labels[field];
  }

  readonly title = computed(() => (this.isEdit() ? 'Edit tenant' : 'New tenant'));

  readonly description = () =>
    'Name identifies the tenant at sign-in; users enter it to select their tenant.';
  readonly cancelLabel = () => 'Cancel';
  readonly saveLabel = () => 'Save';
  //#endif
}
