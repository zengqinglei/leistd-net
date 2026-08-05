import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { form, minLength, maxLength, required, FormField } from '@angular/forms/signals';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideZap,
  lucideCircleCheck,
  lucideInfo,
  lucideEye,
  lucideEyeOff,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import {
  HlmInputGroup,
  HlmInputGroupInput,
  HlmInputGroupButton,
} from '@spartan-ng/helm/input-group';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { lastValueFrom } from 'rxjs';

import { environment } from '../../../../../environments/environment';
import { applicationErrorMessage } from '../../../../core/errors/application-http-error';
import { AuthService } from '../../../../core/services/auth-service';
//#if (IncludeLocalization)
import { LanguageSwitcher } from '../../../../shared/components/language-switcher/language-switcher';
//#endif
import { Logo } from '../../../../shared/components/logo/logo';
import { ThemeModeToggle } from '../../../../shared/components/theme-mode-toggle/theme-mode-toggle';
import { AccountService } from '../../services/account-service';

// GitHub 品牌图标（lucide 已下架品牌 logo，用官方 SVG path 自定义注入）
const githubIcon =
  '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor"><path d="M12 .5C5.37.5 0 5.87 0 12.5c0 5.3 3.44 9.8 8.2 11.39.6.11.82-.26.82-.58v-2.03c-3.34.73-4.04-1.61-4.04-1.61-.55-1.39-1.34-1.76-1.34-1.76-1.09-.75.08-.73.08-.73 1.2.08 1.84 1.24 1.84 1.24 1.07 1.83 2.81 1.3 3.5.99.11-.78.42-1.3.76-1.6-2.67-.3-5.47-1.33-5.47-5.93 0-1.31.47-2.38 1.24-3.22-.13-.31-.54-1.52.11-3.18 0 0 1.01-.32 3.3 1.23a11.5 11.5 0 0 1 6 0c2.29-1.55 3.3-1.23 3.3-1.23.65 1.66.24 2.87.12 3.18.77.84 1.23 1.91 1.23 3.22 0 4.61-2.81 5.63-5.49 5.93.43.37.82 1.1.82 2.22v3.29c0 .32.22.7.83.58A12 12 0 0 0 24 12.5C24 5.87 18.63.5 12 .5z"/></svg>';

@Component({
  selector: 'app-login',
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
    ...HlmCardImports,
    ...HlmFieldImports,
    //#if (IncludeLocalization)
    LanguageSwitcher,
    TranslocoModule,
    //#endif
    Logo,
  ],
  providers: [
    provideIcons({
      lucideZap,
      lucideCircleCheck,
      lucideInfo,
      lucideEye,
      lucideEyeOff,
      github: githubIcon,
    }),
  ],
  templateUrl: './login.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Login {
  private accountService = inject(AccountService);
  private authService = inject(AuthService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif

  // 加载状态
  private _isLoading = signal(false);
  public readonly isLoading = this._isLoading.asReadonly();

  // 密码可见性
  protected readonly showPassword = signal(false);

  // Mock状态：useMock 支持布尔与对象两种形态（与 MockInterceptor 的解析一致）。
  public readonly isMockEnabled = signal(
    environment.useMock === true ||
      (typeof environment.useMock === 'object' && environment.useMock.enable === true),
  );

  // 登录表单模型（Signal Forms）
  private readonly model = signal({
    usernameOrEmail: '',
    password: '',
  });

  //#if (IncludeLocalization)
  readonly loginForm = form(this.model, (path) => {
    required(path.usernameOrEmail, {
      message: this.transloco.translate('common.validation.required'),
    });
    minLength(path.usernameOrEmail, 3, {
      message: this.transloco.translate('common.validation.minLength', { min: 3 }),
    });
    maxLength(path.usernameOrEmail, 256, { message: '' });
    required(path.password, { message: this.transloco.translate('common.validation.required') });
    minLength(path.password, 6, {
      message: this.transloco.translate('common.validation.minLength', { min: 6 }),
    });
    maxLength(path.password, 100, { message: '' });
  });
  //#else
  readonly loginForm = form(this.model, (path) => {
    required(path.usernameOrEmail, { message: 'This field is required.' });
    minLength(path.usernameOrEmail, 3, { message: 'Must be at least 3 characters.' });
    maxLength(path.usernameOrEmail, 256, { message: '' });
    required(path.password, { message: 'This field is required.' });
    minLength(path.password, 6, { message: 'Must be at least 6 characters.' });
    maxLength(path.password, 100, { message: '' });
  });
  //#endif

  constructor() {
    // 进入登录页面时清理旧的认证信息
    this.authService.clearAuthData();
  }

  /**
   * 提交登录表单
   */
  async onSubmit() {
    if (this.loginForm().invalid()) {
      this.loginForm().markAsTouched();
      return;
    }

    this._isLoading.set(true);

    try {
      const { usernameOrEmail, password } = this.model();

      const loginInput = { usernameOrEmail, password };
      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');

      await lastValueFrom(this.authService.login(loginInput));
      await lastValueFrom(this.authService.loadUser());

      // 登录成功提示
      //#if (IncludeLocalization)
      toast.success(this.transloco.translate('account.login.loginSuccess'), {
        description: this.transloco.translate('account.login.welcomeBack'),
        duration: 3000,
      });
      //#else
      toast.success('Signed in successfully', { description: 'Welcome back!', duration: 3000 });
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
      //#if (IncludeLocalization)
      toast.error(this.transloco.translate('account.login.loginFailed'), {
        description: applicationErrorMessage(error),
      });
      //#else
      toast.error('Login failed', { description: applicationErrorMessage(error) });
      //#endif
    } finally {
      this._isLoading.set(false);
    }
  }

  private isSafeLocalReturnUrl(returnUrl: string | null): returnUrl is string {
    return (
      !!returnUrl &&
      returnUrl.startsWith('/') &&
      !returnUrl.startsWith('//') &&
      !returnUrl.includes('://')
    );
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
      toast.error(this.transloco.translate('account.login.loginFailed'), {
        description: this.transloco.translate('account.login.externalLoginFailed', {
          provider: label,
        }),
        duration: 3000,
      });
      //#else
      toast.error('Sign-in failed', {
        description: `Unable to connect to the ${label} sign-in service, please try again later`,
        duration: 3000,
      });
      //#endif
      this._isLoading.set(false);
    }
  }
  //#endif
}
