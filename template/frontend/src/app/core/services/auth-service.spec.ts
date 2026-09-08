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

    expect(request.request.context.get(SILENT_AUTH)).toBeTrue();
    request.flush(null, { status: 401, statusText: 'Unauthorized' });

    await expectAsync(initialization).toBeRejected();
  });

  it('propagates startup probe failures so callers can distinguish outages from 401', async () => {
    const initialization = service.initializeAuth();
    httpTesting
      .expectOne('/api/v1/auth/me')
      .flush({ detail: 'Gateway unavailable' }, { status: 503, statusText: 'Unavailable' });

    await expectAsync(initialization).toBeRejected();
    expect(service.isAuthenticated()).toBeFalse();
  });
});
//#endif
//#if (!LocalIdentity)
import { TestBed } from '@angular/core/testing';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { of } from 'rxjs';

import { AuthService } from './auth-service';
import { TenantContextService } from './tenant-context-service';

describe('Resource AuthService', () => {
  let oidc: jasmine.SpyObj<OidcSecurityService>;
  let service: AuthService;
  let tenantContext: TenantContextService;

  beforeEach(() => {
    oidc = jasmine.createSpyObj<OidcSecurityService>('OidcSecurityService', [
      'checkAuth',
      'authorize',
      'logoff',
    ]);
    oidc.logoff.and.returnValue(of(undefined));
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

  // 认证阶段与导航阶段的边界。initializeAuth() 一度自己消费 returnUrl 并导航，而那时
  // 启动流还停在 loading、permissionGuard 正等它离开 loading——returnUrl 指向 /platform
  // 时形成死等。所以认证只确立主体，落地地址留给回调组件取。
  it('establishes the subject without consuming the return url', async () => {
    sessionStorage.setItem('app.auth.returnUrl', '/platform/users');
    oidc.checkAuth.and.returnValue(
      of({
        isAuthenticated: true,
        accessToken: jwt({ sub: crypto.randomUUID(), tenant_id: crypto.randomUUID() }),
        idToken: 'validated-id-token',
        userData: {},
      }),
    );

    try {
      await service.initializeAuth();

      expect(service.isAuthenticated()).toBeTrue();
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
    oidc.checkAuth.and.returnValue(
      of({
        isAuthenticated: true,
        accessToken: jwt({ sub: crypto.randomUUID(), tenant_id: tenantId, email: 'user@test.dev' }),
        idToken: 'validated-id-token',
        userData: {},
      }),
    );

    await service.initializeAuth();

    expect(service.isAuthenticated()).toBeTrue();
    expect(tenantContext.current()?.id).toBe(tenantId);
  });

  it('rejects an authenticated token with multiple tenant claims', async () => {
    oidc.checkAuth.and.returnValue(
      of({
        isAuthenticated: true,
        accessToken: jwt({ tenant_id: [crypto.randomUUID(), crypto.randomUUID()] }),
        idToken: 'validated-id-token',
        userData: {},
      }),
    );

    await expectAsync(service.initializeAuth()).toBeRejected();
    expect(tenantContext.current()).toBeNull();
  });
});

function jwt(payload: Record<string, unknown>): string {
  const encoded = btoa(JSON.stringify(payload))
    .replace(/=/g, '')
    .replace(/\+/g, '-')
    .replace(/\//g, '_');
  return `header.${encoded}.signature`;
}
//#endif
