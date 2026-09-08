/**
 * 从地址栏读当前的应用内 URL（含查询串）。
 *
 * 为什么不读 `Router.url`：启动流跑在 `provideAppInitializer` 里、初始导航之前，
 * 那时它一律是 `/`——直接打开 `/platform/users` 这种深链时，用它记落地地址会记成 `/`，
 * 用它判断"当前在哪条路由上"会把 OIDC 回调页误判成普通页面。地址栏在两个阶段都准，
 * 因此这里只认地址栏，不再掺第二个来源。哈希路由下整条应用
 * URL 都落在 `location.hash` 里，查询串也在其中。
 */
export function entryRouteUrl(): string {
  const hash = window.location.hash;
  return hash.startsWith('#/')
    ? hash.slice(1)
    : `${window.location.pathname}${window.location.search}`;
}

/**
 * 从入口 URL 里取出应用路径（去掉查询串），供路由归类使用。
 *
 * 归类要拿路径去比，不能拿整条 URL 去 includes：`/#/workspace?returnUrl=/auth/callback`
 * 里出现 `/auth/callback`，人并不在回调页——那样的误判会让普通会话过期走进认证流程
 * 专用的处置分支。落地地址仍用 {@link entryRouteUrl}，它必须保留查询串。
 */
export function entryRoutePath(): string {
  return entryRouteUrl().split('?')[0];
}

/**
 * 当前是否停在认证路由上（登录页、OIDC 回调、外部登录回调）。
 *
 * 这些路由上正有一条认证流程在跑：它自己知道该怎么处置 401，别处不要插手。
 */
export function isOnAuthRoute(): boolean {
  return entryRoutePath().startsWith('/auth/');
}
