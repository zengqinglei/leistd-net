import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
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
import { lastValueFrom } from 'rxjs';

import { environment } from '../../../../../environments/environment';
import { AuthService } from '../../../../core/services/auth-service';
import { ThemeService } from '../../../../core/services/theme-service';
//#if (IncludeLocalization)
import { LanguageSwitcher } from '../../../../shared/components/language-switcher/language-switcher';
//#endif
import { LogoComponent } from '../../../../shared/components/logo/logo';
import { ThemeConfigurator } from '../../../../shared/components/theme-configurator/theme-configurator';
import { AccountService } from '../../services/account-service';

@Component({
  selector: 'app-login',
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
    LogoComponent
  ],
  templateUrl: './login.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Login {
  private fb = inject(FormBuilder);
  private accountService = inject(AccountService);
  private authService = inject(AuthService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private messageService = inject(MessageService);
  public themeService = inject(ThemeService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif

  // 加载状态
  private _isLoading = signal(false);
  public readonly isLoading = this._isLoading.asReadonly();

  // Mock状态
  public readonly isMockEnabled = signal(typeof environment.useMock === 'object' && environment.useMock.enable === true);

  // 登录表单
  loginForm = this.fb.group({
    usernameOrEmail: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(256)]],
    password: ['', [Validators.required, Validators.minLength(6), Validators.maxLength(100)]]
  });

  constructor() {
    // 进入登录页面时清理旧的认证信息
    this.authService.clearAuthData();
  }

  /**
   * 提交登录表单
   */
  async onSubmit() {
    if (this.loginForm.invalid) {
      this.loginForm.markAllAsTouched();
      return;
    }

    this._isLoading.set(true);

    try {
      const { usernameOrEmail, password } = this.loginForm.value;

      const loginInput = {
        usernameOrEmail: usernameOrEmail!,
        password: password!
      };
      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');

      await lastValueFrom(this.authService.login(loginInput));
      await lastValueFrom(this.authService.loadUser());

      // 登录成功提示
      //#if (IncludeLocalization)
      this.messageService.add({
        severity: 'success',
        summary: this.transloco.translate('account.login.loginSuccess'),
        detail: this.transloco.translate('account.login.welcomeBack'),
        life: 3000
      });
      //#else
      this.messageService.add({
        severity: 'success',
        summary: 'Login successful',
        detail: 'Welcome back!',
        life: 3000
      });
      //#endif

      if (this.isSafeLocalReturnUrl(returnUrl)) {
        await this.router.navigateByUrl(returnUrl);
        return;
      }

      // 根据角色跳转
      if (this.authService.currentUser()?.isAdmin()) {
        this.router.navigate(['/platform']);
      } else {
        this.router.navigate(['/workspace']);
      }
    } catch (error) {
      // HTTP 错误已经在 httpErrorInterceptor 中统一处理并显示 Toast
      // 这里只需要捕获错误以确保 finally 块能执行，不需要再次抛出
      console.error('Login failed', error);
    } finally {
      this._isLoading.set(false);
    }
  }

  private isSafeLocalReturnUrl(returnUrl: string | null): returnUrl is string {
    return !!returnUrl && returnUrl.startsWith('/') && !returnUrl.startsWith('//') && !returnUrl.includes('://');
  }

  /**
   * 获取表单字段的错误信息
   */
  getFieldError(fieldName: string): string | null {
    const field = this.loginForm.get(fieldName);
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
    if (field.errors['minlength']) {
      const minLength = field.errors['minlength'].requiredLength;
      //#if (IncludeLocalization)
      return this.transloco.translate('account.register.errMinLength', { min: minLength });
      //#else
      return `At least ${minLength} characters required`;
      //#endif
    }
    return null;
  }
  //#if (IncludeExternalLogin)

  loginWithGitHub() {
    this.loginWithExternalProvider('github', 'GitHub');
  }

  loginWithGoogle() {
    this.loginWithExternalProvider('google', 'Google');
  }

  /**
   * 通用第三方登录
   */
  private async loginWithExternalProvider(provider: 'github' | 'google', label: string) {
    this._isLoading.set(true);
    try {
      const response = await lastValueFrom(this.accountService.getExternalLoginUrl(provider));

      if (!response.loginUrl) {
        throw new Error('No valid login URL was returned');
      }

      window.location.href = response.loginUrl;
    } catch (error) {
      console.error(`${label} login failed`, error);
      //#if (IncludeLocalization)
      this.messageService.add({
        severity: 'error',
        summary: this.transloco.translate('account.login.loginFailed'),
        detail: this.transloco.translate('account.login.externalLoginFailed', { provider: label }),
        life: 3000
      });
      //#else
      this.messageService.add({
        severity: 'error',
        summary: 'Login failed',
        detail: `Unable to connect to the ${label} login service, please try again later`,
        life: 3000
      });
      //#endif
      this._isLoading.set(false);
    }
  }
  //#endif
}
