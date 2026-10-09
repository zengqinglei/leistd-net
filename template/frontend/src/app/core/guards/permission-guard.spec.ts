import { Location } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  NavigationCancel,
  provideRouter,
  RedirectCommand,
  Router,
  RouterStateSnapshot,
} from '@angular/router';
import { firstValueFrom, isObservable, of } from 'rxjs';

import { permissionGuard } from './permission-guard';
import { PERMISSIONS } from '../../shared/constants/permission.constants';
import { User } from '../../shared/models/user.model';
import { ApplicationHttpError } from '../errors/application-http-error';
import { EntryRouteService } from '../routing/entry-route-service';
import { AuthService } from '../services/auth-service';
import { AuthorizationService } from '../services/authorization-service';
//#if (Impersonation)
import { ImpersonationService } from '../services/impersonation-service';
//#endif
import { SessionContextService } from '../services/session-context-service';
import { StartupService } from '../services/startup-service';

/**
 * 路由准入。守卫只读路由自身的 data（`routeConfig.data`）：合并后的 `snapshot.data` 会让父路由的
 * 宽松声明盖住子路由更严格的要求。
 */
describe('permissionGuard', () => {
  const initializeAuth = vi.fn<() => Promise<void>>();
  let authorization: AuthorizationService;
  let router: Router;

  function runGuard(routeConfigData: Record<string, unknown>, inheritedData = {}) {
    // routeConfig.data 是路由自身声明；snapshot.data 是合并了父路由之后的结果。
    const route = {
      data: { ...inheritedData, ...routeConfigData },
      routeConfig: { data: routeConfigData },
    } as unknown as ActivatedRouteSnapshot;

    const result = TestBed.runInInjectionContext(() =>
      permissionGuard(route, { url: '/platform/roles' } as RouterStateSnapshot),
    );

    return firstValueFrom(isObservable(result) ? result : of(result));
  }

  /** 从受保护的深链跑一次启动流；`failure` 给出时探测会话以该错误失败。 */
  async function startUp(failure?: unknown): Promise<void> {
    TestBed.inject(Location).replaceState('/platform');
    if (failure) {
      initializeAuth.mockRejectedValue(failure);
    }
    await TestBed.inject(StartupService).load();
  }

  beforeEach(async () => {
    initializeAuth.mockResolvedValue();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          {
            path: 'platform/roles',
            canActivate: [permissionGuard],
            data: { permission: PERMISSIONS.roles.default },
            children: [],
          },
          { path: 'forbidden', children: [] },
          { path: 'workspace', children: [] },
        ]),
        {
          provide: AuthService,
          useValue: {
            initializeAuth,
            isAuthenticated: () => true,
            currentUser: signal(new User({ id: 'u1', username: 'alice', roles: [] })),
          },
        },
        // 启动流用真实实现，只桩掉它调用的会话上下文（有独立单测）
        { provide: SessionContextService, useValue: { establish: vi.fn(), clear: vi.fn() } },
        //#if (Impersonation)
        { provide: ImpersonationService, useValue: { load: vi.fn() } },
        //#endif
      ],
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
    await startUp();

    await expect(runGuard({})).resolves.toEqual(true);
  });

  it('allows access when the single permission is granted', async () => {
    await startUp();

    await expect(runGuard({ permission: PERMISSIONS.users.default })).resolves.toEqual(true);
  });

  it('treats permissions as any-of rather than all-of', async () => {
    await startUp();

    await expect(
      runGuard({ permissions: [PERMISSIONS.roles.default, PERMISSIONS.users.default] }),
    ).resolves.toEqual(true);
  });

  it('combines permission and permissions when both are declared', async () => {
    await startUp();

    await expect(
      runGuard({ permission: PERMISSIONS.users.default, permissions: [PERMISSIONS.roles.default] }),
    ).resolves.toEqual(true);
  });

  it('redirects to the forbidden page without changing the url when nothing is granted', async () => {
    await startUp();

    const result = await runGuard({ permission: PERMISSIONS.roles.default });

    expect(result instanceof RedirectCommand).toBe(true);
    const command = result as RedirectCommand;
    expect(router.serializeUrl(command.redirectTo)).toBe('/forbidden');
    expect(command.navigationBehaviorOptions?.browserUrl).toBe('/platform/roles');
  });

  // 真实导航：深链打开无权限页时显示 403 页，地址栏仍是用户打开的地址，刷新即重新判定
  it('shows the forbidden page in place of a deep link', async () => {
    await startUp();
    const location = TestBed.inject(Location);
    location.replaceState('/platform/roles?offset=20');

    router.initialNavigation();
    await vi.waitFor(() => expect(router.url).toBe('/forbidden'));

    expect(location.path()).toBe('/platform/roles?offset=20');
  });

  // 应用内从别的页面跳过来同样如此：地址栏是被拒的目标，不停在上一页
  it('shows the target url in the address bar after an in-app navigation is denied', async () => {
    await startUp();
    const location = TestBed.inject(Location);
    await router.navigateByUrl('/workspace');

    await router.navigateByUrl('/platform/roles?offset=20#list');

    expect(router.url).toBe('/forbidden');
    expect(location.path(true)).toBe('/platform/roles?offset=20#list');
  });

  it('does not inherit the looser declaration of the parent route', async () => {
    await startUp();
    // 复刻真实路由：父路由用 permissions 数组、子路由用单个 permission，合并后两键同时保留才能触发
    // 错误放行。当前用户只有 users 权限：读自身 data 时要求 roles 被拦下，误读合并后的 data 会放行。
    const result = await runGuard(
      { permission: PERMISSIONS.roles.default },
      { permissions: [PERMISSIONS.users.default] },
    );

    expect(result instanceof RedirectCommand).toBe(true);
  });

  it('waits for startup to finish instead of deciding during loading', async () => {
    let finishProbe!: () => void;
    initializeAuth.mockReturnValue(new Promise<void>((resolve) => (finishProbe = resolve)));
    const started = startUp();

    let settled = false;
    const pending = runGuard({ permission: PERMISSIONS.users.default }).then((value) => {
      settled = true;
      return value;
    });

    await Promise.resolve();
    TestBed.tick();
    expect(settled).toBe(false);

    // 权限只在启动流结束后可信；在此之前既不能放行受保护页面，也不能拒绝刷新。
    finishProbe();
    await started;
    await expect(pending).resolves.toEqual(true);
  });

  // 启动失败时权限是空的：按它下结论会把一次故障说成"无权限"
  it('blocks without redirecting to the forbidden page when startup failed', async () => {
    await startUp(
      ApplicationHttpError.from(new HttpErrorResponse({ status: 500, statusText: 'HTTP 500' })),
    );
    const cancels: NavigationCancel[] = [];
    router.events.subscribe((event) => event instanceof NavigationCancel && cancels.push(event));

    await expect(router.navigateByUrl('/platform/roles?offset=20')).resolves.toBe(false);

    expect(router.url).toBe('/');
    expect(cancels.map((event) => event.url)).toEqual(['/platform/roles?offset=20']);
    expect(TestBed.inject(EntryRouteService).url()).toBe('/platform/roles?offset=20');
  });
});
