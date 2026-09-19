import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowLeft, lucideShieldCheck } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { lastValueFrom } from 'rxjs';

import {
  ApplicationHttpError,
  applicationErrorMessage,
} from '../../../../core/errors/application-http-error';
//#if (IncludeLocalization)
import { translationReady } from '../../../../core/i18n/translation-ready';
//#endif
import { AccountService } from '../../services/account-service';
import { OtpCodeInput } from '../otp-code-input/otp-code-input';

/**
 * 登录第二步：输入身份验证器应用上的验证码，或手机不在身边时输入恢复码。
 *
 * 通过后服务端下发会话，由登录页接着建立会话上下文与跳转；
 * 凭据过期或错误次数用完时回到密码那一步。
 */
@Component({
  selector: 'app-two-factor-challenge',
  imports: [NgIcon, HlmButton, HlmInput, HlmSpinner, OtpCodeInput],
  providers: [provideIcons({ lucideArrowLeft, lucideShieldCheck })],
  templateUrl: './two-factor-challenge.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TwoFactorChallenge {
  private readonly accountService = inject(AccountService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  private readonly translationReady = translationReady(this.transloco);

  protected readonly t = (key: string) => {
    this.translationReady();
    return this.transloco.translate(key);
  };
  //#else
  protected readonly t = (key: string) => ENGLISH[key] ?? key;
  //#endif

  /** 第一步返回的凭据。 */
  readonly token = input.required<string>();
  /** 第二步通过，会话已下发。 */
  readonly completed = output<void>();
  /** 回到密码那一步（用户选择返回，或凭据已失效）。 */
  readonly cancelled = output<void>();

  protected readonly useRecoveryCode = signal(false);
  protected readonly value = signal('');
  protected readonly submitting = signal(false);

  protected readonly canSubmit = computed(() => {
    const value = this.value().replace(/\s/g, '');
    return (
      !this.submitting() && (this.useRecoveryCode() ? value.length >= 8 : /^\d{6}$/.test(value))
    );
  });

  protected toggleMode(): void {
    this.useRecoveryCode.update((v) => !v);
    this.value.set('');
  }

  protected onInput(event: Event): void {
    this.value.set((event.target as HTMLInputElement).value);
  }

  protected async submit(): Promise<void> {
    if (!this.canSubmit()) {
      return;
    }

    this.submitting.set(true);
    const value = this.value().trim();
    try {
      await lastValueFrom(
        this.accountService.completeTwoFactorLogin(
          this.useRecoveryCode()
            ? { token: this.token(), recoveryCode: value }
            : { token: this.token(), code: value.replace(/\s/g, '') },
        ),
      );
      this.completed.emit();
    } catch (error) {
      toast.error(this.t('account.login.loginFailed'), {
        description: applicationErrorMessage(error),
      });
      // 凭据过期、次数用完或账号被锁：这一步已经无法继续，回到密码那一步
      const code = error instanceof ApplicationHttpError ? error.code : undefined;
      if (code !== 'Auth:TwoFactorCodeInvalid') {
        this.cancelled.emit();
      } else {
        this.value.set('');
      }
    } finally {
      this.submitting.set(false);
    }
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 的 `account.twoFactorLogin` 同步。 */
const ENGLISH: Record<string, string> = {
  'account.login.loginFailed': 'Login failed',
  'account.twoFactorLogin.title': 'Two-factor authentication',
  'account.twoFactorLogin.codeHint': 'Enter the 6-digit code from your authenticator app.',
  'account.twoFactorLogin.recoveryHint': 'Enter one of the recovery codes you saved.',
  'account.twoFactorLogin.codeLabel': 'Verification code',
  'account.twoFactorLogin.recoveryLabel': 'Recovery code',
  'account.twoFactorLogin.submit': 'Verify',
  'account.twoFactorLogin.useRecovery': "Can't use your phone? Use a recovery code",
  'account.twoFactorLogin.useCode': 'Use a verification code instead',
  'account.twoFactorLogin.back': 'Back to sign in',
};
//#endif
