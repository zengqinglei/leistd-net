//#if (LocalIdentity)
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AuthService } from './auth-service';
import { SILENT_AUTH } from '../interceptors/http-context-tokens';

describe('AuthService', () => {
  let service: AuthService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [AuthService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('marks the session probe as silent so the interceptor skips the 401 redirect', async () => {
    const initialization = service.initializeAuth();
    const request = httpTesting.expectOne('/api/v1/auth/me');

    expect(request.request.context.get(SILENT_AUTH)).toBe(true);
    request.flush(null, { status: 401, statusText: 'Unauthorized' });

    await expect(initialization).rejects.toThrow();
  });

  it('propagates startup probe failures so callers can distinguish outages from 401', async () => {
    const initialization = service.initializeAuth();
    httpTesting
      .expectOne('/api/v1/auth/me')
      .flush({ detail: 'Gateway unavailable' }, { status: 503, statusText: 'Unavailable' });

    await expect(initialization).rejects.toThrow();
    expect(service.isAuthenticated()).toBe(false);
  });
});
//#endif
//#if (RemoteTokenAuth)
import { TestBed } from '@angular/core/testing';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { of } from 'rxjs';

import { AuthService } from './auth-service';
import { TenantContextService } from './tenant-context-service';

import type { MockedObject } from 'vitest';

describe('Resource AuthService', () => {
  let oidc: Pick<
    MockedObject<OidcSecurityService>,
    'checkAuth' | 'getPayloadFromAccessToken' | 'authorize' | 'logoff'
  >;
  let service: AuthService;
  let tenantContext: TenantContextService;

  beforeEach(() => {
    oidc = {
      checkAuth: vi.fn().mockName('OidcSecurityService.checkAuth'),
      getPayloadFromAccessToken: vi.fn().mockName('OidcSecurityService.getPayloadFromAccessToken'),
      authorize: vi.fn().mockName('OidcSecurityService.authorize'),
      logoff: vi.fn().mockName('OidcSecurityService.logoff'),
    };
    oidc.logoff.mockReturnValue(of(undefined));
    TestBed.configureTestingModule({
      providers: [
        AuthService,
        TenantContextService,
        { provide: OidcSecurityService, useValue: oidc },
      ],
    });
    service = TestBed.inject(AuthService);
    tenantContext = TestBed.inject(TenantContextService);
  });

  /**
   * 模拟 OIDC 客户端已校验通过的会话。令牌本身刻意不是 JWT：声明只能经客户端的
   * getPayloadFromAccessToken 取得，不该由本服务再去解码令牌字符串。
   */
  function signedIn(payload: Record<string, unknown>): void {
    oidc.checkAuth.mockReturnValue(
      of({
        isAuthenticated: true,
        accessToken: 'validated-access-token',
        idToken: 'validated-id-token',
        userData: {},
      }),
    );
    oidc.getPayloadFromAccessToken.mockReturnValue(of(payload));
  }

  // 认证阶段与导航阶段的边界。initializeAuth() 一度自己消费 returnUrl 并导航，而那时
  // 启动流还停在 loading、permissionGuard 正等它离开 loading——returnUrl 指向 /platform
  // 时形成死等。所以认证只确立主体，落地地址留给回调组件取。
  it('establishes the subject without consuming the return url', async () => {
    sessionStorage.setItem('app.auth.returnUrl', '/platform/users');
    signedIn({ sub: crypto.randomUUID(), tenant_id: crypto.randomUUID() });

    try {
      await service.initializeAuth();

      expect(service.isAuthenticated()).toBe(true);
      // 落地地址还在：导航不在这一步做
      expect(sessionStorage.getItem('app.auth.returnUrl')).toBe('/platform/users');
    } finally {
      sessionStorage.removeItem('app.auth.returnUrl');
    }
  });

  // 取过即清：回调页刷新或二次进入，不该再往上一次的落地地址跳一遍。
  it('hands the return url over exactly once', () => {
    sessionStorage.setItem('app.auth.returnUrl', '/platform/users');

    try {
      expect(service.takeReturnUrl()).toBe('/platform/users');
      expect(sessionStorage.getItem('app.auth.returnUrl')).toBeNull();
      expect(service.takeReturnUrl()).toBe('/workspace');
    } finally {
      sessionStorage.removeItem('app.auth.returnUrl');
    }
  });

  it('establishes tenant context only from the validated access token', async () => {
    const tenantId = '019ff8ed-221b-7673-9ba8-6b6dd5a638ab';
    signedIn({ sub: crypto.randomUUID(), tenant_id: tenantId, email: 'user@test.dev' });

    await service.initializeAuth();

    expect(service.isAuthenticated()).toBe(true);
    expect(tenantContext.current()?.key).toBe(tenantId);
  });

  it('rejects an authenticated token with multiple tenant claims', async () => {
    signedIn({ sub: crypto.randomUUID(), tenant_id: [crypto.randomUUID(), crypto.randomUUID()] });

    await expect(service.initializeAuth()).rejects.toThrow();
    expect(tenantContext.current()).toBeNull();
  });

  // 令牌不是 JWT 时 OIDC 客户端给出空负载而不是报错。空负载里自然没有租户声明，
  // 照单全收就会把这个会话当成宿主。
  it('rejects an access token whose payload cannot be read', async () => {
    signedIn({});

    await expect(service.initializeAuth()).rejects.toThrow();
    expect(service.isAuthenticated()).toBe(false);
    expect(tenantContext.current()).toBeNull();
  });
});
//#endif
