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
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';

//#if (IncludeLocalization)
import { translationReady } from '../../../../../../core/i18n/translation-ready';
//#endif
import {
  CreateTenantInputDto,
  TenantOutputDto,
  UpdateTenantInputDto,
} from '../../../../../../shared/dtos/tenant.dto';

const PASSWORD_RULE = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).{8,20}$/;

interface TenantEditFormModel {
  name: string;
  displayName: string;
  adminEmail: string;
  adminPassword: string;
}

/**
 * 租户新建 / 编辑对话框。
 *
 * 新建时同时提供租户初始管理员的邮箱与密码（由后端在该租户内创建管理员账号）；
 * 编辑只涉及名称与显示名，管理员账号变更走租户内的用户管理。
 */
@Component({
  selector: 'app-tenant-edit-dialog',
  imports: [FormField, HlmButton, HlmInput, ...HlmDialogImports, ...HlmFieldImports],
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
    adminEmail: '',
    adminPassword: '',
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
    //#else
    required(path.name, { message: 'This field is required.' });
    maxLength(path.name, 64, { message: 'Must not exceed 64 characters.' });
    maxLength(path.displayName, 128, { message: 'Must not exceed 128 characters.' });
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
        'Password must be 8–20 characters and include uppercase, lowercase, digits, and special characters.',
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
        adminEmail: '',
        adminPassword: '',
      });
    });
  }

  onSubmit(): void {
    if (this.tenantForm().invalid()) {
      return;
    }

    const model = this.formModel();
    const displayName = model.displayName.trim() || undefined;

    if (this.isEdit()) {
      this.save.emit({
        name: model.name.trim(),
        displayName,
      } satisfies UpdateTenantInputDto);
      return;
    }

    this.save.emit({
      name: model.name.trim(),
      displayName,
      adminEmail: model.adminEmail.trim(),
      adminPassword: model.adminPassword,
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

  fieldLabel(field: 'name' | 'displayName' | 'adminEmail' | 'adminPassword'): string {
    const keys = {
      name: 'tenants.fieldName',
      displayName: 'tenants.fieldDisplayName',
      adminEmail: 'tenants.fieldAdminEmail',
      adminPassword: 'tenants.fieldAdminPassword',
    } as const;
    return this.transloco.translate(keys[field]);
  }
  //#else
  fieldLabel(field: 'name' | 'displayName' | 'adminEmail' | 'adminPassword'): string {
    const labels = {
      name: 'Name',
      displayName: 'Display name',
      adminEmail: 'Admin email',
      adminPassword: 'Admin password',
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
