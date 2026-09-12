import { Route, Routes } from '@angular/router';

import { routes } from './app.routes';
import { PROTECTED_ROUTE_PREFIXES } from './core/services/startup-service';

/**
 * 路由表的两条结构约束。两条都**没有运行期机制维持**，违反了也不会报错，
 * 所以它们只能由用例守着。
 */
describe('顶层路由表', () => {
  const isCatchAllPrefix = (r: Route) => r.path === '' && !!r.loadChildren;
  const isWildcard = (r: Route) => r.path === '**';

  it('扫到的路由数不为零——否则下面两条都是空的', () => {
    expect(routes.length).toBeGreaterThan(3);
  });

  it('空路径 + loadChildren 的公开区必须排在所有具体路径之后', () => {
    // 空路径按**前缀**匹配，配上 loadChildren 就会匹配任何 URL；
    // 而路由器进入懒加载配置后匹配不上**不会退回来重试兄弟路由**。
    // 它排在前面时，`/auth/callback` 这类具体路径会被它吞掉，且全程无报错。
    const catchAllIndexes = (routes as Routes)
      .map((r, i) => (isCatchAllPrefix(r) ? i : -1))
      .filter((i) => i >= 0);
    expect(catchAllIndexes.length)
      .withContext('没有找到「空路径 + loadChildren」的公开区——这条断言是空的')
      .toBe(1);

    const catchAllAt = catchAllIndexes[0];
    const concreteAfter = (routes as Routes)
      .slice(catchAllAt + 1)
      .filter((r) => !isWildcard(r) && r.path !== '')
      .map((r) => r.path);

    expect(concreteAfter)
      .withContext(`这些具体路径排在空路径公开区之后，会被它吞掉：${concreteAfter.join(', ')}`)
      .toEqual([]);
  });

  it('PROTECTED_ROUTE_PREFIXES 必须与挂了 authGuard 的顶层路由一一对应', () => {
    // 这条对应关系在运行期没有任何东西维持（startup-service 反向引 routes 会成环）。
    // 新增一个带 authGuard 的区而忘了加进那个常量时，那个区不等主体就位就渲染，
    // 表现是受保护入口一闪而过，而不是报错。
    const guarded = (routes as Routes)
      .filter((r) => Array.isArray(r.canActivate) && r.canActivate.length > 0)
      .map((r) => `/${r.path}`)
      .sort();

    expect(guarded.length)
      .withContext('没有找到任何带守卫的顶层路由——这条断言是空的')
      .toBeGreaterThan(0);

    expect([...PROTECTED_ROUTE_PREFIXES].sort() as string[])
      .withContext(`路由表里带守卫的是 ${guarded.join(', ')}`)
      .toEqual(guarded);
  });
});
