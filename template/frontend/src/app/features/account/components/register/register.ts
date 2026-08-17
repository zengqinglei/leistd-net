import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import {
  form,
  required,
  minLength,
  maxLength,
  email as emailValidator,
  pattern,
  validate,
  FormField,
} from '@angular/forms/signals';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideUserPlus,
  lucideUsers,
  lucideShield,
  lucideZap,
  lucideEye,
  lucideEyeOff,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import {
  HlmInputGroup,
  HlmInputGroupInput,
  HlmInputGroupButton,
} from '@spartan-ng/helm/input-group';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { lastValueFrom } from 'rxjs';

import { applicationErrorMessage } from '../../../../core/errors/application-http-error';
//#if (IncludeLocalization)
import { LanguageSwitcher } from '../../../../shared/components/language-switcher/language-switcher';
//#endif
import { Logo } from '../../../../shared/components/logo/logo';
import { ThemeModeToggle } from '../../../../shared/components/theme-mode-toggle/theme-mode-toggle';
import { CaptchaOutputDto, SecurityConfigOutputDto } from '../../models/account.dto';
import { AccountService } from '../../services/account-service';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [
    FormField,
    RouterModule,
    NgIcon,
    HlmButton,
    HlmInput,
    HlmSpinner,
    HlmInputGroup,
    HlmInputGroupInput,
    HlmInputGroupButton,
    ThemeModeToggle,
    ...HlmFieldImports,
    ...HlmTooltipImports,
    //#if (IncludeLocalization)
    LanguageSwitcher,
    TranslocoModule,
    //#endif
    Logo,
  ],
  providers: [
    provideIcons({
      lucideUserPlus,
      lucideUsers,
      lucideShield,
      lucideZap,
      lucideEye,
      lucideEyeOff,
    }),
  ],
  templateUrl: './register.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Register implements OnInit {
  private accountService = inject(AccountService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);
  //#if (IncludeLocalization)
  public readonly transloco = inject(TranslocoService);
  // 属性位置的文案（无法在标签属性里用 #if 分支）经此对象绑定
  readonly i18n = {
    captcha: () => this.transloco.translate('account.register.captcha'),
    captchaPlaceholder: () => this.transloco.translate('account.register.captchaPlaceholder'),
    captchaRefresh: () => this.transloco.translate('account.register.captchaRefresh'),
    captchaAlt: () => this.transloco.translate('account.register.captchaAlt'),
  };
  //#else
  readonly i18n = {
    captcha: () => 'Captcha',
    captchaPlaceholder: () => 'Enter the captcha on the right',
    captchaRefresh: () => "Can't see clearly? Click to refresh",
    captchaAlt: () => 'Captcha',
  };
  //#endif

  // returnUrl：注册成功后跳转 login 时传递
  returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');

  private _isLoading = signal(false);
  public readonly isLoading = this._isLoading.asReadonly();

  // 密码可见性
  protected readonly showPassword = signal(false);
  protected readonly showConfirmPassword = signal(false);

  public securityConfig = signal<SecurityConfigOutputDto | null>(null);
  public captchaData = signal<CaptchaOutputDto | null>(null);

  public countdown = signal(0);
  private countdownIntervalId: ReturnType<typeof setInterval> | null = null;
  private readonly emailVerificationChallenge = signal<{
    challengeId: string;
    email: string;
    expiresAt: number;
  } | null>(null);
  private _isSendingEmailCode = signal(false);
  public readonly isSendingEmailCode = this._isSendingEmailCode.asReadonly();

  // 注册表单模型（Signal Forms）
  private readonly model = signal({
    email: '',
    username: '',
    captchaCode: '',
    emailVerificationCode: '',
    password: '',
    confirmPassword: '',
  });

  //#if (IncludeLocalization)
  readonly registerForm = form(this.model, (path) => {
    required(path.email, { message: this.transloco.translate('common.validation.required') });
    emailValidator(path.email, {
      message: this.transloco.translate('common.validation.email'),
    });
    maxLength(path.email, 256, { message: '' });
    required(path.username, { message: this.transloco.translate('common.validation.required') });
    minLength(path.username, 3, {
      message: this.transloco.translate('common.validation.minLength', { min: 3 }),
    });
    maxLength(path.username, 64, { message: '' });
    pattern(path.username, /^[a-zA-Z0-9_]+$/, {
      message: this.transloco.translate('common.validation.usernamePattern'),
    });
    required(path.captchaCode, {
      message: this.transloco.translate('common.validation.required'),
    });
    maxLength(path.captchaCode, 10, { message: '' });
    required(path.emailVerificationCode, {
      message: this.transloco.translate('common.validation.required'),
      when: () => this.securityConfig()?.enableEmailVerification === true,
    });
    required(path.password, { message: this.transloco.translate('common.validation.required') });
    pattern(path.password, /^(?=.*[a-zA-Z])(?=.*\d).{6,100}$/, {
      message: this.transloco.translate('common.validation.passwordWeak'),
    });
    required(path.confirmPassword, {
      message: this.transloco.translate('common.validation.required'),
    });
    validate(path.confirmPassword, (ctx) => {
      const confirm = ctx.value();
      const password = ctx.valueOf(path.password);
      if (password && confirm && password !== confirm) {
        return {
          kind: 'passwordMismatch',
          message: this.transloco.translate('common.validation.passwordMismatch'),
        };
      }
      return null;
    });
  });
  //#else
  readonly registerForm = form(this.model, (path) => {
    required(path.email, { message: 'This field is required.' });
    emailValidator(path.email, { message: 'Please enter a valid email address.' });
    maxLength(path.email, 256, { message: '' });
    required(path.username, { message: 'This field is required.' });
    minLength(path.username, 3, { message: 'Must be at least 3 characters.' });
    maxLength(path.username, 64, { message: '' });
    pattern(path.username, /^[a-zA-Z0-9_]+$/, {
      message: 'Must be 3–64 letters, digits, or underscores.',
    });
    required(path.captchaCode, { message: 'This field is required.' });
    maxLength(path.captchaCode, 10, { message: '' });
    required(path.emailVerificationCode, {
      message: 'This field is required.',
      when: () => this.securityConfig()?.enableEmailVerification === true,
    });
    required(path.password, { message: 'This field is required.' });
    pattern(path.password, /^(?=.*[a-zA-Z])(?=.*\d).{6,100}$/, {
      message: 'Password must be at least 6 characters and include letters and digits.',
    });
    required(path.confirmPassword, { message: 'This field is required.' });
    validate(path.confirmPassword, (ctx) => {
      const confirm = ctx.value();
      const password = ctx.valueOf(path.password);
      if (password && confirm && password !== confirm) {
        return { kind: 'passwordMismatch', message: 'The two passwords do not match.' };
      }
      return null;
    });
  });
  //#endif

  // 用户名是否被用户手动编辑过（一旦手动改动，停止从邮箱自动推导）
  private usernameManuallyEdited = false;
  // 最近一次自动推导写入的用户名，用于区分用户手动输入
  private lastDerivedUsername = '';

  constructor() {
    this.destroyRef.onDestroy(() => this.clearCountdown());

    // 监听 email/username 变化：从邮箱前缀自动推导 username，
    // 一旦用户手动改动 username 即停止推导（等价原 valueChanges 逻辑）。
    effect(() => {
      const email = this.model().email;
      const challenge = this.emailVerificationChallenge();
      untracked(() => {
        if (challenge && challenge.email !== this.normalizeEmail(email)) {
          this.emailVerificationChallenge.set(null);
          this.model.update((m) => ({ ...m, emailVerificationCode: '' }));
          this.clearCountdown();
        }

        const currentUsername = this.model().username;

        // 用户手动改动了 username（当前值既非空也不等于我们上次自动写入的值）
        if (currentUsername && currentUsername !== this.lastDerivedUsername) {
          this.usernameManuallyEdited = true;
        }

        if (this.usernameManuallyEdited || !email) {
          return;
        }

        const usernamePart = email.split('@')[0];
        if (usernamePart && usernamePart !== currentUsername) {
          this.lastDerivedUsername = usernamePart;
          this.model.update((m) => ({ ...m, username: usernamePart }));
        }
      });
    });
  }

  ngOnInit() {
    this.loadSecurityConfig();
    this.refreshCaptcha();
  }

  async loadSecurityConfig() {
    try {
      const config = await lastValueFrom(this.accountService.getSecurityConfig());
      this.securityConfig.set(config);
    } catch (err) {
      this.showRequestError(err);
    }
  }

  async refreshCaptcha() {
    try {
      const captcha = await lastValueFrom(this.accountService.getCaptcha());
      this.captchaData.set(captcha);
      this.model.update((m) => ({ ...m, captchaCode: '' }));
    } catch (err) {
      this.showRequestError(err);
    }
  }

  async sendEmailCode() {
    const { email, captchaCode } = this.model();
    const captchaToken = this.captchaData()?.captchaToken;

    if (!email || this.registerForm.email().invalid()) {
      //#if (IncludeLocalization)
      toast.warning(this.transloco.translate('common.notice'), {
        description: this.transloco.translate('account.register.emailRequired'),
      });
      //#else
      toast.warning('Notice', { description: 'Please enter a valid email first' });
      //#endif
      return;
    }

    if (!captchaCode || !captchaToken) {
      //#if (IncludeLocalization)
      toast.warning(this.transloco.translate('common.notice'), {
        description: this.transloco.translate('account.register.captchaRequired'),
      });
      //#else
      toast.warning('Notice', { description: 'Please enter the captcha first' });
      //#endif
      return;
    }

    this._isSendingEmailCode.set(true);
    try {
      const challenge = await lastValueFrom(
        this.accountService.sendEmailCode({
          email,
          captchaCode,
          captchaToken,
        }),
      );
      this.emailVerificationChallenge.set({
        challengeId: challenge.challengeId,
        email: this.normalizeEmail(email),
        expiresAt: Date.now() + challenge.expiresInSeconds * 1000,
      });
      //#if (IncludeLocalization)
      toast.success(this.transloco.translate('common.success'), {
        description: this.transloco.translate('account.register.emailCodeSent'),
      });
      //#else
      toast.success('Success', { description: 'Verification code sent, please check your email' });
      //#endif
      this.startCountdown(challenge.retryAfterSeconds);
    } catch (error) {
      this.showRequestError(error);
      this.refreshCaptcha(); // 如果验证码错误，刷新图形验证码
    } finally {
      this._isSendingEmailCode.set(false);
    }
  }

  private startCountdown(seconds: number) {
    this.clearCountdown();
    this.countdown.set(seconds);
    this.countdownIntervalId = setInterval(() => {
      const current = this.countdown();
      if (current <= 1) {
        this.clearCountdown();
      } else {
        this.countdown.set(current - 1);
      }
    }, 1000);
  }

  private clearCountdown() {
    if (this.countdownIntervalId) {
      clearInterval(this.countdownIntervalId);
      this.countdownIntervalId = null;
    }
    this.countdown.set(0);
  }

  async onSubmit() {
    if (this.registerForm().invalid()) {
      this.registerForm().markAsTouched();
      return;
    }

    const formValue = this.model();
    const emailVerificationEnabled = this.securityConfig()?.enableEmailVerification === true;
    const challenge = this.emailVerificationChallenge();
    if (
      emailVerificationEnabled &&
      (!challenge ||
        challenge.email !== this.normalizeEmail(formValue.email) ||
        challenge.expiresAt <= Date.now())
    ) {
      this.registerForm.emailVerificationCode().markAsTouched();
      return;
    }

    this._isLoading.set(true);
    try {
      const captchaToken = this.captchaData()?.captchaToken;

      if (!captchaToken) {
        //#if (IncludeLocalization)
        toast.error(this.transloco.translate('common.error'), {
          description: this.transloco.translate('account.register.captchaTokenMissing'),
        });
        //#else
        toast.error('Error', { description: 'Please refresh to get the captcha' });
        //#endif
        return;
      }

      await lastValueFrom(
        this.accountService.register({
          email: formValue.email,
          username: formValue.username,
          password: formValue.password,
          captchaCode: formValue.captchaCode,
          captchaToken: captchaToken,
          emailVerification: emailVerificationEnabled
            ? {
                challengeId: challenge!.challengeId,
                code: formValue.emailVerificationCode,
              }
            : undefined,
        }),
      );

      //#if (IncludeLocalization)
      toast.success(this.transloco.translate('account.register.registerSuccess'), {
        description: this.transloco.translate('account.register.registerSuccessDetail'),
      });
      //#else
      toast.success('Account created', {
        description: 'Account created successfully, please sign in',
      });
      //#endif
      this.router.navigate(['/auth/login'], {
        queryParams: this.returnUrl ? { returnUrl: this.returnUrl } : undefined,
      });
    } catch (error) {
      this.showRequestError(error);
      this.refreshCaptcha();
    } finally {
      this._isLoading.set(false);
    }
  }

  private showRequestError(error: unknown): void {
    //#if (IncludeLocalization)
    toast.error(this.transloco.translate('common.requestError'), {
      description: applicationErrorMessage(error),
    });
    //#else
    toast.error('Request failed', { description: applicationErrorMessage(error) });
    //#endif
  }

  private normalizeEmail(email: string): string {
    return email.trim().toLowerCase();
  }
}
