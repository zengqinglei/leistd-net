import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { firstValueFrom, isObservable, of } from 'rxjs';

import { permissionGuard } from './permission-guard';
import { PERMISSIONS } from '../../shared/models/permission';
import { AuthorizationService } from '../services/authorization-service';
import { StartupService } from '../services/startup-service';

/**
 * 路由准入。
 *
 * 这里最要紧的一条是"只读路由自身的 data"：守卫刻意不用
 * `ActivatedRouteSnapshot.data`，因为它会把父路由的 data 合并进来，
 * 于是 /platform 上"拥有任一平台权限即可"的宽松声明会顺着继承链盖住子路由更严格的要求。
 * 那处改动看起来只是"换成标准写法"，却会让本该拦下的页面全部放行，且不会有任何东西报错。
 */
describe('permissionGuard', () => {
  const status = signal<'loading' | 'success' | 'failed'>('success');
  let authorization: AuthorizationService;
  let router: Router;

  function runGuard(routeConfigData: Record<string, unknown>, inheritedData = {}) {
    // routeConfig.data 是路由自身声明；snapshot.data 是合并了父路由之后的结果。
    const route = {
      data: { ...inheritedData, ...routeConfigData },
      routeConfig: { data: routeConfigData },
    } as unknown as ActivatedRouteSnapshot;

    const result = TestBed.runInInjectionContext(() =>
      permissionGuard(route, {} as RouterStateSnapshot),
    );

    return firstValueFrom(isObservable(result) ? result : of(result));
  }

  beforeEach(() => {
    status.set('success');

    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: StartupService, useValue: { status } }],
    });

    authorization = TestBed.inject(AuthorizationService);
    router = TestBed.inject(Router);

    authorization.setPermissions({
      permissions: [PERMISSIONS.users.default],
      isSuperAdmin: false,
      revision: 'r1',
    });
  });

  it('未声明权限的路由只要求已认证', async () => {
    await expectAsync(runGuard({})).toBeResolvedTo(true);
  });

  it('单个 permission 满足时放行', async () => {
    await expectAsync(runGuard({ permission: PERMISSIONS.users.default })).toBeResolvedTo(true);
  });

  it('permissions 是"任一满足"而不是"全部满足"', async () => {
    await expectAsync(
      runGuard({ permissions: [PERMISSIONS.roles.default, PERMISSIONS.users.default] }),
    ).toBeResolvedTo(true);
  });

  it('permission 与 permissions 并存时合并判定', async () => {
    await expectAsync(
      runGuard({ permission: PERMISSIONS.users.default, permissions: [PERMISSIONS.roles.default] }),
    ).toBeResolvedTo(true);
  });

  it('都不满足时跳 403 而不是登录页', async () => {
    const result = await runGuard({ permission: PERMISSIONS.roles.default });

    expect(result instanceof UrlTree).toBeTrue();
    expect(router.serializeUrl(result as UrlTree)).toBe('/403-forbidden');
  });

  it('不继承父路由的宽松声明', async () => {
    // 复刻真实路由的形状：父路由用 permissions 数组（"拥有任一平台权限即可进平台区"），
    // 子路由用单个 permission。两个键不同名，合并后会同时保留——这才是能触发
    // any-of 错误放行的组合。若两边用同一个键，子会覆盖父，无论守卫读
    // routeConfig.data 还是合并后的 data 结果都一样，用例就锁不住任何东西。
    //
    // 当前用户只有 users 权限：读自身 data 时要求 roles → 拦下；
    // 误读合并后的 data 时会因为父路由的 users 而放行。
    const result = await runGuard(
      { permission: PERMISSIONS.roles.default },
      { permissions: [PERMISSIONS.users.default] },
    );

    expect(result instanceof UrlTree).toBeTrue();
  });

  it('等到启动流结束后再判定，不在 loading 阶段抢答', async () => {
    status.set('loading');

    let settled = false;
    const pending = runGuard({ permission: PERMISSIONS.users.default }).then((value) => {
      settled = true;
      return value;
    });

    await Promise.resolve();
    expect(settled).toBeFalse();

    // 权限要到启动流结束才可信；此前放行会闪现受保护页面，拦截又会误伤刷新。
    status.set('success');
    await expectAsync(pending).toBeResolvedTo(true);
  });
});
