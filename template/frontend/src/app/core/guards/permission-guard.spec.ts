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
import { PERMISSIONS } from '../../shared/constants/permission.constants';
import { AuthorizationService } from '../services/authorization-service';
import { StartupService } from '../services/startup-service';

/**
 * 路由准入。守卫只读路由自身的 data（`routeConfig.data`）：合并后的 `snapshot.data` 会让父路由的
 * 宽松声明盖住子路由更严格的要求。
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
      versionToken: 'r1',
    });
  });

  it('requires only authentication for routes without declared permissions', async () => {
    await expect(runGuard({})).resolves.toEqual(true);
  });

  it('allows access when the single permission is granted', async () => {
    await expect(runGuard({ permission: PERMISSIONS.users.default })).resolves.toEqual(true);
  });

  it('treats permissions as any-of rather than all-of', async () => {
    await expect(
      runGuard({ permissions: [PERMISSIONS.roles.default, PERMISSIONS.users.default] }),
    ).resolves.toEqual(true);
  });

  it('combines permission and permissions when both are declared', async () => {
    await expect(
      runGuard({ permission: PERMISSIONS.users.default, permissions: [PERMISSIONS.roles.default] }),
    ).resolves.toEqual(true);
  });

  it('redirects to 403 instead of the login page when nothing is granted', async () => {
    const result = await runGuard({ permission: PERMISSIONS.roles.default });

    expect(result instanceof UrlTree).toBe(true);
    expect(router.serializeUrl(result as UrlTree)).toBe('/403-forbidden');
  });

  it('does not inherit the looser declaration of the parent route', async () => {
    // 复刻真实路由：父路由用 permissions 数组、子路由用单个 permission，合并后两键同时保留才能触发
    // 错误放行。当前用户只有 users 权限：读自身 data 时要求 roles 被拦下，误读合并后的 data 会放行。
    const result = await runGuard(
      { permission: PERMISSIONS.roles.default },
      { permissions: [PERMISSIONS.users.default] },
    );

    expect(result instanceof UrlTree).toBe(true);
  });

  it('waits for startup to finish instead of deciding during loading', async () => {
    status.set('loading');

    let settled = false;
    const pending = runGuard({ permission: PERMISSIONS.users.default }).then((value) => {
      settled = true;
      return value;
    });

    await Promise.resolve();
    expect(settled).toBe(false);

    // 权限只在启动流结束后可信；在此之前既不能放行受保护页面，也不能拒绝刷新。
    status.set('success');
    await expect(pending).resolves.toEqual(true);
  });
});
