//#if (IdentityService)
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
//#if (ResourceService)
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
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
        { provide: Router, useValue: jasmine.createSpyObj<Router>('Router', ['navigateByUrl']) },
      ],
    });
    service = TestBed.inject(AuthService);
    tenantContext = TestBed.inject(TenantContextService);
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
