import { inject } from '@angular/core';
import { Router } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { toast } from '@spartan-ng/brain/sonner';
import { lastValueFrom } from 'rxjs';

import { ReturnNavigation, ReturnUrlService } from '../../../core/routing/return-url-service';
import { AuthService } from '../../../core/services/auth-service';
import { AuthorizationService } from '../../../core/services/authorization-service';
import { SessionContextService } from '../../../core/services/session-context-service';

/**
 * 会话已下发之后的共同收尾（口令单步、免口令、第二步通过）：回授权端点时直接整页离开；
 * 否则取当前用户、建立会话上下文、提示并跳转。须在注入上下文里创建。
 * @returns 收尾函数；结果说明是否已发起整页离开（离开时调用方不复位加载态）。
 */
export function injectLoginCompletion(): (returnUrl: string | null) => Promise<ReturnNavigation> {
  const authService = inject(AuthService);
  const authorizationService = inject(AuthorizationService);
  const sessionContext = inject(SessionContextService);
  const returnUrls = inject(ReturnUrlService);
  const router = inject(Router);
  //#if (IncludeLocalization)
  const transloco = inject(TranslocoService);
  //#endif

  // 按权限进入默认页：拥有任一平台入口权限才进管理区，而不是按角色名或超管标志判断
  const enterLandingPage = () =>
    router.navigate([authorizationService.canAccessPlatform() ? '/platform' : '/workspace']);

  return async (returnUrl) => {
    //#if (OpenIddictServer)
    // 回到授权端点是整页离开：服务端会重新校验会话、重新认证证明与首绑要求（受限会话由它送去设置页），
    // 资源应用启动时自建上下文。在这里读用户、应用账户偏好、弹成功提示，只会让即将离开的页面先变一下
    if (returnUrls.isServerPage(returnUrl)) {
      sessionContext.clear();
      return returnUrls.navigate(returnUrl, enterLandingPage);
    }
    //#endif
    sessionContext.clear();
    await lastValueFrom(authService.loadUser());
    // 受限会话先完成首绑，不应用账户偏好。
    if (authService.currentUser()?.twoFactorSetupRequired) {
      return (await router.navigateByUrl(returnUrls.twoFactorSetup(returnUrl)))
        ? 'routed'
        : 'cancelled';
    }
    const result = await sessionContext.enter(() =>
      returnUrls.navigate(returnUrl, enterLandingPage),
    );
    if (result !== 'cancelled') {
      // 导航成功且偏好已提交，提示使用目标页的语言。
      //#if (IncludeLocalization)
      toast.success(transloco.translate('account.login.loginSuccess'), {
        description: transloco.translate('account.login.welcomeBack'),
        duration: 3000,
      });
      //#else
      toast.success('Signed in successfully', { description: 'Welcome back!', duration: 3000 });
      //#endif
    }
    return result;
  };
}
