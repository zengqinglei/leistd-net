import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, signal } from '@angular/core';
//#if (IncludeLocalization)
import { toSignal } from '@angular/core/rxjs-interop';
//#endif
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { AvatarModule } from 'primeng/avatar';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { DividerModule } from 'primeng/divider';
import { FileSelectEvent, FileUploadModule } from 'primeng/fileupload';
import { InputTextModule } from 'primeng/inputtext';
import { MultiSelectModule } from 'primeng/multiselect';
import { PasswordModule } from 'primeng/password';
import { ToggleSwitchModule } from 'primeng/toggleswitch';

import { DialogLoadingComponent } from '../../../../../../shared/components/dialog-loading/dialog-loading';
import { DIALOG_CONFIGS } from '../../../../../../shared/constants/dialog-config.constants';
import { ROLE_LABEL_MAP } from '../../../../../../shared/models/role.enum';
import { CreateUserInputDto, UpdateUserInputDto, UserManagementOutputDto } from '../../../../models/user-management.dto';

const MAX_AVATAR_SIZE = 1024 * 1024;
const PASSWORD_RULE = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).{8,20}$/;

@Component({
  selector: 'app-user-edit-dialog',
  imports: [
    CommonModule,
    ReactiveFormsModule,
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
    DialogModule,
    ButtonModule,
    AvatarModule,
    FileUploadModule,
    InputTextModule,
    PasswordModule,
    ToggleSwitchModule,
    MultiSelectModule,
    DividerModule,
    DialogLoadingComponent
  ],
  templateUrl: './user-edit-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class UserEditDialogComponent {
  visible = model(false);
  loading = input(false);
  saving = input(false);
  user = input<UserManagementOutputDto | null>(null);
  readonly saved = output<CreateUserInputDto | UpdateUserInputDto>();

  private readonly fb = inject(FormBuilder);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  // 追踪活动语言：切换时该 signal 变化 → 依赖它的 computed 重算，文案重新翻译。
  private readonly activeLang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });
  private readonly unnamedLabel = () => this.transloco.translate('users.editDialog.unnamedUser');
  readonly dialogHeader = () =>
    this.transloco.translate(this.isEditMode() ? 'users.editDialog.editHeader' : 'users.editDialog.createHeader');
  readonly rolesPlaceholder = () => this.transloco.translate('users.editDialog.rolesPlaceholder');
  readonly avatarMessages = () => ({
    sizeSummary: this.transloco.translate('users.editDialog.avatarTooLargeSummary'),
    sizeDetail: this.transloco.translate('users.editDialog.avatarTooLargeDetail'),
    typeSummary: this.transloco.translate('users.editDialog.avatarBadTypeSummary'),
    typeDetail: this.transloco.translate('users.editDialog.avatarBadTypeDetail')
  });
  //#else
  private readonly unnamedLabel = () => 'Unnamed user';
  readonly dialogHeader = () => (this.isEditMode() ? 'Edit user' : 'New user');
  readonly rolesPlaceholder = () => 'Select roles';
  readonly avatarMessages = () => ({
    sizeSummary: 'File too large',
    sizeDetail: 'The avatar size cannot exceed 1MB',
    typeSummary: 'Unsupported format',
    typeDetail: 'Please upload a PNG, JPG or WEBP image'
  });
  //#endif

  dialogConfig = DIALOG_CONFIGS.SMALL;
  readonly avatarPreview = signal('');
  readonly displayName = computed(
    () => this.form.controls.displayName.value.trim() || this.form.controls.username.value.trim() || this.unnamedLabel()
  );
  readonly avatarLabel = computed(() => (this.displayName().trim().charAt(0) || 'U').toUpperCase());
  readonly avatarStyle = computed(() => {
    const seed = (this.form.controls.username.value || this.displayName()).trim();
    let total = 0;

    for (const char of seed) {
      total += char.charCodeAt(0);
    }

    const palette = [
      { background: '#dbeafe', color: '#1d4ed8' },
      { background: '#dcfce7', color: '#15803d' },
      { background: '#fef3c7', color: '#b45309' },
      { background: '#fce7f3', color: '#be185d' },
      { background: '#ede9fe', color: '#6d28d9' }
    ];

    return palette[total % palette.length];
  });
  readonly form = this.fb.nonNullable.group({
    username: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(64), Validators.pattern(/^[a-zA-Z0-9_]+$/)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    displayName: ['', [Validators.maxLength(128)]],
    avatar: [''],
    password: ['', [Validators.required, Validators.pattern(PASSWORD_RULE)]],
    isActive: [true],
    isEmailVerified: [false],
    roles: [['Member'], [Validators.required]]
  });

  //#if (IncludeLocalization)
  // 本地化模式：ROLE_LABEL_MAP 值是词条键，读 activeLang 建立依赖，语言切换时 computed 重算，标签重新翻译。
  readonly roleOptions = computed(() => {
    this.activeLang();
    return Object.entries(ROLE_LABEL_MAP).map(([value, label]) => ({ label: this.transloco.translate(label), value }));
  });
  //#else
  readonly roleOptions = computed(() => Object.entries(ROLE_LABEL_MAP).map(([value, label]) => ({ label, value })));
  //#endif

  constructor() {
    // 同时依赖 visible 与 user：每次对话框打开都重置表单，避免新建模式残留上次输入
    // （user 信号从 null 到 null 不变化时 effect 不会重跑，需借 visible 触发）
    effect(() => {
      const user = this.user();
      if (!this.visible()) {
        return;
      }
      const avatar = user?.avatar ?? '';
      this.form.reset({
        username: user?.username ?? '',
        email: user?.email ?? '',
        displayName: user?.displayName ?? '',
        avatar,
        password: '',
        isActive: user?.isActive ?? true,
        isEmailVerified: user?.isEmailVerified ?? false,
        roles: user ? [...user.roles] : ['Member']
      });
      this.avatarPreview.set(avatar);

      if (user) {
        this.form.controls.username.disable();
        this.form.controls.password.disable();
      } else {
        this.form.controls.username.enable();
        this.form.controls.password.enable();
      }
    });
  }

  isEditMode() {
    return !!this.user();
  }

  hasAvatarImage() {
    const avatar = this.avatarPreview();
    return avatar.startsWith('data:image/') || avatar.startsWith('http://') || avatar.startsWith('https://');
  }

  onAvatarSelect(event: FileSelectEvent) {
    const file = event.files?.[0];

    if (!file) {
      return;
    }

    const reader = new FileReader();
    reader.onload = () => {
      const result = typeof reader.result === 'string' ? reader.result : '';
      this.form.controls.avatar.setValue(result);
      this.avatarPreview.set(result);
    };
    reader.readAsDataURL(file);
  }

  hasPasswordRuleError() {
    const control = this.form.controls.password;
    return control.touched && control.hasError('pattern');
  }

  onHide() {
    this.visible.set(false);
  }

  save() {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const model = this.form.getRawValue();
    if (this.isEditMode()) {
      this.saved.emit({
        email: model.email.trim(),
        displayName: model.displayName.trim() || undefined,
        avatar: model.avatar.trim() || undefined,
        isActive: model.isActive,
        isEmailVerified: model.isEmailVerified,
        roles: model.roles
      });
      return;
    }

    this.saved.emit({
      username: model.username.trim(),
      email: model.email.trim(),
      displayName: model.displayName.trim() || undefined,
      avatar: model.avatar.trim() || undefined,
      password: model.password,
      isActive: model.isActive,
      isEmailVerified: model.isEmailVerified,
      roles: model.roles
    });
  }

  protected readonly maxAvatarSize = MAX_AVATAR_SIZE;
}
