import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { toast } from '@spartan-ng/brain/sonner';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { lastValueFrom } from 'rxjs';

import { applicationErrorMessage } from '../../../../core/errors/application-http-error';
import { AuthService } from '../../../../core/services/auth-service';
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { SessionContextService } from '../../../../core/services/session-context-service';
import { AccountService } from '../../services/account-service';
import { EXTERNAL_LINK_PENDING_KEY } from '../external-logins/external-logins';

/**
 * 外部登录回调组件
 *
 * 处理 GitHub/Google 等第三方登录重定向回来后的流程:
 * 1. 从 URL query params 中获取 code、state、provider
 * 2. 将 code+state 发送到后端换取 Cookie session
 * 3. 加载用户信息并根据角色跳转
 */
@Component({
  selector: 'app-external-auth-callback',
  imports: [HlmSpinner],
  template: `
    <main class="flex min-h-screen items-center justify-center bg-background px-4">
      <section class="text-center">
        @if (error()) {
          <h1 class="mb-3 text-2xl font-semibold text-destructive">{{ failedTitle() }}</h1>
          <p class="text-muted-foreground">{{ error() }}</p>
        } @else {
          <hlm-spinner class="text-4xl" [attr.aria-label]="processingAria()" />
          <h1 class="mt-4 text-2xl font-semibold text-foreground">
            {{ processingTitle() }}
          </h1>
        }
      </section>
    </main>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ExternalAuthCallback implements OnInit {
  private authService = inject(AuthService);
  private readonly authorizationService = inject(AuthorizationService);
  private readonly sessionContext = inject(SessionContextService);
  private accountService = inject(AccountService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif

  protected readonly error = signal<string | null>(null);

  // 内联模板里的条件文案：.ts 的模板字符串区不支持 HTML 注释式条件指令，改用 getter 承载。
  //#if (IncludeLocalization)
  protected readonly failedTitle = () => this.transloco.translate('account.login.loginFailed');
  protected readonly processingAria = () =>
    this.transloco.translate('account.externalCallback.processingAria');
  protected readonly processingTitle = () =>
    this.transloco.translate('account.externalCallback.processing');
  //#else
  protected readonly failedTitle = () => 'Sign-in failed';
  protected readonly processingAria = () => 'Completing sign-in';
  protected readonly processingTitle = () => 'Processing third-party sign-in';
  //#endif

  ngOnInit(): void {
    this.processCallback();
  }

  private async processCallback(): Promise<void> {
    const params = this.route.snapshot.queryParamMap;
    const code = params.get('code');
    const state = params.get('state');
    const provider = params.get('provider') ?? this.route.snapshot.paramMap.get('provider');

    if (!code || !provider) {
      //#if (IncludeLocalization)
      this.error.set(this.transloco.translate('account.externalCallback.missingParams'));
      //#else
      this.error.set('Missing required callback parameters');
      //#endif
      return;
    }

    // 绑定外部账号时出发前记下了提供商：这次回来是绑定，不是登录
    const pendingLink = sessionStorage.getItem(EXTERNAL_LINK_PENDING_KEY);
    sessionStorage.removeItem(EXTERNAL_LINK_PENDING_KEY);
    if (pendingLink === provider) {
      await this.completeLink(provider, code, state ?? '');
      return;
    }

    try {
      // 1. 将 code+state 发送到后端建立 Cookie session
      const result = await lastValueFrom(
        this.accountService.externalLoginCallback(provider, { provider, code, state: state ?? '' }),
      );

      // 已启用两步验证：会话还没下发，回登录页做第二步（凭据走导航状态，不进地址栏）
      if (result?.requiresTwoFactor && result.twoFactorToken) {
        await this.router.navigate(['/auth/login'], {
          state: { twoFactorToken: result.twoFactorToken },
        });
        return;
      }

      // 2. 建立会话上下文（权限 + 设置）并按权限跳转。
      //    设置也必须在这里就位：SPA 内跳转不会重跑应用初始化器。
      await lastValueFrom(this.authService.loadUser());
      if (this.authService.currentUser()?.twoFactorSetupRequired) {
        await this.router.navigate(['/auth/two-factor-setup']);
        return;
      }
      await this.sessionContext.establish();
      if (this.authorizationService.canAccessPlatform()) {
        this.router.navigate(['/platform']);
      } else {
        this.router.navigate(['/workspace']);
      }
    } catch (err) {
      console.error('External login callback processing failed', err);
      //#if (IncludeLocalization)
      this.error.set(this.transloco.translate('account.externalCallback.failed'));
      //#else
      this.error.set('Third-party sign-in failed. Please go back and try again.');
      //#endif
    }
  }

  /** 完成绑定并回到「账户与安全」面板；失败也回去，由那里的列表反映实际状态。 */
  private async completeLink(provider: string, code: string, state: string): Promise<void> {
    try {
      await lastValueFrom(
        this.accountService.linkExternalLogin(provider, { provider, code, state }),
      );
      //#if (IncludeLocalization)
      toast.success(this.transloco.translate('account.externalLogins.linked'));
      //#else
      toast.success('Account linked');
      //#endif
    } catch (error) {
      //#if (IncludeLocalization)
      toast.error(this.transloco.translate('common.requestError'), {
        description: applicationErrorMessage(error),
      });
      //#else
      toast.error('Request failed', { description: applicationErrorMessage(error) });
      //#endif
    }

    // 回调页整页加载时启动流程不取当前用户（它按登录流程处理），这里补上再回设置页；
    // 取不到说明会话已不在，交给路由守卫带去登录页
    try {
      await lastValueFrom(this.authService.loadUser());
      await this.sessionContext.establish();
    } catch {
      // 由守卫处理
    }
    await this.router.navigate(['/workspace/settings/security']);
  }
}
