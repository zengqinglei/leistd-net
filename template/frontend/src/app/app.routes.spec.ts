import { Route, Routes } from '@angular/router';

import { routes } from './app.routes';
import { authGuard } from './core/guards/auth-guard';
import { PROTECTED_ROUTE_PREFIXES } from './core/services/startup-service';

describe('顶层路由表', () => {
  const isCatchAllPrefix = (r: Route) => r.path === '' && !!r.loadChildren;
  const isWildcard = (r: Route) => r.path === '**';

  it('扫到的路由数不为零——否则下面两条都是空的', () => {
    expect(routes.length).toBeGreaterThan(3);
  });

  it('空路径 + loadChildren 的公开区必须排在所有具体路径之后', () => {
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
    const authGuarded = (routes as Routes)
      .filter((r) => r.canActivate?.includes(authGuard))
      .map((r) => `/${r.path}`)
      .sort();

    expect(authGuarded.length)
      .withContext('没有找到任何使用 authGuard 的顶层路由——这条断言是空的')
      .toBeGreaterThan(0);

    expect([...PROTECTED_ROUTE_PREFIXES].sort() as string[])
      .withContext(`路由表里使用 authGuard 的是 ${authGuarded.join(', ')}`)
      .toEqual(authGuarded);
  });
});
