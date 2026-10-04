//#if (ExternalLogin)
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AccountService } from './account-service';
//#if (IncludeMultiTenancy)
import { TenantContextService } from '../../../core/services/tenant-context-service';
//#endif

describe('External account navigation', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      //#if (IncludeMultiTenancy)
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: TenantContextService, useValue: { current: () => ({ key: 'tenant-key' }) } },
      ],
      //#else
      providers: [provideHttpClient(), provideHttpClientTesting()],
      //#endif
    });
  });

  it('carries the authorization return address with the applicable tenant scope', () => {
    const returnUrl = '/connect/authorize?client_id=resource&state=original';
    const url = new URL(
      TestBed.inject(AccountService).getExternalLoginUrl('google', returnUrl),
      'https://app.test',
    );
    expect(url.pathname).toBe('/api/v1/external-auth/google/challenge');
    //#if (IncludeMultiTenancy)
    expect(url.searchParams.get('tenant')).toBe('tenant-key');
    //#else
    expect(url.searchParams.has('tenant')).toBe(false);
    //#endif
    expect(url.searchParams.get('returnUrl')).toBe(returnUrl);
  });

  it('uses protected routes for both phases of linking', () => {
    const account = TestBed.inject(AccountService);
    expect(account.getExternalLinkUrl('github')).toBe(
      '/api/v1/external-auth/github/link/challenge',
    );
    account.linkExternalLogin('github').subscribe();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/external-auth/github/link/complete')
      .flush({ linked: true });
    TestBed.inject(HttpTestingController).verify();
  });
});
//#endif
