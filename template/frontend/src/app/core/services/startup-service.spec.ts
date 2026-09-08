import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';

import { AuthService } from './auth-service';
import { SessionContextService } from './session-context-service';
import { StartupService } from './startup-service';
import { ApplicationHttpError } from '../errors/application-http-error';

describe('StartupService', () => {
  let authService: jasmine.SpyObj<AuthService>;
  // 会话上下文（当前用户、权限、设置）整个桩掉：它自己有独立单测，本组用例只关心
  // 状态机的分支。用真实实现的话，这里就要连它内部那些依赖一起打桩——启动状态机的
  // 测试没有理由知道那些，而漏掉一个就会向 Karma 发真实请求，
  // 靠 404 被降级逻辑吞掉后照样变绿。
  let sessionContext: jasmine.SpyObj<SessionContextService>;
  let service: StartupService;
  let isProtectedRoute: jasmine.Spy<() => boolean>;

  beforeEach(() => {
    // 认证数据的清理由会话上下文统一负责（它有独立单测），这里只需要认证探测本身。
    authService = jasmine.createSpyObj<AuthService>('AuthService', ['initializeAuth']);
    sessionContext = jasmine.createSpyObj<SessionContextService>('SessionContextService', [
      'establish',
      'clear',
    ]);
    sessionContext.establish.and.resolveTo();
    TestBed.configureTestingModule({
      providers: [
        StartupService,
        { provide: AuthService, useValue: authService },
        { provide: SessionContextService, useValue: sessionContext },
      ],
    });
    service = TestBed.inject(StartupService);
    // 固定为受保护路由，覆盖认证探测的全部状态转换分支；判据本身由末尾两条用例
    // 用真实实现验证（callThrough + replaceState）。
    isProtectedRoute = spyOn(
      service as unknown as { isProtectedRoute(): boolean },
      'isProtectedRoute',
    ).and.returnValue(true);
  });

  function httpError(status: number): ApplicationHttpError {
    return ApplicationHttpError.from(
      new HttpErrorResponse({ status, statusText: `HTTP ${status}` }),
    );
  }

  it('treats 401 as signed-out and still reaches success', async () => {
    authService.initializeAuth.and.rejectWith(httpError(401));

    await service.load();

    // 401 视为未登录：认证数据、权限、设置由统一清理入口一起清掉，
    // 否则上一位用户的裁剪结论和偏好会留给下一个人（SessionContextService 有独立单测覆盖三样）。
    expect(sessionContext.clear).toHaveBeenCalled();
    expect(service.status()).toBe('success');
  });

  it('marks startup as failed when the auth service is unavailable (503)', async () => {
    const error = httpError(503);
    authService.initializeAuth.and.rejectWith(error);

    await service.load();

    expect(service.status()).toBe('failed');
    expect(service.error()).toBe(error);
    // 非 401 故障不是「未登录」：不能清会话，否则一次接口抖动就把用户踢下线。
    expect(sessionContext.clear).not.toHaveBeenCalled();
  });

  it('marks startup as failed on network errors (status 0)', async () => {
    authService.initializeAuth.and.rejectWith(httpError(0));

    await service.load();

    expect(service.status()).toBe('failed');
  });

  it('reaches success when the session probe resolves', async () => {
    authService.initializeAuth.and.resolveTo();

    await service.load();

    // 权限与当前用户在同一次启动中就位，Guard 与菜单才不会闪现受保护入口。
    expect(sessionContext.establish).toHaveBeenCalled();
    expect(service.status()).toBe('success');
    expect(service.error()).toBeNull();
  });

  /**
   * 入口路由判据本身，用真实实现跑。
   *
   * 上面的用例把它打桩成 true 以隔离状态机，因此判据坏掉不会有任何用例变红：
   * 恒 false 时受保护深链会在无主体状态下启动，Guard 按无权限把已登录的人踢去登录页；
   * 恒 true 时公开页也要探一次认证。两种都得钉住。
   */
  describe('entry route gate', () => {
    afterEach(() => history.replaceState(null, '', '/context.html'));

    it('establishes the subject on a protected route', async () => {
      isProtectedRoute.and.callThrough();
      authService.initializeAuth.and.resolveTo();
      history.replaceState(null, '', '/platform/users');

      await service.load();

      expect(authService.initializeAuth).toHaveBeenCalled();
      expect(sessionContext.establish).toHaveBeenCalled();
    });

    it('does not probe the session on a public route', async () => {
      isProtectedRoute.and.callThrough();
      authService.initializeAuth.and.resolveTo();
      history.replaceState(null, '', '/');

      await service.load();

      expect(authService.initializeAuth).not.toHaveBeenCalled();
      expect(service.status()).toBe('success');
    });
  });
  //#if (!LocalIdentity)

  /**
   * OIDC 回调页上的 401。
   *
   * 这一刻刚从授权服务器换到令牌，401 是 API 拒了它，不是「未登录」。当作未登录判成功
   * 走下去，外壳就会渲染 router-outlet，回调组件跳进受保护路由，Guard 发现没有主体
   * 又发起一次授权；授权服务器那边会话还在，立刻带着新 code 回到回调页，同样被拒——
   * 一圈一圈在浏览器和 IdP 之间打转，而每一圈的结果都一样。
   */
  describe('on the OIDC callback', () => {
    afterEach(() => history.replaceState(null, '', '/context.html'));

    it('fails startup instead of letting the callback navigate on', async () => {
      isProtectedRoute.and.callThrough();
      history.replaceState(null, '', '/auth/callback?code=abc&state=xyz');
      authService.initializeAuth.and.resolveTo();
      sessionContext.establish.and.rejectWith(httpError(401));

      await service.load();

      expect(service.status()).toBe('failed');
      expect(service.error()).not.toBeNull();
    });

    // 回调判据必须落在路径上。查询串里出现 `/auth/callback` 的人并不在回调页，
    // 误判的代价是普通会话过期被当成"刚换到的令牌被 API 拒了"，直接进故障页，
    // 而正确行为是按已登出处理、让 Guard 把人送去登录。
    it('does not mistake an auth path inside the query string for the callback', async () => {
      isProtectedRoute.and.callThrough();
      history.replaceState(null, '', '/#/workspace?returnUrl=/auth/callback');
      authService.initializeAuth.and.resolveTo();
      sessionContext.establish.and.rejectWith(httpError(401));

      await service.load();

      expect(service.status()).toBe('success');
      expect(sessionContext.clear).toHaveBeenCalled();
    });

    it('still treats a 401 outside the callback as signed out', async () => {
      isProtectedRoute.and.callThrough();
      history.replaceState(null, '', '/platform/users');
      authService.initializeAuth.and.resolveTo();
      sessionContext.establish.and.rejectWith(httpError(401));

      await service.load();

      // 会话过期是常态，不能因为回调页那条特例把普通页面也变成故障页。
      expect(service.status()).toBe('success');
      expect(sessionContext.clear).toHaveBeenCalled();
    });
  });
  //#endif
});
