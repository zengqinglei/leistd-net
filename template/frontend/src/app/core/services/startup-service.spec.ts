import { Location } from '@angular/common';
import { HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
//#if (TenantWorkspace)
import { Component, signal } from '@angular/core';
//#endif
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
//#if (TenantWorkspace)
import { RouterTestingHarness } from '@angular/router/testing';
//#endif
// prettier-ignore
import {
  //#if (IncludeNotifications || IncludeRealTime)
  EMPTY,
  //#endif
  firstValueFrom,
} from 'rxjs';

import { ApplicationHttpError } from '../errors/application-http-error';
//#if (TenantWorkspace)
import { workspaceDeniedGuard, workspaceGuard } from '../guards/workspace-guard';
//#endif
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../i18n/transloco.testing';
//#endif
import { httpErrorInterceptor } from '../interceptors/http-error-interceptor';
//#if (TenantWorkspace)
import { workspaceInterceptor } from '../interceptors/workspace-interceptor';
//#endif
//#if (RemoteTokenAuth)
import { MOCKED_URL } from '../mock/mocked-url';
//#endif
import { EntryRouteService } from '../routing/entry-route-service';
//#if (RemoteTokenAuth)
import { FullPageNavigationService } from '../routing/full-page-navigation-service';
//#endif
import { AuthService } from './auth-service';
import { SessionContextService } from './session-context-service';
//#if (IncludeNotifications || IncludeRealTime)
import { SignalRService } from './signalr-service';
//#endif
import { StartupService } from './startup-service';
//#if (TenantWorkspace)
import { WorkspaceContextService } from './workspace-context-service';
//#endif

import type { MockedObject } from 'vitest';

describe('StartupService', () => {
  let authService: Pick<MockedObject<AuthService>, 'initializeAuth'>;
  // 会话上下文整个桩掉（它有独立单测），本组只关心状态机分支；用真实实现要连带打桩其依赖，
  // 漏掉的真实请求还会被降级逻辑吞掉而照样变绿。
  let sessionContext: Pick<MockedObject<SessionContextService>, 'establish' | 'clear'>;
  let service: StartupService;

  beforeEach(() => {
    // 认证数据的清理由会话上下文统一负责（它有独立单测），这里只需要认证探测本身。
    authService = {
      initializeAuth: vi.fn().mockName('AuthService.initializeAuth'),
    };
    sessionContext = {
      establish: vi.fn().mockName('SessionContextService.establish'),
      clear: vi.fn().mockName('SessionContextService.clear'),
    };
    sessionContext.establish.mockResolvedValue();
    TestBed.configureTestingModule({
      providers: [
        StartupService,
        provideRouter([{ path: 'platform/users', children: [] }]),
        { provide: AuthService, useValue: authService },
        { provide: SessionContextService, useValue: sessionContext },
        //#if (TenantWorkspace)
        {
          provide: WorkspaceContextService,
          useValue: {
            target: signal<string | null>(null),
            nameFromUrl: () => null,
            loadForStartup: (context: Promise<void>) => context,
          },
        },
        //#endif
      ],
    });
    service = TestBed.inject(StartupService);
    openAt('/platform/users');
  });

  /** 设定地址栏但不导航：启动流跑在初始导航之前，读到的只能是地址栏。 */
  function openAt(url: string): void {
    TestBed.inject(Location).replaceState(url);
  }

  function httpError(status: number): ApplicationHttpError {
    return ApplicationHttpError.from(
      new HttpErrorResponse({ status, statusText: `HTTP ${status}` }),
    );
  }

  it('treats 401 as signed-out and still reaches success', async () => {
    authService.initializeAuth.mockRejectedValue(httpError(401));

    await service.load();

    // 401 视为未登录：认证数据、权限、设置由统一清理入口一起清掉，
    // 否则上一位用户的裁剪结论和偏好会留给下一个人（SessionContextService 有独立单测覆盖三样）。
    expect(sessionContext.clear).toHaveBeenCalled();
    expect(service.status()).toBe('success');
  });

  it('marks startup as failed when the auth service is unavailable (503)', async () => {
    const error = httpError(503);
    authService.initializeAuth.mockRejectedValue(error);

    await service.load();

    expect(service.status()).toBe('failed');
    expect(service.error()).toBe(error);
    // 非 401 故障不是「未登录」：不能清会话，否则一次接口抖动就把用户踢下线。
    expect(sessionContext.clear).not.toHaveBeenCalled();
  });

  it('marks startup as failed on network errors (status 0)', async () => {
    authService.initializeAuth.mockRejectedValue(httpError(0));

    await service.load();

    expect(service.status()).toBe('failed');
  });

  it('reaches success when the session probe resolves', async () => {
    authService.initializeAuth.mockResolvedValue();

    await service.load();

    // 权限与当前用户在同一次启动中就位，Guard 与菜单才不会闪现受保护入口。
    expect(sessionContext.establish).toHaveBeenCalled();
    expect(service.status()).toBe('success');
    expect(service.error()).toBeNull();
  });

  /** 守卫的共同前置：启动失败既不是未登录也不是无权限，导航被扣下，等重试。 */
  describe('settled', () => {
    it('waits while loading and lets the guard decide after success', async () => {
      authService.initializeAuth.mockReturnValue(new Promise(() => undefined));
      void service.load();

      let decided: boolean | undefined;
      const subscription = service.settled('/platform/users').subscribe((v) => (decided = v));
      await Promise.resolve();
      TestBed.tick();
      expect(decided).toBeUndefined();
      subscription.unsubscribe();

      authService.initializeAuth.mockResolvedValue();
      await service.load();

      await expect(firstValueFrom(service.settled('/platform/users'))).resolves.toBe(true);
      expect(TestBed.inject(EntryRouteService).takeHeld()).toBeNull();
    });

    it('holds the target url and rejects the navigation when startup failed', async () => {
      authService.initializeAuth.mockRejectedValue(httpError(403));
      await service.load();

      await expect(firstValueFrom(service.settled('/platform/users?offset=20#list'))).resolves.toBe(
        false,
      );
      expect(TestBed.inject(EntryRouteService).url()).toBe('/platform/users?offset=20#list');
    });
  });

  describe('retry', () => {
    it('lands on the held target once startup succeeds, without reading the address bar', async () => {
      openAt('/platform/users?offset=20');
      authService.initializeAuth.mockRejectedValue(httpError(503));
      await service.load();
      await firstValueFrom(service.settled('/platform/users?offset=20#list'));
      // 守卫取消首次导航后 Router 会把地址栏还原成 /
      openAt('/');

      authService.initializeAuth.mockResolvedValue();
      await service.retry();

      // 入口按扣下的目标判定，仍是受保护路由：探测了会话，而不是按公开页直接放行
      expect(authService.initializeAuth).toHaveBeenCalledTimes(2);
      expect(service.status()).toBe('success');
      const router = TestBed.inject(Router);
      expect(router.url).toBe('/platform/users?offset=20#list');
      expect(TestBed.inject(Location).path(true)).toBe('/platform/users?offset=20#list');
      expect(TestBed.inject(EntryRouteService).takeHeld()).toBeNull();
    });

    it('keeps the held target while startup still fails', async () => {
      authService.initializeAuth.mockRejectedValue(httpError(503));
      await service.load();
      await firstValueFrom(service.settled('/platform/users'));

      await service.retry();

      expect(service.status()).toBe('failed');
      expect(TestBed.inject(Router).navigated).toBe(false);
      expect(TestBed.inject(EntryRouteService).url()).toBe('/platform/users');
    });
  });

  describe('entry route gate', () => {
    it('establishes the subject on a protected route', async () => {
      authService.initializeAuth.mockResolvedValue();
      openAt('/platform/users');

      await service.load();

      expect(authService.initializeAuth).toHaveBeenCalled();
      expect(sessionContext.establish).toHaveBeenCalled();
    });

    it('does not probe the session on a public route', async () => {
      authService.initializeAuth.mockResolvedValue();
      openAt('/');

      await service.load();

      expect(authService.initializeAuth).not.toHaveBeenCalled();
      expect(service.status()).toBe('success');
    });
  });
  //#if (ExternalLogin)

  /**
   * 外部登录回调页：提供商带着 code 跳回来，这一刻还没有会话，回调组件自己去换。
   * 启动流程若没认出它，可能会先探一次会话，
   * 把回调当普通页面处理，外部登录断在这一步且不报错。
   */
  it('lets the external sign-in callback run without probing the session', async () => {
    openAt('/auth/external-callback/github?code=abc&state=xyz');

    await service.load();

    expect(authService.initializeAuth).not.toHaveBeenCalled();
    expect(service.status()).toBe('success');
  });
  //#endif
  //#if (RemoteTokenAuth)

  /**
   * OIDC 回调页上的 401 是 API 拒了刚换到的令牌，不是未登录；按未登录处理会在浏览器与 IdP
   * 之间循环授权。
   */
  describe('on the OIDC callback', () => {
    // 回调判据落在路径上：查询串里含 `/auth/callback` 的普通页面会话过期时应按已登出处理。
    it('does not mistake an auth path inside the query string for the callback', async () => {
      openAt('/workspace?returnUrl=/auth/callback');
      authService.initializeAuth.mockResolvedValue();
      sessionContext.establish.mockRejectedValue(httpError(401));

      await service.load();

      expect(service.status()).toBe('success');
      expect(sessionContext.clear).toHaveBeenCalled();
    });

    it('still treats a 401 outside the callback as signed out', async () => {
      openAt('/platform/users');
      authService.initializeAuth.mockResolvedValue();
      sessionContext.establish.mockRejectedValue(httpError(401));

      await service.load();

      // 会话过期是常态，不能因为回调页那条特例把普通页面也变成故障页。
      expect(service.status()).toBe('success');
      expect(sessionContext.clear).toHaveBeenCalled();
    });
  });
  //#endif
});
//#if (TenantWorkspace)

@Component({ template: '<p>Workspace content</p>' })
class WorkspaceContent {}
//#endif

describe('StartupService HTTP requests', () => {
  let http: HttpTestingController;
  let startup: StartupService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        // prettier-ignore
        provideRouter([
          { path: 'auth/login', children: [] },
          //#if (TenantWorkspace)
          { path: 'platform', component: WorkspaceContent, canActivate: [workspaceGuard] },
          {
            path: 'w/:workspace/platform',
            component: WorkspaceContent,
            canActivate: [workspaceGuard],
          },
          {
            path: 'workspace-denied',
            component: WorkspaceContent,
            canActivate: [workspaceDeniedGuard],
          },
          { path: 'account', component: WorkspaceContent },
          //#endif
        ]),
        // prettier-ignore
        provideHttpClient(
          withInterceptors([
            //#if (TenantWorkspace)
            workspaceInterceptor,
            //#endif
            httpErrorInterceptor,
          ]),
        ),
        provideHttpClientTesting(),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(),
        //#endif
        //#if (RemoteTokenAuth)
        { provide: MOCKED_URL, useValue: () => false },
        { provide: FullPageNavigationService, useValue: { replace: vi.fn() } },
        //#endif
        //#if (IncludeNotifications || IncludeRealTime)
        {
          provide: SignalRService,
          useValue: { sessionRejected$: EMPTY, reset: vi.fn(), setWorkspace: vi.fn() },
        },
        //#endif
      ],
    });
    TestBed.inject(Location).replaceState('/platform');
    http = TestBed.inject(HttpTestingController);
    startup = TestBed.inject(StartupService);
  });

  afterEach(() => http.verify());

  async function start(tenantId: string | null = null) {
    const done = startup.load();
    http.expectOne('/api/v1/auth/me').flush({ id: 'signed-in', username: 'admin', tenantId });
    await Promise.resolve();
    return { done };
  }

  it('keeps permissions and settings in flight together and waits for settings on permission failure', async () => {
    const { done } = await start();
    //#if (TenantWorkspace)
    http.expectOne(directory).flush(workspaces);
    //#endif
    const permissions = http.expectOne('/api/v1/permissions/current');
    const settings = http.expectOne('/api/v1/settings');

    permissions.flush(null, { status: 503, statusText: 'Unavailable' });
    await new Promise<void>((resolve) => setTimeout(resolve));
    expect(startup.status()).toBe('loading');
    settings.flush([]);

    await done;
    expect(startup.status()).toBe('failed');
    expect(startup.error()).toMatchObject({ status: 503 });
  });

  it('shows the permission outage when settings return 401 first without signing out', async () => {
    const { done } = await start();
    //#if (TenantWorkspace)
    http.expectOne(directory).flush(workspaces);
    //#endif
    http.expectOne('/api/v1/settings').flush(null, { status: 401, statusText: 'Unauthorized' });
    http
      .expectOne('/api/v1/permissions/current')
      .flush(null, { status: 503, statusText: 'Unavailable' });

    await done;
    expect(startup.status()).toBe('failed');
    expect(startup.error()).toMatchObject({ status: 503 });
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(true);
    expect(TestBed.inject(Location).path()).toBe('/platform');
  });
  //#if (TenantWorkspace)

  const directory = '/api/v1/auth/me/workspaces';
  const permissions = '/api/v1/permissions/current';
  const settings = '/api/v1/settings';
  const workspaces = [{ name: 'acme', tenantId: 't1', userId: 'acme-member' }];
  const permissionSnapshot = { permissions: [], isSuperAdmin: false, versionToken: 'v1' };

  for (const [path, tenantId, scope] of [
    ['/w/ACME/platform', null, 'ACME'],
    ['/w/acme/platform', 't1', 'acme'],
    ['/platform', null, null],
  ] as const) {
    it(`reuses the first request batch when ${tenantId ? 'a tenant account' : 'a host account'} enters ${path}`, async () => {
      TestBed.inject(Location).replaceState(path);
      const { done } = await start(tenantId);
      const batch = http.match((request) =>
        [directory, permissions, settings].includes(request.url),
      );
      const requestKeys = batch.map(({ request }) => [
        request.url,
        request.headers.get('X-Workspace'),
      ]);
      const expected = [
        [directory, null],
        [permissions, scope],
        [settings, scope],
      ];
      if (scope && !tenantId) expected.push([permissions, null]);
      expect(requestKeys).toHaveLength(expected.length);
      expect(requestKeys).toEqual(expect.arrayContaining(expected));
      for (const pending of batch) {
        const response =
          pending.request.url === directory
            ? workspaces
            : pending.request.url === permissions
              ? permissionSnapshot
              : [];
        pending.flush(response);
      }
      await done;

      const harness = await RouterTestingHarness.create();
      await harness.navigateByUrl(path, WorkspaceContent);
      const workspace = TestBed.inject(WorkspaceContextService);
      expect(workspace.current()).toBe(scope ? 'acme' : null);
      expect(workspace.actorId()).toBe(scope ? 'acme-member' : 'signed-in');
      //#if (IncludeNotifications || IncludeRealTime)
      if (scope) {
        expect(TestBed.inject(SignalRService).setWorkspace).toHaveBeenCalledWith('acme', !tenantId);
      }
      //#endif
      if (path === '/platform') {
        await harness.navigateByUrl('/account', WorkspaceContent);
        await harness.navigateByUrl('/platform', WorkspaceContent);
      }
      http.expectNone(directory);
    });
  }

  it('lets the first workspace guard see the directory error and retries it on the next navigation', async () => {
    const { done } = await start();
    http.expectOne(directory).flush(null, { status: 503, statusText: 'Unavailable' });
    http.expectOne(permissions).flush(permissionSnapshot);
    http.expectOne(settings).flush([]);
    await done;

    expect(startup.status()).toBe('success');
    const harness = await RouterTestingHarness.create();
    await expect(harness.navigateByUrl('/platform')).rejects.toMatchObject({ status: 503 });
    http.expectNone(directory);
    const retry = harness.navigateByUrl('/platform', WorkspaceContent);
    await vi.waitFor(() => http.expectOne(directory).flush(workspaces), { interval: 1 });
    await retry;
  });

  for (const [code, reason] of [
    ['Workspace:Forbidden', 'forbidden'],
    ['Workspace:Unavailable', 'unavailable'],
  ]) {
    it(`shows the cold workspace denial without another permission request when ${reason}`, async () => {
      TestBed.inject(Location).replaceState('/w/other/platform');
      const { done } = await start('t1');
      http.expectOne(directory).flush(workspaces);
      for (const path of [permissions, settings]) {
        http.expectOne(path).flush({ code }, { status: 403, statusText: 'Forbidden' });
      }
      await done;
      expect(startup.status()).toBe('success');

      const harness = await RouterTestingHarness.create();
      await harness.navigateByUrl('/w/other/platform', WorkspaceContent);
      expect(TestBed.inject(Router).url).toContain(`reason=${reason}`);
      expect(TestBed.inject(Location).path()).toBe('/w/other/platform');
      http.expectNone(permissions);
    });
  }

  it('reloads with the workspace header when a tenant account enters from the host route', async () => {
    const { done } = await start('t1');
    http.expectOne(directory).flush(workspaces);
    for (const path of [permissions, settings]) {
      const request = http.expectOne(path);
      expect(request.request.headers.has('X-Workspace')).toBe(false);
      request.flush(path === permissions ? permissionSnapshot : []);
    }
    await done;

    const harness = await RouterTestingHarness.create();
    const entered = harness.navigateByUrl('/platform', WorkspaceContent);
    await vi.waitFor(
      () => {
        const pending = http.match(
          (request) => request.url === permissions || request.url === settings,
        );
        expect(pending).toHaveLength(2);
        for (const request of pending) {
          expect(request.request.headers.get('X-Workspace')).toBe('acme');
          request.flush(request.request.url === permissions ? permissionSnapshot : []);
        }
      },
      { interval: 1 },
    );
    await entered;
    expect(TestBed.inject(Router).url).toBe('/w/acme/platform');
    http.expectNone(directory);
  });
  //#endif
});
