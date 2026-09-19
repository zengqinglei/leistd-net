import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
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
import { lucideCircleAlert, lucideCircleCheck, lucideImagePlus } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { finalize } from 'rxjs/operators';

import { applicationErrorMessage } from '../../../../core/errors/application-http-error';
import { AuthService } from '../../../../core/services/auth-service';
import {
  AvatarImageRejected,
  isAvatarImageUrl,
  prepareAvatarImage,
} from '../../../../shared/utils/avatar-image';
import { AccountService } from '../../services/account-service';

const PHONE_PATTERN = /^[0-9+\-()\s]{0,20}$/;

/** 当前这次邮箱验证：发出的验证码对应的挑战，以及还要等多久才能重发。 */
interface EmailChallenge {
  challengeId: string;
  email: string;
}

/**
 * 个人设置 ·「个人资料」面板：头像、用户名、邮箱（含验证）、显示名、手机。
 *
 * - 表单跟着当前用户走：进面板、保存成功后（服务端返回的新值写回当前用户）都按当前用户重填；
 *   「撤销修改」同样回到当前用户的值。
 * - 头像选完即生效，不跟表单一起保存：它有自己的接口，也不该被"撤销修改"撤回。
 * - 邮箱验证针对的是**已保存**的邮箱；表单里改了还没保存时，先提示保存后再验证。
 */
@Component({
  selector: 'app-profile-panel',
  standalone: true,
  // prettier-ignore
  imports: [
    FormField,
    NgIcon,
    HlmButton,
    HlmSpinner,
    HlmInput,
    HlmBadge,
    ...HlmFieldImports,
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
  ],
  providers: [provideIcons({ lucideCircleAlert, lucideCircleCheck, lucideImagePlus })],
  templateUrl: './profile-panel.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProfilePanel {
  private readonly authService = inject(AuthService);
  private readonly accountService = inject(AccountService);
  private readonly destroyRef = inject(DestroyRef);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  private readonly t = (key: string, params?: Record<string, unknown>) =>
    this.transloco.translate(key, params);
  //#else
  private readonly t = (key: string, params?: Record<string, unknown>) =>
    ENGLISH[key]?.replace(/\{\{(\w+)\}\}/g, (_, name: string) => String(params?.[name] ?? '')) ??
    key;
  //#endif

  readonly saving = signal(false);
  readonly user = computed(() => this.authService.currentUser());

  /** 角色徽章直接展示后端返回的角色名，不再依赖前端硬编码的角色枚举与标签映射。 */
  readonly roleLabels = computed(() => this.authService.currentUser()?.roles ?? []);

  /** 正在上传的头像（处理好的 data URL）；上传期间先显示它，完成后换成服务端给的地址。 */
  private readonly pendingAvatar = signal<string | null>(null);
  readonly avatarBusy = signal(false);
  readonly avatarPreview = computed(() => this.pendingAvatar() ?? this.user()?.avatar ?? '');
  readonly hasAvatarImage = computed(() => isAvatarImageUrl(this.avatarPreview()));

  // 表单模型（Signal Forms）
  protected readonly formModel = signal({
    username: '',
    email: '',
    displayName: '',
    phoneNumber: '',
  });

  readonly displayName = computed(
    () =>
      this.formModel().displayName.trim() ||
      this.user()?.displayName ||
      this.user()?.username ||
      this.t('account.profile.guestUser'),
  );

  /** 表单里的邮箱与已保存的不同：验证只能对已保存的邮箱做。 */
  readonly emailEdited = computed(
    () => this.formModel().email.trim().toLowerCase() !== (this.user()?.email ?? '').toLowerCase(),
  );

  /**
   * 部署能不能发邮箱验证码（验证码摘要密钥是否已配置）。取回之前按"能"处理：
   * 多数部署都配了，先藏起按钮再冒出来反而闪一下；真不能时发送端也会给出明确原因。
   */
  readonly emailVerificationAvailable = signal(true);

  readonly emailChallenge = signal<EmailChallenge | null>(null);
  readonly emailCode = signal('');
  readonly sendingCode = signal(false);
  readonly confirmingCode = signal(false);
  /** 距离可以重发还剩几秒；0 表示可以发。 */
  readonly resendSeconds = signal(0);
  private resendTimer: ReturnType<typeof setInterval> | undefined;

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

  constructor() {
    // 当前用户一变（首次取回、保存成功后写回）就按它重填；表单模型不参与追踪，
    // 否则每敲一个字都会触发重填，把输入抹掉。
    effect(() => {
      this.user();
      untracked(() => this.reset());
    });
    this.destroyRef.onDestroy(() => clearInterval(this.resendTimer));
    this.accountService
      .getSecurityConfig()
      .subscribe((config) =>
        this.emailVerificationAvailable.set(config.emailVerificationAvailable),
      );
  }

  /** 按当前用户回填表单，丢掉未保存的修改。 */
  reset(): void {
    const user = this.user();
    this.formModel.set({
      username: user?.username ?? '',
      email: user?.email ?? '',
      displayName: user?.displayName ?? '',
      phoneNumber: user?.phoneNumber ?? '',
    });
    // 已保存的邮箱变了（改邮箱后保存），手里那个挑战是发给旧地址的，作废
    if (this.emailChallenge()?.email !== user?.email) {
      this.emailChallenge.set(null);
      this.emailCode.set('');
    }
  }

  /** 选完图片即处理并上传：裁成正方形、缩到 256，失败原因就地说清。 */
  async onAvatarSelect(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    // 允许再次选择同一文件时仍触发 change 事件
    input.value = '';
    if (!file) {
      return;
    }

    let dataUrl: string;
    try {
      dataUrl = await prepareAvatarImage(file);
    } catch (error: unknown) {
      const reason = error instanceof AvatarImageRejected ? error.reason : 'decode';
      toast.error(this.t('account.profile.avatarRejectedSummary'), {
        description: this.t(`account.profile.avatarRejected.${reason}`),
      });
      return;
    }

    this.pendingAvatar.set(dataUrl);
    this.applyAvatar(dataUrl, 'account.profile.avatarUpdated');
  }

  removeAvatar(): void {
    this.applyAvatar(null, 'account.profile.avatarRemoved');
  }

  private applyAvatar(avatar: string | null, successKey: string): void {
    this.avatarBusy.set(true);
    this.accountService
      .setAvatar({ avatar })
      .pipe(
        finalize(() => {
          this.avatarBusy.set(false);
          this.pendingAvatar.set(null);
        }),
      )
      .subscribe({
        next: () => toast.success(this.t(successKey)),
        error: (error) =>
          toast.error(this.t('common.requestError'), {
            description: applicationErrorMessage(error),
          }),
      });
  }

  /** 给已保存的邮箱发验证码，并开始重发倒计时。 */
  sendEmailCode(): void {
    const email = this.user()?.email;
    if (!email) {
      return;
    }

    this.sendingCode.set(true);
    this.accountService
      .sendCurrentEmailCode()
      .pipe(finalize(() => this.sendingCode.set(false)))
      .subscribe({
        next: (challenge) => {
          this.emailChallenge.set({ challengeId: challenge.challengeId, email });
          this.emailCode.set('');
          this.startResendCountdown(challenge.retryAfterSeconds);
          toast.success(this.t('account.profile.codeSent', { email }));
        },
        error: (error) =>
          toast.error(this.t('common.requestError'), {
            description: applicationErrorMessage(error),
          }),
      });
  }

  confirmEmailCode(): void {
    const challenge = this.emailChallenge();
    const code = this.emailCode().trim();
    if (!challenge || code.length === 0) {
      return;
    }

    this.confirmingCode.set(true);
    this.accountService
      .confirmCurrentEmail({ challengeId: challenge.challengeId, code })
      .pipe(finalize(() => this.confirmingCode.set(false)))
      .subscribe({
        next: () => {
          this.emailChallenge.set(null);
          this.emailCode.set('');
          toast.success(this.t('account.profile.emailVerifiedSuccess'));
        },
        error: (error) =>
          toast.error(this.t('common.requestError'), {
            description: applicationErrorMessage(error),
          }),
      });
  }

  onEmailCodeInput(event: Event): void {
    this.emailCode.set((event.target as HTMLInputElement).value);
  }

  private startResendCountdown(seconds: number): void {
    clearInterval(this.resendTimer);
    this.resendSeconds.set(Math.max(0, Math.ceil(seconds)));
    this.resendTimer = setInterval(() => {
      const next = this.resendSeconds() - 1;
      this.resendSeconds.set(Math.max(0, next));
      if (next <= 0) {
        clearInterval(this.resendTimer);
      }
    }, 1000);
  }

  onSubmit(): void {
    if (this.profileForm().invalid()) {
      this.profileForm().markAsTouched();
      return;
    }

    this.saving.set(true);
    const { username, email, displayName, phoneNumber } = this.formModel();

    this.accountService
      .updateCurrentUser({
        username: username.trim(),
        email: email.trim(),
        displayName: displayName.trim() || undefined,
        phoneNumber: phoneNumber.trim() || undefined,
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () =>
          toast.success(this.t('common.success'), {
            description: this.t('account.profile.updateSuccess'),
          }),
        error: (error) =>
          toast.error(this.t('common.requestError'), {
            description: applicationErrorMessage(error),
          }),
      });
  }
}
//#if (!IncludeLocalization)

/** 不带本地化的模板里用的英文文案，键与语言包一致，便于两种形态共用同一套代码路径。 */
const ENGLISH: Record<string, string> = {
  'common.success': 'Success',
  'common.requestError': 'Request failed',
  'account.profile.guestUser': 'Guest user',
  'account.profile.updateSuccess': 'Profile updated',
  'account.profile.avatarUpdated': 'Avatar updated',
  'account.profile.avatarRemoved': 'Avatar removed',
  'account.profile.avatarRejectedSummary': 'Cannot use this image',
  'account.profile.avatarRejected.type': 'Please choose a PNG, JPG or WebP image.',
  'account.profile.avatarRejected.size': 'The image cannot exceed 10 MB.',
  'account.profile.avatarRejected.decode': 'This image could not be read. Try another one.',
  'account.profile.codeSent': 'A verification code was sent to {{email}}',
  'account.profile.emailVerifiedSuccess': 'Email address verified',
};
//#endif
