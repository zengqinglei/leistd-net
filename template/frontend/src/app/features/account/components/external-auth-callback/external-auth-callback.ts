import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
//#endif
import { toast } from '@spartan-ng/brain/sonner';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { lastValueFrom } from 'rxjs';

import {
  ApplicationHttpError,
  applicationErrorMessage,
} from '../../../../core/errors/application-http-error';
import { AuthService } from '../../../../core/services/auth-service';
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { SessionContextService } from '../../../../core/services/session-context-service';
//#if (!IncludeLocalization)
import { englishText } from '../../../../shared/utils/english-text';
//#endif
import { AccountService } from '../../services/account-service';
import { EXTERNAL_LINK_PENDING_KEY } from '../external-logins/external-logins';

/**
 * 外部登录回调组件
 *
 * 处理 GitHub/Google 等第三方登录重定向回来后的流程:
 * 1. 从路由参数取 provider（回调地址 /auth/external-callback/{provider}），从查询串取 code、state
 * 2. 将 code+state 发送到后端换取 Cookie session
 * 3. 加载用户信息并根据角色跳转
 */
@Component({
  selector: 'app-external-auth-callback',
  // prettier-ignore
  imports: [
    HlmSpinner,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  templateUrl: './external-auth-callback.html',
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
  //#else
  protected readonly t = englishText(ENGLISH);
  //#endif

  /**
   * 失败说明：本端的说法存词条键、由模板按当前语言取，服务端下发的原因原样显示。
   * 存成已翻译的文字的话，停在这页切换语言时它不会跟着变。
   */
  protected readonly error = signal<{ key: string } | { text: string } | null>(null);

  ngOnInit(): void {
    this.processCallback();
  }

  private async processCallback(): Promise<void> {
    const params = this.route.snapshot.queryParamMap;
    const code = params.get('code');
    const state = params.get('state');
    const provider = this.route.snapshot.paramMap.get('provider');

    if (!code || !provider) {
      this.error.set({ key: 'account.externalCallback.missingParams' });
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
      // 业务拒绝（如"该邮箱已有账号，请先登录再绑定"）要让用户知道下一步怎么做，展示服务端下发的原因；
      // 其余失败只给通用提示
      if (err instanceof ApplicationHttpError && err.status >= 400 && err.status < 500) {
        this.error.set({ text: err.message });
        return;
      }
      this.error.set({ key: 'account.externalCallback.failed' });
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
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'account.login.loginFailed': 'Sign-in failed',
  'account.externalCallback.processingAria': 'Completing sign-in',
  'account.externalCallback.processing': 'Processing third-party sign-in',
  'account.externalCallback.missingParams': 'Missing required callback parameters',
  'account.externalCallback.failed': 'Third-party sign-in failed. Please go back and try again.',
};
//#endif
