import { ChangeDetectionStrategy, Component, computed, inject, model, signal } from '@angular/core';
import {
  form,
  required,
  email as emailValidator,
  maxLength,
  pattern,
  FormField,
} from '@angular/forms/signals';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideImagePlus } from '@ng-icons/lucide';
import { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { finalize } from 'rxjs/operators';

import { applicationErrorMessage } from '../../../../core/errors/application-http-error';
import { AuthService } from '../../../../core/services/auth-service';
import { AccountService } from '../../services/account-service';

const MAX_AVATAR_SIZE = 1024 * 1024;
const ACCEPTED_AVATAR_TYPES = ['image/png', 'image/jpeg', 'image/webp'];
const PHONE_PATTERN = /^[0-9+\-()\s]{0,20}$/;

@Component({
  selector: 'app-profile-settings-dialog',
  standalone: true,
  imports: [
    FormField,
    NgIcon,
    HlmButton,
    HlmSpinner,
    HlmInput,
    HlmBadge,
    ...HlmDialogImports,
    ...HlmFieldImports,
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
  ],
  providers: [provideIcons({ lucideImagePlus })],
  templateUrl: './profile-settings-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProfileSettingsDialog {
  readonly visible = model(false);

  private readonly authService = inject(AuthService);
  private readonly accountService = inject(AccountService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  private readonly guestLabel = () => this.transloco.translate('account.profile.guestUser');
  readonly dialogHeader = () => this.transloco.translate('account.profile.header');
  private readonly uploadMessages = () => ({
    sizeSummary: this.transloco.translate('account.profile.invalidFileSizeSummary'),
    sizeDetail: this.transloco.translate('account.profile.invalidFileSizeDetail'),
    typeSummary: this.transloco.translate('account.profile.invalidFileTypeSummary'),
    typeDetail: this.transloco.translate('account.profile.invalidFileTypeDetail'),
  });
  //#else
  private readonly guestLabel = () => 'Guest user';
  readonly dialogHeader = () => 'Profile';
  private readonly uploadMessages = () => ({
    sizeSummary: 'File too large',
    sizeDetail: 'The avatar size cannot exceed 1MB',
    typeSummary: 'Unsupported format',
    typeDetail: 'Please upload a PNG, JPG, or WEBP image',
  });
  //#endif

  readonly saving = signal(false);
  readonly user = computed(() => this.authService.currentUser());

  //#if (LocalAuthorization)
  /** 角色徽章直接展示后端返回的角色名，不再依赖前端硬编码的角色枚举与标签映射。 */
  readonly roleLabels = computed(() => this.authService.currentUser()?.roles ?? []);
  //#endif
  readonly avatarPreview = signal('');

  // 表单模型（Signal Forms）
  protected readonly formModel = signal({
    username: '',
    email: '',
    displayName: '',
    phoneNumber: '',
    avatar: '',
  });

  readonly displayName = computed(
    () =>
      this.formModel().displayName.trim() ||
      this.user()?.displayName ||
      this.user()?.username ||
      this.guestLabel(),
  );

  //#if (IncludeLocalization)
  readonly profileForm = form(this.formModel, (path) => {
    required(path.username, {
      message: this.transloco.translate('common.validation.required'),
    });
    pattern(path.username, /^[a-zA-Z0-9_]{3,64}$/, {
      message: this.transloco.translate('common.validation.usernamePattern'),
    });
    required(path.email, { message: this.transloco.translate('common.validation.required') });
    emailValidator(path.email, {
      message: this.transloco.translate('common.validation.email'),
    });
    maxLength(path.email, 256, { message: '' });
    maxLength(path.displayName, 128, {
      message: this.transloco.translate('common.validation.maxLength', { max: 128 }),
    });
    maxLength(path.phoneNumber, 20, {
      message: this.transloco.translate('common.validation.maxLength', { max: 20 }),
    });
    pattern(path.phoneNumber, PHONE_PATTERN, {
      message: this.transloco.translate('common.validation.phonePattern'),
    });
  });
  //#else
  readonly profileForm = form(this.formModel, (path) => {
    required(path.username, { message: 'This field is required.' });
    pattern(path.username, /^[a-zA-Z0-9_]{3,64}$/, {
      message: 'Must be 3–64 letters, digits, or underscores.',
    });
    required(path.email, { message: 'This field is required.' });
    emailValidator(path.email, {
      message: 'Please enter a valid email address.',
    });
    maxLength(path.email, 256, { message: '' });
    maxLength(path.displayName, 128, { message: 'Must not exceed 128 characters.' });
    maxLength(path.phoneNumber, 20, { message: 'Must not exceed 20 characters.' });
    pattern(path.phoneNumber, PHONE_PATTERN, {
      message: 'Only digits, spaces, and + - ( ) are allowed.',
    });
  });
  //#endif

  /** 桥接 hlm-dialog 声明式 state 到对外 visible 契约；打开时用当前用户回填表单。 */
  onDialogStateChange(state: BrnDialogState): void {
    const open = state === 'open';
    this.visible.set(open);
    if (open) {
      const user = this.user();
      const avatar = user?.avatar ?? '';
      this.formModel.set({
        username: user?.username ?? '',
        email: user?.email ?? '',
        displayName: user?.displayName ?? '',
        phoneNumber: user?.phoneNumber ?? '',
        avatar,
      });
      this.avatarPreview.set(avatar);
    }
  }

  hasAvatarImage(): boolean {
    const avatar = this.avatarPreview();
    return (
      avatar.startsWith('data:image/') ||
      avatar.startsWith('http://') ||
      avatar.startsWith('https://')
    );
  }

  onAvatarSelect(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) {
      return;
    }

    const messages = this.uploadMessages();

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

  onHide(): void {
    this.visible.set(false);
  }

  onSubmit(): void {
    if (this.profileForm().invalid()) {
      this.profileForm().markAsTouched();
      return;
    }

    this.saving.set(true);
    const { username, email, displayName, phoneNumber, avatar } = this.formModel();

    this.accountService
      .updateCurrentUser({
        username: username.trim(),
        email: email.trim(),
        displayName: displayName.trim() || undefined,
        phoneNumber: phoneNumber.trim() || undefined,
        avatar: avatar.trim() || undefined,
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          //#if (IncludeLocalization)
          toast.success(this.transloco.translate('common.success'), {
            description: this.transloco.translate('account.profile.updateSuccess'),
          });
          //#else
          toast.success('Success', { description: 'Profile updated' });
          //#endif
          this.visible.set(false);
        },
        error: (error) => {
          //#if (IncludeLocalization)
          toast.error(this.transloco.translate('common.requestError'), {
            description: applicationErrorMessage(error),
          });
          //#else
          toast.error('Request failed', { description: applicationErrorMessage(error) });
          //#endif
        },
      });
  }
}
