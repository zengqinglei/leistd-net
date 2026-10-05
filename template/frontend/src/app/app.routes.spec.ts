import { Route, Routes } from '@angular/router';

import { routes } from './app.routes';
import { authGuard } from './core/guards/auth-guard';
import { PROTECTED_ROUTE_PREFIXES } from './core/services/startup-service';
import { PLATFORM_ENTRY_PERMISSIONS } from './shared/models/permission';

describe('top-level routes', () => {
  const isCatchAllPrefix = (r: Route) => r.path === '' && !!r.loadChildren;
  const isWildcard = (r: Route) => r.path === '**';

  // 扫到的路由数为零的话，下面两条都是空的。
  it('finds a non-zero number of routes', () => {
    expect(routes.length).toBeGreaterThan(3);
  });

  it('places the empty-path + loadChildren public area after all concrete paths', () => {
    const catchAllIndexes = (routes as Routes)
      .map((r, i) => (isCatchAllPrefix(r) ? i : -1))
      .filter((i) => i >= 0);
    expect(
      catchAllIndexes.length,
      '没有找到「空路径 + loadChildren」的公开区——这条断言是空的',
    ).toBe(1);

    const catchAllAt = catchAllIndexes[0];
    const concreteAfter = (routes as Routes)
      .slice(catchAllAt + 1)
      .filter((r) => !isWildcard(r) && r.path !== '')
      .map((r) => r.path);

    expect(
      concreteAfter,
      `这些具体路径排在空路径公开区之后，会被它吞掉：${concreteAfter.join(', ')}`,
    ).toEqual([]);
  });

  it('maps PROTECTED_ROUTE_PREFIXES one-to-one to top-level authGuard routes', () => {
    const authGuarded = (routes as Routes)
      .filter((r) => r.canActivate?.includes(authGuard))
      .map((r) => `/${r.path}`)
      .sort();

    expect(
      authGuarded.length,
      '没有找到任何使用 authGuard 的顶层路由——这条断言是空的',
    ).toBeGreaterThan(0);

    expect(
      [...PROTECTED_ROUTE_PREFIXES].sort() as string[],
      `路由表里使用 authGuard 的是 ${authGuarded.join(', ')}`,
    ).toEqual(authGuarded);
  });

  /**
   * 路由守卫与菜单/重定向必须用同一份权限清单。
   *
   * 两处曾各自硬编码，在多租户场景下不等价：路由含 tenants、canAccessPlatform 不含，
   * 于是只有租户管理权限的账号菜单里没有入口、登录后被重定向走，但直接敲 URL 能进。
   * 只把两处改成引用同一常量还不够——下一个人仍可能在路由里手写补一项，
   * 所以这里断言"同源"，让分叉在 CI 里立刻失败。
   */
  it('shares the /platform route allowlist with canAccessPlatform', () => {
    const platformRoute = routes.find((route) => route.path === 'platform');

    expect(platformRoute, '/platform 路由不存在').toBeDefined();
    expect(platformRoute?.data?.['permissions']).toEqual([...PLATFORM_ENTRY_PERMISSIONS]);
  });
});
