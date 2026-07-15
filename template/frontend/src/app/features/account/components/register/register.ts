import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { StyleClassModule } from 'primeng/styleclass';
import { TooltipModule } from 'primeng/tooltip';
import { lastValueFrom } from 'rxjs';

import { ThemeService } from '../../../../core/services/theme-service';
//#if (IncludeLocalization)
import { LanguageSwitcher } from '../../../../shared/components/language-switcher/language-switcher';
//#endif
import { LogoComponent } from '../../../../shared/components/logo/logo';
import { ThemeConfigurator } from '../../../../shared/components/theme-configurator/theme-configurator';
import { CaptchaOutputDto, SecurityConfigOutputDto } from '../../models/account.dto';
import { AccountService } from '../../services/account-service';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    RouterModule,
    CardModule,
    InputTextModule,
    PasswordModule,
    ButtonModule,
    StyleClassModule,
    ThemeConfigurator,
    //#if (IncludeLocalization)
    LanguageSwitcher,
    TranslocoModule,
    //#endif
    LogoComponent,
    TooltipModule
  ],
  templateUrl: './register.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Register implements OnInit {
  private fb = inject(FormBuilder);
  private accountService = inject(AccountService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private messageService = inject(MessageService);
  private destroyRef = inject(DestroyRef);
  public themeService = inject(ThemeService);
  //#if (IncludeLocalization)
  public readonly transloco = inject(TranslocoService);
  // 属性位置的文案（无法在标签属性里用 #if 分支）经此对象绑定
  readonly i18n = {
    captcha: () => this.transloco.translate('account.register.captcha'),
    captchaPlaceholder: () => this.transloco.translate('account.register.captchaPlaceholder'),
    captchaRefresh: () => this.transloco.translate('account.register.captchaRefresh'),
    captchaAlt: () => this.transloco.translate('account.register.captchaAlt')
  };
  //#else
  readonly i18n = {
    captcha: () => 'Captcha',
    captchaPlaceholder: () => 'Enter the captcha on the right',
    captchaRefresh: () => "Can't see clearly? Click to refresh",
    captchaAlt: () => 'Captcha'
  };
  //#endif

  // returnUrl：注册成功后跳转 login 时传递
  returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');

  private _isLoading = signal(false);
  public readonly isLoading = this._isLoading.asReadonly();

  public securityConfig = signal<SecurityConfigOutputDto | null>(null);
  public captchaData = signal<CaptchaOutputDto | null>(null);

  public countdown = signal(0);
  private countdownIntervalId: ReturnType<typeof setInterval> | null = null;
  private _isSendingEmailCode = signal(false);
  public readonly isSendingEmailCode = this._isSendingEmailCode.asReadonly();

  registerForm = this.fb.group(
    {
      email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
      username: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(64), Validators.pattern(/^[a-zA-Z0-9_]+$/)]],
      captchaCode: ['', [Validators.required, Validators.maxLength(10)]],
      emailVerificationCode: [''],
      password: ['', [Validators.required, Validators.pattern(/^(?=.*[a-zA-Z])(?=.*\d).{6,100}$/)]],
      confirmPassword: ['', [Validators.required]]
    },
    { validators: this.passwordMatchValidator }
  );

  private usernameManuallyEdited = false;

  constructor() {
    this.destroyRef.onDestroy(() => this.clearCountdown());
  }

  ngOnInit() {
    this.loadSecurityConfig();
    this.refreshCaptcha();

    // 监听 email 变化，自动推导 username
    this.registerForm
      .get('email')
      ?.valueChanges.pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(email => {
        if (!this.usernameManuallyEdited && email) {
          const usernamePart = email.split('@')[0];
          if (usernamePart) {
            this.registerForm.get('username')?.setValue(usernamePart, { emitEvent: false });
          }
        }
      });

    // 监听 username 变化，标记是否手动修改
    this.registerForm
      .get('username')
      ?.valueChanges.pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(val => {
        if (val) {
          this.usernameManuallyEdited = true;
        }
      });
  }

  passwordMatchValidator(control: AbstractControl): ValidationErrors | null {
    const password = control.get('password')?.value;
    const confirmPassword = control.get('confirmPassword')?.value;

    if (password && confirmPassword && password !== confirmPassword) {
      control.get('confirmPassword')?.setErrors({ passwordMismatch: true });
      return { passwordMismatch: true };
    } else {
      const errors = control.get('confirmPassword')?.errors;
      if (errors) {
        delete errors['passwordMismatch'];
        if (Object.keys(errors).length === 0) {
          control.get('confirmPassword')?.setErrors(null);
        } else {
          control.get('confirmPassword')?.setErrors(errors);
        }
      }
      return null;
    }
  }

  async loadSecurityConfig() {
    try {
      const config = await lastValueFrom(this.accountService.getSecurityConfig());
      this.securityConfig.set(config);
      if (config.enableEmailVerification) {
        this.registerForm.get('emailVerificationCode')?.setValidators([Validators.required]);
        this.registerForm.get('emailVerificationCode')?.updateValueAndValidity();
      }
    } catch (err) {
      console.error('Failed to load security config', err);
    }
  }

  async refreshCaptcha() {
    try {
      const captcha = await lastValueFrom(this.accountService.getCaptcha());
      this.captchaData.set(captcha);
      this.registerForm.get('captchaCode')?.setValue('');
    } catch (err) {
      console.error('Failed to refresh captcha', err);
    }
  }

  async sendEmailCode() {
    const email = this.registerForm.get('email')?.value;
    const captchaCode = this.registerForm.get('captchaCode')?.value;
    const captchaToken = this.captchaData()?.captchaToken;

    if (!email || this.registerForm.get('email')?.invalid) {
      //#if (IncludeLocalization)
      this.messageService.add({
        severity: 'warn',
        summary: this.transloco.translate('common.notice'),
        detail: this.transloco.translate('account.register.emailRequired')
      });
      //#else
      this.messageService.add({ severity: 'warn', summary: 'Notice', detail: 'Please enter a valid email first' });
      //#endif
      return;
    }

    if (!captchaCode || !captchaToken) {
      //#if (IncludeLocalization)
      this.messageService.add({
        severity: 'warn',
        summary: this.transloco.translate('common.notice'),
        detail: this.transloco.translate('account.register.captchaRequired')
      });
      //#else
      this.messageService.add({ severity: 'warn', summary: 'Notice', detail: 'Please enter the captcha first' });
      //#endif
      return;
    }

    this._isSendingEmailCode.set(true);
    try {
      await lastValueFrom(
        this.accountService.sendEmailCode({
          email,
          captchaCode,
          captchaToken
        })
      );
      //#if (IncludeLocalization)
      this.messageService.add({
        severity: 'success',
        summary: this.transloco.translate('common.success'),
        detail: this.transloco.translate('account.register.emailCodeSent')
      });
      //#else
      this.messageService.add({ severity: 'success', summary: 'Success', detail: 'Verification code sent, please check your email' });
      //#endif
      this.startCountdown();
    } catch {
      this.refreshCaptcha(); // 如果验证码错误，刷新图形验证码
    } finally {
      this._isSendingEmailCode.set(false);
    }
  }

  private startCountdown() {
    this.clearCountdown();
    this.countdown.set(60);
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
    if (this.registerForm.invalid) {
      this.registerForm.markAllAsTouched();
      return;
    }

    this._isLoading.set(true);
    try {
      const formValue = this.registerForm.value;
      const captchaToken = this.captchaData()?.captchaToken;

      if (!captchaToken) {
        //#if (IncludeLocalization)
        this.messageService.add({
          severity: 'error',
          summary: this.transloco.translate('common.error'),
          detail: this.transloco.translate('account.register.captchaTokenMissing')
        });
        //#else
        this.messageService.add({ severity: 'error', summary: 'Error', detail: 'Please refresh to get the captcha' });
        //#endif
        return;
      }

      await lastValueFrom(
        this.accountService.register({
          email: formValue.email!,
          username: formValue.username!,
          password: formValue.password!,
          captchaCode: formValue.captchaCode!,
          captchaToken: captchaToken,
          emailVerificationCode: formValue.emailVerificationCode || undefined
        })
      );

      //#if (IncludeLocalization)
      this.messageService.add({
        severity: 'success',
        summary: this.transloco.translate('account.register.registerSuccess'),
        detail: this.transloco.translate('account.register.registerSuccessDetail')
      });
      //#else
      this.messageService.add({
        severity: 'success',
        summary: 'Registration successful',
        detail: 'Account created successfully, please sign in'
      });
      //#endif
      this.router.navigate(['/auth/login'], {
        queryParams: this.returnUrl ? { returnUrl: this.returnUrl } : undefined
      });
    } catch {
      this.refreshCaptcha();
    } finally {
      this._isLoading.set(false);
    }
  }

  getFieldError(fieldName: string): string | null {
    const field = this.registerForm.get(fieldName);
    if (!field || !field.touched || !field.errors) {
      return null;
    }

    if (field.errors['required']) {
      //#if (IncludeLocalization)
      return this.transloco.translate('account.register.errRequired');
      //#else
      return 'This field is required';
      //#endif
    }
    if (field.errors['email']) {
      //#if (IncludeLocalization)
      return this.transloco.translate('account.register.errEmailInvalid');
      //#else
      return 'Invalid email format';
      //#endif
    }
    if (field.errors['pattern'] && fieldName === 'username') {
      //#if (IncludeLocalization)
      return this.transloco.translate('account.register.errUsernamePattern');
      //#else
      return 'Only letters, digits and underscores are allowed';
      //#endif
    }
    if (field.errors['pattern'] && fieldName === 'password') {
      //#if (IncludeLocalization)
      return this.transloco.translate('account.register.errPasswordPattern');
      //#else
      return 'Password must be at least 6 characters and contain letters and digits';
      //#endif
    }
    if (field.errors['minlength']) {
      const minLength = field.errors['minlength'].requiredLength;
      //#if (IncludeLocalization)
      return this.transloco.translate('account.register.errMinLength', { min: minLength });
      //#else
      return `At least ${minLength} characters required`;
      //#endif
    }
    if (field.errors['passwordMismatch']) {
      //#if (IncludeLocalization)
      return this.transloco.translate('account.register.errPasswordMismatch');
      //#else
      return 'The two passwords do not match';
      //#endif
    }
    return null;
  }
}
