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
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { finalize } from 'rxjs/operators';

import { notify } from '../../../../core/notifications/notify';
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
  readonly avatarPreview = signal('');

  // 表单模型（Signal Forms）
  protected readonly formModel = signal({
    username: '',
    email: '',
    nickname: '',
    phoneNumber: '',
    avatar: '',
  });

  readonly displayName = computed(
    () =>
      this.formModel().nickname.trim() ||
      this.user()?.nickname ||
      this.user()?.username ||
      this.guestLabel(),
  );
  readonly avatarLabel = computed(() => {
    const text = this.displayName().trim();
    return (text.charAt(0) || 'U').toUpperCase();
  });
  readonly avatarStyle = computed(() => {
    const seed = (this.formModel().username || this.user()?.username || this.displayName()).trim();
    let total = 0;

    for (const char of seed) {
      total += char.charCodeAt(0);
    }

    const palette = [
      { background: '#dbeafe', color: '#1d4ed8' },
      { background: '#dcfce7', color: '#15803d' },
      { background: '#fef3c7', color: '#b45309' },
      { background: '#fce7f3', color: '#be185d' },
      { background: '#ede9fe', color: '#6d28d9' },
    ];

    return palette[total % palette.length];
  });

  //#if (IncludeLocalization)
  readonly profileForm = form(this.formModel, (path) => {
    required(path.username, {
      message: this.transloco.translate('account.profile.usernameRequired'),
    });
    pattern(path.username, /^[a-zA-Z0-9_]{3,64}$/, {
      message: this.transloco.translate('account.profile.usernameFormatError'),
    });
    required(path.email, { message: this.transloco.translate('account.profile.emailError') });
    emailValidator(path.email, {
      message: this.transloco.translate('account.profile.emailError'),
    });
    maxLength(path.email, 256, {
      message: this.transloco.translate('account.profile.emailError'),
    });
    maxLength(path.nickname, 128, {
      message: this.transloco.translate('account.profile.nicknameError'),
    });
    maxLength(path.phoneNumber, 20, {
      message: this.transloco.translate('account.profile.phoneNumberLengthError'),
    });
    pattern(path.phoneNumber, PHONE_PATTERN, {
      message: this.transloco.translate('account.profile.phoneNumberPatternError'),
    });
  });
  //#else
  readonly profileForm = form(this.formModel, (path) => {
    required(path.username, { message: 'Username cannot be empty' });
    pattern(path.username, /^[a-zA-Z0-9_]{3,64}$/, {
      message: 'Username must be 3–64 characters of letters, numbers, or underscores',
    });
    required(path.email, {
      message: 'Please enter a valid email; length cannot exceed 256 characters',
    });
    emailValidator(path.email, {
      message: 'Please enter a valid email; length cannot exceed 256 characters',
    });
    maxLength(path.email, 256, {
      message: 'Please enter a valid email; length cannot exceed 256 characters',
    });
    maxLength(path.nickname, 128, { message: 'Nickname length cannot exceed 128 characters' });
    maxLength(path.phoneNumber, 20, { message: 'Phone number length cannot exceed 20 characters' });
    pattern(path.phoneNumber, PHONE_PATTERN, {
      message: 'Phone number supports only digits, spaces, and the symbols + - ( )',
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
        nickname: user?.nickname ?? '',
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
      notify.error(messages.typeSummary, { detail: messages.typeDetail });
      input.value = '';
      return;
    }

    if (file.size > MAX_AVATAR_SIZE) {
      notify.error(messages.sizeSummary, { detail: messages.sizeDetail });
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
    const { username, email, nickname, phoneNumber, avatar } = this.formModel();

    this.accountService
      .updateCurrentUser({
        username: username.trim(),
        email: email.trim(),
        nickname: nickname.trim() || undefined,
        phoneNumber: phoneNumber.trim() || undefined,
        avatar: avatar.trim() || undefined,
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          //#if (IncludeLocalization)
          notify.success(this.transloco.translate('common.success'), {
            detail: this.transloco.translate('account.profile.updateSuccess'),
          });
          //#else
          notify.success('Success', { detail: 'Profile updated' });
          //#endif
          this.visible.set(false);
        },
      });
  }
}
