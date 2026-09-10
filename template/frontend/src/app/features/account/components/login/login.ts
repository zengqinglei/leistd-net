import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { form, minLength, maxLength, required, FormField } from '@angular/forms/signals';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
// prettier-ignore
import {
  lucideZap,
  lucideCircleCheck,
  lucideInfo,
  lucideEye,
  lucideEyeOff,
  lucideX,
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
// prettier-ignore
import {
  ApplicationHttpError,
  applicationErrorMessage,
} from '../../../../core/errors/application-http-error';
import { AuthService } from '../../../../core/services/auth-service';
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { SessionContextService } from '../../../../core/services/session-context-service';
import { TenantContextService } from '../../../../core/services/tenant-context-service';
import { PASSWORD_MAX_LENGTH } from '../../../../core/validation/password-rule';
//#if (IncludeLocalization)
import { LanguageSwitcher } from '../../../../shared/components/language-switcher/language-switcher';
//#endif
import { Logo } from '../../../../shared/components/logo/logo';
import { ThemeModeToggle } from '../../../../shared/components/theme-mode-toggle/theme-mode-toggle';
import { HostTenantDecision } from '../../../../shared/dtos/tenant.dto';
import { TenantService } from '../../../platform/services/tenant-service';
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
  // prettier-ignore
  providers: [
    provideIcons({
      lucideZap,
      lucideX,
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
  private readonly authorizationService = inject(AuthorizationService);
  private readonly sessionContext = inject(SessionContextService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif
  private readonly tenantService = inject(TenantService);
  protected readonly tenantContext = inject(TenantContextService);

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
    // 登录只设防滥用上限，不校验口令策略：策略生效前设置的旧口令也必须能登录。
    maxLength(path.password, PASSWORD_MAX_LENGTH, { message: '' });
  });
  //#else
  readonly loginForm = form(this.model, (path) => {
    required(path.usernameOrEmail, { message: 'This field is required.' });
    minLength(path.usernameOrEmail, 3, { message: 'Must be at least 3 characters.' });
    maxLength(path.usernameOrEmail, 256, { message: '' });
    required(path.password, { message: 'This field is required.' });
    maxLength(path.password, PASSWORD_MAX_LENGTH, { message: '' });
  });
  //#endif

  constructor() {
    // 进入登录页时清理上一个主体的全部痕迹：认证数据、权限、设置。
    // 只清认证数据不够——已登录用户在 SPA 内导航到这里不会重跑应用初始化器，
    // 旧权限和设置会留在内存里，新用户登录后若权限加载失败就会看到上一个人的偏好。
    this.sessionContext.clear();

    // 子域名部署下按主机名把租户定住，用户完全不必填；未命中则保持原状（上次记住的或空白）。
    void this.resolveTenantFromHost();
  }

  /**
   * 提交登录表单
   */
  async onSubmit() {
    // 租户上下文没定案就不发认证请求。按钮已经禁用，这里再挡一次是因为回车提交、
    // 以及探测在"表单填完、按钮刚点下"之间才失败的时序都绕不过表单事件。
    if (this.authBlocked()) {
      return;
    }

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

      // 会话上下文必须在任何跳转之前建立完成。进登录页时它已被清空，此时直接跳 returnUrl：
      // permissionGuard 会在空权限下判定并把人踢到 403——从受保护页面的深链登录，
      // 本该落到那个页面，却落在拒绝页。设置也在这里就位，否则保存过的显示偏好
      // 要到下一次硬刷新才生效（SPA 内跳转不会重跑应用初始化器）。
      await this.sessionContext.establish();

      // 成功提示放在会话建立之后：它一旦失败就走 catch 弹「登录失败」，
      // 提前提示会让用户先看到成功、紧接着看到失败，而人还停在登录页。
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

      // 按权限跳转：拥有任一平台入口权限才进管理区，而不是按角色名或超管标志判断。
      if (this.authorizationService.canAccessPlatform()) {
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

  // 租户选择：确认后写入本地上下文，登录请求由拦截器附 X-Tenant-Id；不选即宿主登录。
  readonly tenantName = signal('');
  protected readonly tenantChecking = signal(false);
  readonly tenantError = signal<string | null>(null);

  /**
   * 主机名探测的进度与结果。
   *
   * 五档，缺一不可：
   * - `pending` 探测未回来。它一旦定案就会覆盖当前上下文，所以这段时间租户区不可操作，
   *   认证入口也不能放行——否则用户会带着一个即将被换掉的租户点下登录。
   * - `tenant` / `host` 域名已定案，界面不能再改。
   * - `undecided` 域名不表态（未配置子域名格式的部署），交回用户手填。
   * - `failed` 探测**没能完成**。这不等于"域名不表态"：域名不存在的租户时租户解析中间件
   *   直接 404，那是"这个地址指向一个用不了的租户"；瞬时网络故障也证明不了域名没有约束。
   *   把失败折进 undecided，等于在不知道域名会怎么解析的情况下让人手选一个注定被覆盖的
   *   租户，然后带着它去登录。
   */
  readonly hostProbe = signal<HostTenantDecision | 'pending' | 'failed'>('pending');

  /**
   * 租户由**域名**定案，界面不能再改。
   *
   * 两种定案都要锁：指向某个租户，或指向宿主。服务端按主机名解析且不允许请求头改写，
   * 前端放开选择只会让界面显示的租户与服务端将要用的那个不一致——
   * 用户以为在某个租户下登录，请求其实落在宿主（或反之）。
   */
  readonly tenantLocked = computed(() => {
    const decision = this.hostProbe();
    return decision === 'host' || decision === 'tenant';
  });

  /**
   * 租户区不接受操作：探测还没回来、探测失败，或域名已经定案。
   *
   * 只有明确拿到 `undecided` 才开放手选。前两种情形下界面并不知道域名会怎么解析，
   * 让人先挑一个，结果只会被随后的定案无声换掉。
   */
  readonly tenantSelectionBlocked = computed(
    () => this.hostProbe() !== 'undecided' || this.tenantLocked(),
  );

  /**
   * 租户上下文还没定案，认证入口一律等它。
   *
   * 与 {@link tenantSelectionBlocked} 分开：域名已经定案时不允许**选择**，但恰恰应该允许
   * 登录。反过来，探测未回来或失败时不能登录——服务端按主机名解析且不接受请求头改写，
   * 此时提交等于在前端显示着一个租户、请求却落到另一个上下文里。
   */
  readonly authBlocked = computed(
    () => this.hostProbe() === 'pending' || this.hostProbe() === 'failed',
  );

  /**
   * 开机按主机名探测一次租户：子域名部署下用户完全不必填。
   *
   * 三档结果分别处理——把"宿主定案"和"域名不表态"混成一档，会让宿主域上残留着
   * 上次记住的租户，而请求已经按宿主发出去了。
   */
  private async resolveTenantFromHost(): Promise<void> {
    try {
      const result = await lastValueFrom(this.tenantService.getByHost());
      switch (result.decision) {
        case 'tenant':
          // 租户不存在或已停用时 tenant 为空：清掉记住的那个，并保持锁定，
          // 让界面提示这个域名不可用，而不是让人换一个反正会被覆盖的租户。
          if (result.tenant?.isActive) {
            this.tenantContext.set(result.tenant);
          } else {
            this.tenantContext.clear();
            this.tenantError.set(this.tenantUnavailableMessage());
          }
          break;

        case 'host':
          // 受管域但不指向租户：服务端会按宿主处理，记住的租户必须清掉。
          this.tenantContext.clear();
          break;

        case 'undecided':
          // 域名不表态：保持现状（上次记住的租户，或空白待填）。
          break;
      }

      this.hostProbe.set(result.decision);
    } catch {
      // 保留成独立的失败态：未配置子域名格式的部署本来就正常返回 undecided，走不到这里。
      // 能走到这里的是"域名指向的租户解析不了"（中间件 404）或后端不可达，两者都不能
      // 推断成"域名不表态"，所以不开放手选、也不放行登录，只给出原因和重试。
      this.hostProbe.set('failed');
      this.tenantError.set(this.tenantProbeFailedMessage());
    }
  }

  /**
   * 重试域名探测。
   *
   * 瞬时故障不该让人只剩"刷新整页"这一条路——尤其表单可能已经填好了。
   */
  async retryHostProbe(): Promise<void> {
    if (this.hostProbe() !== 'failed') {
      return;
    }

    this.hostProbe.set('pending');
    this.tenantError.set(null);
    await this.resolveTenantFromHost();
  }

  async onConfirmTenant(): Promise<void> {
    const name = this.tenantName().trim();
    // 探测未回来 / 域名已定案时不接受手选：前者会被随后的探测结果覆盖，后者本就不该能改。
    if (!name || this.tenantChecking() || this.tenantSelectionBlocked()) {
      return;
    }

    this.tenantChecking.set(true);
    this.tenantError.set(null);

    try {
      const tenant = await lastValueFrom(this.tenantService.getByName(name));
      if (!tenant.isActive) {
        this.tenantError.set(this.tenantInactiveMessage());
        return;
      }
      this.tenantContext.set(tenant);
      this.tenantName.set('');
    } catch (error) {
      if (error instanceof ApplicationHttpError && error.status === 404) {
        this.tenantError.set(this.tenantNotFoundMessage());
      } else {
        this.tenantError.set(applicationErrorMessage(error));
      }
    } finally {
      this.tenantChecking.set(false);
    }
  }

  clearTenant(): void {
    // 与手选同一条闸门：清除也是一次手动改租户，探测未回来或失败时同样不该放行。
    if (this.tenantSelectionBlocked()) {
      return;
    }

    this.tenantContext.clear();
    this.tenantError.set(null);
  }

  //#if (IncludeLocalization)
  private tenantNotFoundMessage = () => this.transloco.translate('account.login.tenantNotFound');
  private tenantInactiveMessage = () => this.transloco.translate('account.login.tenantInactive');
  private tenantUnavailableMessage = () =>
    this.transloco.translate('account.login.tenantUnavailable');
  private tenantProbeFailedMessage = () =>
    this.transloco.translate('account.login.tenantProbeFailed');
  //#else
  private tenantNotFoundMessage = () => 'Tenant does not exist';
  private tenantInactiveMessage = () => 'Tenant is deactivated';
  private tenantUnavailableMessage = () =>
    'The tenant this address points to is unavailable. Contact your administrator.';
  private tenantProbeFailedMessage = () =>
    'Could not determine the tenant for this address. Check your connection and try again.';
  //#endif
  //#if (ExternalLogin)

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
    // 与本地登录同一条约束：第三方回调最终也落在按主机名解析出的那个上下文里。
    if (this.authBlocked()) {
      return;
    }

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
