import { Location } from '@angular/common';
import { Injectable, inject } from '@angular/core';

/**
 * 当前停在哪条应用路由上，读自 Angular 的 `Location`。
 *
 * 为什么不读 `Router.url`：启动流跑在 `provideAppInitializer` 里、初始导航之前，那时它一律是 `/`——
 * 直接打开 `/platform/users` 这种深链时，用它记落地地址会记成 `/`，用它判断"当前在哪条路由上"
 * 会把 OIDC 回调页误判成普通页面。`Location` 按所配置的路径或哈希策略直接解析地址栏并去掉
 * base href，初始导航前后都准。
 */
@Injectable({ providedIn: 'root' })
export class EntryRouteService {
  private readonly location = inject(Location);

  /**
   * 应用内 URL，保留查询串（用作落地地址）。
   *
   * `Location.path()` 对站点根给出 `''`、对带查询串的根给出 `?a=1`，这里统一补成以 `/` 开头。
   */
  url(): string {
    const path = this.location.path();
    return path.startsWith('/') ? path : `/${path}`;
  }

  /**
   * 应用路径（去掉查询串），供路由归类使用。
   *
   * 归类要拿路径去比，不能拿整条 URL 去 includes：`/#/workspace?returnUrl=/auth/callback`
   * 里出现 `/auth/callback`，人并不在回调页——那样的误判会让普通会话过期走进认证流程
   * 专用的处置分支。
   */
  path(): string {
    return this.url().split('?')[0];
  }

  /**
   * 当前是否停在认证路由上（登录页、OIDC 回调、外部登录回调）。
   *
   * 这些路由上正有一条认证流程在跑：它自己知道该怎么处置 401，别处不要插手。
   */
  isOnAuthRoute(): boolean {
    return this.path().startsWith('/auth/');
  }
}
