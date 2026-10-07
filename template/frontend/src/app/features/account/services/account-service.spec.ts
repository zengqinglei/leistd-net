import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AccountService } from './account-service';
import { AuthService } from '../../../core/services/auth-service';
//#if (ExternalLogin && IncludeMultiTenancy)
import { TenantContextService } from '../../../core/services/tenant-context-service';
//#endif

import type { Mock } from 'vitest';

/**
 * API 服务只封装 HTTP：资料类写请求原样返回服务端保存后的资料，
 * 写回当前用户由发起操作的页面负责（见 profile-panel.spec.ts）。
 */
describe('AccountService profile writes', () => {
  const savedUser = { id: 'user-1', username: 'alice', email: 'alice@example.com' };
  let setCurrentUser: Mock;
  let httpTesting: HttpTestingController;
  let account: AccountService;

  beforeEach(() => {
    setCurrentUser = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { setCurrentUser } },
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    account = TestBed.inject(AccountService);
  });

  afterEach(() => httpTesting.verify());

  it('returns the saved profile without writing the current user', () => {
    const emitted: unknown[] = [];
    account
      .updateCurrentUser({ username: 'alice', email: 'alice@example.com' })
      .subscribe((user) => emitted.push(user));

    const request = httpTesting.expectOne({ method: 'PUT', url: '/api/v1/auth/me' });
    expect(request.request.body).toEqual({ username: 'alice', email: 'alice@example.com' });
    request.flush(savedUser);

    expect(emitted).toEqual([savedUser]);
    expect(setCurrentUser).not.toHaveBeenCalled();
  });

  it('returns the profile with the new avatar without writing the current user', () => {
    const emitted: unknown[] = [];
    account.setAvatar({ avatar: null }).subscribe((user) => emitted.push(user));

    const request = httpTesting.expectOne({ method: 'PUT', url: '/api/v1/auth/me/avatar' });
    expect(request.request.body).toEqual({ avatar: null });
    request.flush(savedUser);

    expect(emitted).toEqual([savedUser]);
    expect(setCurrentUser).not.toHaveBeenCalled();
  });
  //#if (Email)

  it('returns the profile after confirming the email without writing the current user', () => {
    const emitted: unknown[] = [];
    account
      .confirmCurrentEmail({ challengeId: 'challenge-1', code: '123456' })
      .subscribe((user) => emitted.push(user));

    const request = httpTesting.expectOne({
      method: 'POST',
      url: '/api/v1/auth/me/email-verification/confirm',
    });
    expect(request.request.body).toEqual({ challengeId: 'challenge-1', code: '123456' });
    request.flush({ ...savedUser, isEmailVerified: true });

    expect(emitted).toEqual([{ ...savedUser, isEmailVerified: true }]);
    expect(setCurrentUser).not.toHaveBeenCalled();
  });
  //#endif
});
//#if (ExternalLogin)

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
