//#if (IncludeLocalization)
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
//#else
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
//#endif
import {
  form,
  required,
  email as emailValidator,
  minLength,
  maxLength,
  pattern,
  disabled,
  FormField,
} from '@angular/forms/signals';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideImagePlus, lucideEye, lucideEyeOff } from '@ng-icons/lucide';
import { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import {
  HlmInputGroup,
  HlmInputGroupInput,
  HlmInputGroupButton,
} from '@spartan-ng/helm/input-group';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmSeparator } from '@spartan-ng/helm/separator';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmSwitch } from '@spartan-ng/helm/switch';

//#if (IncludeLocalization)
import { translationReady } from '../../../../../../core/i18n/translation-ready';
//#endif
import { DialogLoading } from '../../../../../../shared/components/dialog-loading/dialog-loading';
import { ROLE_LABEL_MAP } from '../../../../../../shared/models/role.enum';
import {
  CreateUserInputDto,
  UpdateUserInputDto,
  UserManagementOutputDto,
} from '../../../../models/user-management.dto';

const MAX_AVATAR_SIZE = 1024 * 1024;
const ACCEPTED_AVATAR_TYPES = ['image/png', 'image/jpeg', 'image/webp'];
const PASSWORD_RULE = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).{8,20}$/;

@Component({
  selector: 'app-user-edit-dialog',
  standalone: true,
  imports: [
    FormField,
    NgIcon,
    HlmButton,
    HlmSpinner,
    HlmInput,
    HlmInputGroup,
    HlmInputGroupInput,
    HlmInputGroupButton,
    HlmSwitch,
    HlmSeparator,
    ...HlmDialogImports,
    ...HlmFieldImports,
    ...HlmSelectImports,
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
    DialogLoading,
  ],
  providers: [provideIcons({ lucideImagePlus, lucideEye, lucideEyeOff })],
  templateUrl: './user-edit-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserEditDialog {
  readonly visible = model(false);
  readonly loading = input(false);
  readonly saving = input(false);
  readonly user = input<UserManagementOutputDto | null>(null);
  readonly saved = output<CreateUserInputDto | UpdateUserInputDto>();

  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  // 追踪「翻译就绪」：资源加载完成与语言切换时重算，含首帧避免裸键。
  private readonly translationReady = translationReady(this.transloco);
  private readonly unnamedLabel = () => this.transloco.translate('users.editDialog.unnamedUser');
  readonly dialogHeader = () =>
    this.transloco.translate(
      this.isEditMode() ? 'users.editDialog.editHeader' : 'users.editDialog.createHeader',
    );
  readonly rolesPlaceholder = () => this.transloco.translate('users.editDialog.rolesPlaceholder');
  private readonly avatarMessages = () => ({
    sizeSummary: this.transloco.translate('users.editDialog.avatarTooLargeSummary'),
    sizeDetail: this.transloco.translate('users.editDialog.avatarTooLargeDetail'),
    typeSummary: this.transloco.translate('users.editDialog.avatarBadTypeSummary'),
    typeDetail: this.transloco.translate('users.editDialog.avatarBadTypeDetail'),
  });
  //#else
  private readonly unnamedLabel = () => 'Unnamed user';
  readonly dialogHeader = () => (this.isEditMode() ? 'Edit user' : 'New user');
  readonly rolesPlaceholder = () => 'Select roles';
  private readonly avatarMessages = () => ({
    sizeSummary: 'File too large',
    sizeDetail: 'The avatar size cannot exceed 1MB',
    typeSummary: 'Unsupported format',
    typeDetail: 'Please upload a PNG, JPG or WEBP image',
  });
  //#endif

  readonly avatarPreview = signal('');

  // 表单模型（Signal Forms）
  protected readonly formModel = signal({
    username: '',
    email: '',
    displayName: '',
    avatar: '',
    password: '',
    isActive: true,
    isEmailVerified: false,
    roles: ['Member'] as string[],
  });

  readonly displayName = computed(
    () =>
      this.formModel().displayName.trim() ||
      this.formModel().username.trim() ||
      this.unnamedLabel(),
  );
  readonly avatarLabel = computed(() => (this.displayName().trim().charAt(0) || 'U').toUpperCase());

  //#if (IncludeLocalization)
  readonly userForm = form(this.formModel, (path) => {
    required(path.username, {
      message: this.transloco.translate('common.validation.required'),
    });
    minLength(path.username, 3, {
      message: this.transloco.translate('common.validation.usernamePattern'),
    });
    maxLength(path.username, 64, {
      message: this.transloco.translate('common.validation.usernamePattern'),
    });
    pattern(path.username, /^[a-zA-Z0-9_]+$/, {
      message: this.transloco.translate('common.validation.usernamePattern'),
    });
    // 编辑模式禁用用户名（不可改）。
    disabled(path.username, { when: () => this.isEditMode() });
    required(path.email, { message: this.transloco.translate('common.validation.required') });
    emailValidator(path.email, {
      message: this.transloco.translate('common.validation.email'),
    });
    maxLength(path.email, 256, { message: '' });
    maxLength(path.displayName, 128, {
      message: this.transloco.translate('common.validation.maxLength', { max: 128 }),
    });
    // 初始密码仅在新建模式校验（编辑模式无密码字段）。
    required(path.password, {
      message: this.transloco.translate('common.validation.required'),
      when: () => !this.isEditMode(),
    });
    pattern(path.password, PASSWORD_RULE, {
      message: this.transloco.translate('common.validation.passwordRule'),
      when: () => !this.isEditMode(),
    });
    required(path.roles, { message: this.transloco.translate('common.validation.required') });
  });
  //#else
  readonly userForm = form(this.formModel, (path) => {
    required(path.username, { message: 'This field is required.' });
    minLength(path.username, 3, {
      message: 'Must be 3–64 letters, digits, or underscores.',
    });
    maxLength(path.username, 64, {
      message: 'Must be 3–64 letters, digits, or underscores.',
    });
    pattern(path.username, /^[a-zA-Z0-9_]+$/, {
      message: 'Must be 3–64 letters, digits, or underscores.',
    });
    // 编辑模式禁用用户名（不可改）。
    disabled(path.username, { when: () => this.isEditMode() });
    required(path.email, { message: 'This field is required.' });
    emailValidator(path.email, { message: 'Please enter a valid email address.' });
    maxLength(path.email, 256, { message: '' });
    maxLength(path.displayName, 128, { message: 'Must not exceed 128 characters.' });
    // 初始密码仅在新建模式校验（编辑模式无密码字段）。
    required(path.password, {
      message: 'This field is required.',
      when: () => !this.isEditMode(),
    });
    pattern(path.password, PASSWORD_RULE, {
      message:
        'Password must be 8–20 characters and include uppercase, lowercase, digits, and special characters.',
      when: () => !this.isEditMode(),
    });
    required(path.roles, { message: 'This field is required.' });
  });
  //#endif

  //#if (IncludeLocalization)
  // 本地化模式：ROLE_LABEL_MAP 值是词条键，读 translationReady 建立依赖，资源就绪 / 语言切换时 computed 重算，标签重新翻译。
  readonly roleOptions = computed(() => {
    this.translationReady();
    return Object.entries(ROLE_LABEL_MAP).map(([value, label]) => ({
      label: this.transloco.translate(label),
      value,
    }));
  });
  //#else
  readonly roleOptions = computed(() =>
    Object.entries(ROLE_LABEL_MAP).map(([value, label]) => ({ label, value })),
  );
  //#endif

  // 角色值 → 标签，用于 hlm-select-multiple 触发器回显。
  readonly roleLabel = (value: string): string =>
    this.roleOptions().find((option) => option.value === value)?.label ?? value;

  constructor() {
    // 同时依赖 visible 与 user：每次对话框打开都重置表单，避免新建模式残留上次输入
    // （user 信号从 null 到 null 不变化时 effect 不会重跑，需借 visible 触发）
    effect(() => {
      const user = this.user();
      if (!this.visible()) {
        return;
      }
      const avatar = user?.avatar ?? '';
      this.formModel.set({
        username: user?.username ?? '',
        email: user?.email ?? '',
        displayName: user?.displayName ?? '',
        avatar,
        password: '',
        isActive: user?.isActive ?? true,
        isEmailVerified: user?.isEmailVerified ?? false,
        roles: user ? [...user.roles] : ['Member'],
      });
      this.avatarPreview.set(avatar);
    });
  }

  isEditMode() {
    return !!this.user();
  }

  hasAvatarImage() {
    const avatar = this.avatarPreview();
    return (
      avatar.startsWith('data:image/') ||
      avatar.startsWith('http://') ||
      avatar.startsWith('https://')
    );
  }

  onAvatarSelect(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) {
      return;
    }

    const messages = this.avatarMessages();

    if (!ACCEPTED_AVATAR_TYPES.includes(file.type)) {
      toast.error(messages.typeSummary, { description: messages.typeDetail });
      input.value = '';
      return;
    }

    if (file.size > MAX_AVATAR_SIZE) {
      toast.error(messages.sizeSummary, { description: messages.sizeDetail });
      input.value = '';
      return;
    }

    const reader = new FileReader();
    reader.onload = () => {
      const result = typeof reader.result === 'string' ? reader.result : '';
      this.formModel.update((m) => ({ ...m, avatar: result }));
      this.avatarPreview.set(result);
    };
    reader.readAsDataURL(file);

    // 允许再次选择同一文件时仍触发 change 事件
    input.value = '';
  }

  // hlm-switch 为 CVA（checked 非 ModelSignal），Signal Forms 的 [formField] 不适配；
  // 直接以 [checked]/(checkedChange) 双向绑定回写模型信号。
  setActive(checked: boolean): void {
    this.formModel.update((m) => ({ ...m, isActive: checked }));
  }

  setEmailVerified(checked: boolean): void {
    this.formModel.update((m) => ({ ...m, isEmailVerified: checked }));
  }

  /** 桥接 hlm-dialog 声明式 state 到对外 visible 契约。 */
  onDialogStateChange(state: BrnDialogState): void {
    this.visible.set(state === 'open');
  }

  onHide() {
    this.visible.set(false);
  }

  save() {
    if (this.userForm().invalid()) {
      this.userForm().markAsTouched();
      return;
    }

    const model = this.formModel();
    if (this.isEditMode()) {
      this.saved.emit({
        email: model.email.trim(),
        displayName: model.displayName.trim() || undefined,
        avatar: model.avatar.trim() || undefined,
        isActive: model.isActive,
        isEmailVerified: model.isEmailVerified,
        roles: model.roles,
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
      roles: model.roles,
    });
  }

  protected readonly showPassword = signal(false);
}
