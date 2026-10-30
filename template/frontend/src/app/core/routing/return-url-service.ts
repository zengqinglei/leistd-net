//#if (OpenIddictServer)
import { DOCUMENT } from '@angular/common';
//#endif
import { Injectable, inject } from '@angular/core';
import { Router, UrlTree } from '@angular/router';

/** 强制两步验证设置页：组织要求两步验证而本人尚未启用时，受限会话只能停在这里。 */
export const TWO_FACTOR_SETUP_URL = '/auth/two-factor-setup';

/**
 * {@link ReturnUrlService.navigate} 的去向：`left` 已发起整页导航（旧页面稍后才卸载，调用方不该再复位界面）；
 * `routed` 已在应用内跳转到原目标；`fallback` 没有可接续的目标，已执行默认去向。
 */
export type ReturnNavigation = 'left' | 'routed' | 'fallback';

/**
 * 认证流程之后回到原目标（`returnUrl`）：守卫、登录、外部登录完成、登录第二步与强制设置页共用一套判定与跳转。
 *
 * 只接受站内地址；`/connect/*`（OIDC 授权端点）要整页回到服务端，原样保留 `client_id`、`request_uri`
 * 与重新认证证明，不能当成前端路由。地址里只有导航目标，口令、验证码与待提交的修改从不放进去。
 */
@Injectable({ providedIn: 'root' })
export class ReturnUrlService {
  private readonly router = inject(Router);
  //#if (OpenIddictServer)
  private readonly document = inject(DOCUMENT);
  //#endif

  /**
   * 可接续的目标：站内绝对路径才算数；空、外站、协议相对（`//`）、含反斜杠的地址，
   * 以及指向设置页自身的地址（会原地打转）一律返回 `null`，由调用方回到默认页。
   */
  resolve(returnUrl: string | null | undefined): string | null {
    if (
      !returnUrl ||
      !returnUrl.startsWith('/') ||
      returnUrl.startsWith('//') ||
      returnUrl.includes('://') ||
      returnUrl.includes('\\')
    ) {
      return null;
    }

    return returnUrl.split(/[?#]/)[0] === TWO_FACTOR_SETUP_URL ? null : returnUrl;
  }

  /** 去强制设置页的地址，带上可接续的原目标（不可接续时不带）。 */
  twoFactorSetup(returnUrl: string | null | undefined): UrlTree {
    const target = this.resolve(returnUrl);
    return this.router.createUrlTree([TWO_FACTOR_SETUP_URL], {
      queryParams: target ? { returnUrl: target } : {},
    });
  }

  /**
   * 回到原目标；没有可接续的目标时执行 `fallback`（通常按权限去默认页）。
   * 应用内跳转与 `fallback` 之前会话上下文（用户、权限、设置）必须已经就位：SPA 内跳转不会重跑应用初始化器。
//#if (OpenIddictServer)
   * 服务端整页地址（见 {@link isServerPage}）不需要：新页面自己建立上下文，调用方可以跳过这些准备直接离开。
//#endif
   */
  async navigate(
    returnUrl: string | null | undefined,
    fallback: () => Promise<unknown>,
  ): Promise<ReturnNavigation> {
    //#if (OpenIddictServer)
    if (this.isServerPage(returnUrl)) {
      this.loadPage(returnUrl);
      return 'left';
    }
    //#endif
    const target = this.resolve(returnUrl);
    if (!target) {
      await fallback();
      return 'fallback';
    }
    await this.router.navigateByUrl(target);
    return 'routed';
  }
  //#if (OpenIddictServer)

  /**
   * 原目标是授权端点（`/connect/*`）：它不是前端路由，要整页回到服务端继续同一个缓存的授权请求。
   * 服务端会重新校验会话、重新认证证明与首绑要求，离开前不必再读当前用户或建立会话上下文。
   */
  isServerPage(returnUrl: string | null | undefined): returnUrl is string {
    return this.resolve(returnUrl)?.startsWith('/connect/') === true;
  }

  /** 整页导航到服务端地址。单独成方法，测试据此观察跳转而不真的离开页面。 */
  loadPage(url: string): void {
    this.document.location.assign(url);
  }
  //#endif
}
