//#if (ExternalLogin)
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AccountService } from './account-service';
import { TenantContextService } from '../../../core/services/tenant-context-service';

describe('External account navigation', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: TenantContextService, useValue: { current: () => ({ key: 'tenant-key' }) } },
      ],
    });
  });

  it('carries the authorization return address and tenant in the login challenge', () => {
    const returnUrl = '/connect/authorize?client_id=resource&state=original';
    const url = new URL(
      TestBed.inject(AccountService).getExternalLoginUrl('google', returnUrl),
      'https://app.test',
    );
    expect(url.pathname).toBe('/api/v1/external-auth/google/challenge');
    expect(url.searchParams.get('tenant')).toBe('tenant-key');
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
