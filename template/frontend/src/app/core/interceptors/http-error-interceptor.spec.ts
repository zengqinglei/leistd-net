import {
  HttpContext,
  HttpErrorResponse,
  HttpEvent,
  HttpHandlerFn,
  HttpHeaders,
  HttpRequest,
} from '@angular/common/http';
import { Injector, provideZonelessChangeDetection, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { Observable, throwError } from 'rxjs';

import { SILENT_AUTH } from './http-context-tokens';
import { httpErrorInterceptor } from './http-error-interceptor';
import { ApplicationHttpError } from '../errors/application-http-error';
import { entryRouteUrl } from '../routing/entry-route';
import { AuthService } from '../services/auth-service';
import { SessionContextService } from '../services/session-context-service';
import { TenantContextService } from '../services/tenant-context-service';

/**
 * 直接以 runInInjectionContext 驱动拦截器：next 用 throwError 同步发射错误，
 * catchError 同步映射，因此错误在订阅时同步落到 error 回调（zoneless 无需 fakeAsync）。
 */
describe('httpErrorInterceptor', () => {
  let injector: Injector;
  let router: Router;
  //#if (LocalIdentity)
  let navigate: jasmine.Spy;
  //#endif
  let sessionContext: jasmine.SpyObj<SessionContextService>;
  let authService: jasmine.SpyObj<AuthService>;

  beforeEach(() => {
    // 拦截器的契约就是「调统一清理入口」，真实实例只会连带拉起整条会话依赖链。
    sessionContext = jasmine.createSpyObj<SessionContextService>('SessionContextService', [
      'clear',
    ]);
    //#if (LocalIdentity)
    authService = jasmine.createSpyObj<AuthService>('AuthService', ['isAuthenticated']);
    //#else
    authService = jasmine.createSpyObj<AuthService>('AuthService', ['isAuthenticated', 'login']);
    //#endif
    authService.isAuthenticated.and.returnValue(true);
    // 清理是同步的，清完就没有主体了——并发 401 的收敛全靠这一点，替身必须照实模拟。
    sessionContext.clear.and.callFake(() => authService.isAuthenticated.and.returnValue(false));
    TestBed.configureTestingModule({
      // prettier-ignore
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: SessionContextService, useValue: sessionContext },
        { provide: AuthService, useValue: authService },
      ],
    });
    injector = TestBed.inject(Injector);
    router = TestBed.inject(Router);
    //#if (LocalIdentity)
    navigate = spyOn(router, 'navigate');
    //#endif
  });

  function runInterceptor(error: unknown, options?: { context?: HttpContext }): unknown {
    const req = new HttpRequest('GET', '/api/test', { context: options?.context });
    const next: HttpHandlerFn = () => throwError(() => error) as Observable<HttpEvent<unknown>>;

    let caught: unknown;
    runInInjectionContext(injector, () => httpErrorInterceptor(req, next)).subscribe({
      error: (value: unknown) => (caught = value),
    });
    return caught;
  }

  function httpError(
    status: number,
    error: unknown = null,
    headers?: Record<string, string>,
  ): HttpErrorResponse {
    return new HttpErrorResponse({
      status,
      statusText: `status ${status}`,
      url: '/api/test',
      error,
      headers: headers ? new HttpHeaders(headers) : undefined,
    });
  }

  for (const status of [403, 404, 409, 422, 500]) {
    it(`normalizes a ${status} response to an ApplicationHttpError`, () => {
      const caught = runInterceptor(httpError(status));
      expect(caught).toBeInstanceOf(ApplicationHttpError);
      expect((caught as ApplicationHttpError).status).toBe(status);
    });
  }

  it('normalizes a network failure (status 0) to an ApplicationHttpError', () => {
    const caught = runInterceptor(httpError(0, new ProgressEvent('error')));
    expect(caught).toBeInstanceOf(ApplicationHttpError);
    expect((caught as ApplicationHttpError).status).toBe(0);
  });

  it('parses an RFC 9457 errors array (code + details)', () => {
    const caught = runInterceptor(
      httpError(422, { errors: [{ code: 'name_required', detail: 'Name is required.' }] }),
    );
    const applicationError = caught as ApplicationHttpError;
    expect(applicationError).toBeInstanceOf(ApplicationHttpError);
    expect(applicationError.code).toBe('name_required');
    expect(applicationError.message).toBe('Name is required.');
    expect(applicationError.details.length).toBe(1);
  });

  it('lets a non-HTTP error pass through unchanged toward the GlobalErrorHandler', () => {
    const original = new Error('boom');
    const caught = runInterceptor(original);
    expect(caught).toBe(original);
    expect(caught instanceof ApplicationHttpError).toBe(false);
  });

  /**
   * 「重新发起认证」在两种身份形态下是不同的动作：本地身份跳自带登录页，
   * OIDC 形态重新走授权。断言收在这里，用例主体两种形态共用一份。
   */
  function expectReauthentication(returnUrl: string): void {
    //#if (LocalIdentity)
    expect(navigate).toHaveBeenCalledWith(['/auth/login'], { queryParams: { returnUrl } });
    //#else
    expect(authService.login).toHaveBeenCalledWith(returnUrl);
    //#endif
  }

  /** 恢复只能发生一次，且带着最初那个落地地址。 */
  function expectSingleReauthentication(returnUrl: string): void {
    //#if (LocalIdentity)
    expect(navigate.calls.allArgs()).toEqual([[['/auth/login'], { queryParams: { returnUrl } }]]);
    //#else
    expect(authService.login.calls.allArgs()).toEqual([[returnUrl]]);
    //#endif
  }

  function expectNoReauthentication(): void {
    //#if (LocalIdentity)
    expect(navigate).not.toHaveBeenCalled();
    //#else
    expect(authService.login).not.toHaveBeenCalled();
    //#endif
  }

  // 断言的是统一清理入口，而不是 AuthService.clearAuthData()：只断言后者的话，
  // 把生产代码退回"只清认证数据"会继续通过，而权限和设置会留给下一个登录的人。
  //
  // 两种形态都要走这一条。OIDC 形态没有静默续期也没有刷新令牌，isAuthenticated()
  // 只看内存主体，不会自己变假——少了这里的清理与重新认证，令牌到期后 Guard 继续放行、
  // 旧权限旧设置继续显示、请求全部 401，而这是那种部署形态的常规生命周期。
  it('clears the whole session and re-initiates authentication on a non-silent 401', () => {
    const caught = runInterceptor(httpError(401));

    expect(sessionContext.clear).toHaveBeenCalled();
    expectReauthentication(entryRouteUrl());
    expect(caught).toBeInstanceOf(ApplicationHttpError);
    expect((caught as ApplicationHttpError).status).toBe(401);
  });

  // 直接打开深链时，启动流跑在初始导航之前——那时 Router.url 是 '/'，
  // 用它记落地地址会把用户重新登录后送去首页，而不是他点开的那一页。
  it('records the deep link that has not been navigated to yet', () => {
    history.replaceState(null, '', '/platform/users?page=2');

    try {
      runInterceptor(httpError(401));

      expect(router.url).toBe('/');
      expectSingleReauthentication('/platform/users?page=2');
    } finally {
      history.replaceState(null, '', '/context.html');
    }
  });

  // 认证路由上不能再发起一次认证：OIDC 形态下那是死循环——回调页上再授权一次，
  // IdP 侧已有会话，立刻带着新 code 跳回来，而 401 的原因一点没变。
  // 只把落地地址丢掉是不够的，authorize() 本身就不能再发生。
  it('leaves the 401 entirely to the running auth flow while on an auth route', () => {
    history.replaceState(null, '', '/auth/callback?code=abc');

    try {
      const caught = runInterceptor(httpError(401));

      // 会话清理都不能做：回调这一刻主体刚建立，清掉之后启动流照常判成功、
      // 回调页照常跳进受保护路由，Guard 发现没有主体又发起一次授权——
      // callback → 清主体 → workspace → authorize → callback，循环只是多绕一跳。
      expect(sessionContext.clear).not.toHaveBeenCalled();
      expectNoReauthentication();
      // 归一化照做：调用方（这里是启动流）要靠它判断状态码。
      expect(caught).toBeInstanceOf(ApplicationHttpError);
      expect((caught as ApplicationHttpError).status).toBe(401);
    } finally {
      history.replaceState(null, '', '/context.html');
    }
  });

  // 令牌到期时一屏的并发请求会一起 401，这是常规场景。第一条恢复之后，
  // 后到的那些不能把最初的落地地址覆盖成认证页自己或默认页。
  it('recovers only once when a batch of 401s arrives', () => {
    history.replaceState(null, '', '/platform/users');

    try {
      runInterceptor(httpError(401));
      runInterceptor(httpError(401));
      runInterceptor(httpError(401));

      // 恢复跑两遍不是"多导航一次"这么轻：OIDC 客户端的 authorize() 是异步的，
      // 每条流程都会重新生成并覆盖 PKCE codeVerifier，两条交叉后回调换 token 会失败。
      // 收敛靠的是第一条已经同步清掉主体，后面的到闸门处就没有主体可清了。
      expectSingleReauthentication('/platform/users');
      expect(sessionContext.clear).toHaveBeenCalledTimes(1);
    } finally {
      history.replaceState(null, '', '/context.html');
    }
  });

  // 本来就没有主体时，这里没有东西要清，重新认证也该由 Guard 或启动流按自己的时机发起。
  it('does nothing but normalize when there is no subject to drop', () => {
    authService.isAuthenticated.and.returnValue(false);

    const caught = runInterceptor(httpError(401));

    expect(sessionContext.clear).not.toHaveBeenCalled();
    expectNoReauthentication();
    expect(caught).toBeInstanceOf(ApplicationHttpError);
  });

  // 「租户没了」与「要不要重新认证」是两件正交的事。这条把它们钉开：认证路由上、
  // 静默请求、外加这个头——租户必须清，而会话与认证一动都不能动。
  // 登录页的启动探测正是最容易撞上它的地方（旧 Cookie 带着已停用租户的 tenant_id），
  // 不清的话后续登录请求继续携带失效的 X-Tenant-Id，用户一直登不进来。
  it('clears an invalid tenant even on an auth route, without touching the session', () => {
    history.replaceState(null, '', '/auth/login');
    const tenantContext = TestBed.inject(TenantContextService);
    const clearTenantSpy = spyOn(tenantContext, 'clear');
    const context = new HttpContext().set(SILENT_AUTH, true);

    try {
      runInterceptor(httpError(401, null, { 'X-Tenant-Invalid': '1' }), { context });

      expect(clearTenantSpy).toHaveBeenCalled();
      expect(sessionContext.clear).not.toHaveBeenCalled();
      expectNoReauthentication();
    } finally {
      history.replaceState(null, '', '/context.html');
    }
  });

  it('clears the selected tenant on a 401 marked X-Tenant-Invalid', () => {
    const tenantContext = TestBed.inject(TenantContextService);
    const clearTenantSpy = spyOn(tenantContext, 'clear');

    runInterceptor(httpError(401, null, { 'X-Tenant-Invalid': '1' }));

    // 不清的话，重新认证后会带着这个已失效的租户再次被拒——用户换个地方卡住
    expect(clearTenantSpy).toHaveBeenCalled();
  });

  it('does not treat an ordinary 401 as an invalid tenant', () => {
    const tenantContext = TestBed.inject(TenantContextService);
    const clearTenantSpy = spyOn(tenantContext, 'clear');

    runInterceptor(httpError(401));

    // 拦截器只按 X-Tenant-Invalid 处置租户。普通 401 的租户去向不在这里，而在
    // AuthService.clearAuthData()（经 sessionContext.clear() 到达，此处是替身）：
    // 本地身份保留登录入口的选择，免得每次超时都要重选；OIDC 形态的租户来自令牌声明，
    // 跟着主体一起清。所以这条断言说的是「拦截器没越过那层去清」，不是「租户一定还在」。
    expect(clearTenantSpy).not.toHaveBeenCalled();
  });

  it('clears the tenant on a silent 401 marked X-Tenant-Invalid, without touching auth or routing', () => {
    const tenantContext = TestBed.inject(TenantContextService);
    const clearTenantSpy = spyOn(tenantContext, 'clear');
    const context = new HttpContext().set(SILENT_AUTH, true);

    runInterceptor(httpError(401, null, { 'X-Tenant-Invalid': '1' }), { context });

    // 静默只表达"别为后台请求打断用户"，与"这个租户已经没了"是两件事：
    // /auth/me 与 /permissions/current 同样会撞上失效租户，不清就留到重新认证后再次被拒
    expect(clearTenantSpy).toHaveBeenCalled();
    expect(sessionContext.clear).not.toHaveBeenCalled();
    expectNoReauthentication();
  });

  // 启动阶段的 /auth/me 与 /permissions/current 都是静默的：这里若打断，
  // 启动流自己的 401 处置就会和拦截器抢方向盘。
  it('honors SILENT_AUTH: skips the 401 recovery but still normalizes the error', () => {
    const context = new HttpContext().set(SILENT_AUTH, true);

    const caught = runInterceptor(httpError(401), { context });

    expect(sessionContext.clear).not.toHaveBeenCalled();
    expectNoReauthentication();
    expect(caught).toBeInstanceOf(ApplicationHttpError);
  });
});
