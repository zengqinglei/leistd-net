//#if (ExternalLogin)
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { ExternalAuthCallback } from './external-auth-callback';
import { ApplicationHttpError } from '../../../../core/errors/application-http-error';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../core/i18n/transloco.testing';
//#endif
import { AuthService } from '../../../../core/services/auth-service';
import { SessionContextService } from '../../../../core/services/session-context-service';
import { AccountService } from '../../services/account-service';

/**
 * 外部登录回调的接线。
 *
 * 会话上下文必须在导航之前建立。删掉那一行，其它任何用例都不会变红，
 * 而表现是保存过的显示偏好在外部登录后不生效（SPA 内跳转不会重跑应用初始化器），
 * 以及落地页在权限未就位时按无权限渲染。
 *
 * 业务拒绝展示服务端下发的原因：该邮箱已有账号时用户要据此先登录、再绑定。
 */
describe('ExternalAuthCallback', () => {
  let fixture: ComponentFixture<ExternalAuthCallback>;
  let calls: string[];
  let accountService: { externalLoginCallback: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    calls = [];

    accountService = {
      externalLoginCallback: vi.fn().mockName('AccountService.externalLoginCallback'),
    };
    accountService.externalLoginCallback.mockReturnValue(of(undefined) as never);

    const authService = {
      loadUser: vi.fn().mockName('AuthService.loadUser'),
      currentUser: signal(null) as never,
    };
    authService.loadUser.mockReturnValue(of(undefined) as never);

    const sessionContext = {
      establish: vi
        .fn()
        .mockName('establish')
        .mockImplementation(async () => {
          calls.push('establish');
        }),
    };

    await TestBed.configureTestingModule({
      imports: [ExternalAuthCallback],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
        { provide: AccountService, useValue: accountService },
        { provide: AuthService, useValue: authService },
        { provide: SessionContextService, useValue: sessionContext },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              queryParamMap: convertToParamMap({ intent: 'login' }),
              paramMap: convertToParamMap({ provider: 'github' }),
            },
          },
        },
      ],
    }).compileComponents();

    vi.spyOn(TestBed.inject(Router), 'navigate').mockImplementation(async () => {
      calls.push('navigate');
      return true;
    });

    fixture = TestBed.createComponent(ExternalAuthCallback);
  });

  it('establishes the session before navigating', async () => {
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve));

    expect(calls).toContain('navigate');
    expect(calls.indexOf('establish')).toBeGreaterThanOrEqual(0);
    expect(calls.indexOf('establish')).toBeLessThan(calls.indexOf('navigate'));
  });

  it('preserves the protected return address when a second step is required', async () => {
    accountService.externalLoginCallback.mockReturnValue(
      of({
        requiresTwoFactor: true,
        twoFactorToken: 'challenge',
        returnUrl: '/connect/authorize?state=original',
      }),
    );
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve));
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/auth/login'], {
      state: { twoFactorToken: 'challenge', returnUrl: '/connect/authorize?state=original' },
    });
    expect(calls).not.toContain('establish');
  });

  it('uses the protected return address after establishing a session', async () => {
    accountService.externalLoginCallback.mockReturnValue(
      of({ returnUrl: '/workspace/settings/security' }),
    );
    const navigate = vi
      .spyOn(TestBed.inject(Router), 'navigateByUrl')
      .mockImplementation(async () => {
        calls.push('navigate-return');
        return true;
      });
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve));
    expect(navigate).toHaveBeenCalledWith('/workspace/settings/security');
    expect(calls.indexOf('establish')).toBeLessThan(calls.indexOf('navigate-return'));
  });

  it('shows the server reason when the sign-in is rejected', async () => {
    vi.spyOn(console, 'error').mockReturnValue(undefined);
    const reason = 'An account with this email already exists.';
    accountService.externalLoginCallback.mockReturnValue(
      throwError(() =>
        ApplicationHttpError.from(
          new HttpErrorResponse({
            status: 409,
            error: { code: 'ExternalAuth:AccountExistsSignInToLink', detail: reason },
          }),
        ),
      ),
    );

    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(reason);
    expect(calls).not.toContain('navigate');
  });

  it('shows a generic message when the server fails', async () => {
    vi.spyOn(console, 'error').mockReturnValue(undefined);
    const detail = 'Internal failure detail';
    accountService.externalLoginCallback.mockReturnValue(
      throwError(() =>
        ApplicationHttpError.from(new HttpErrorResponse({ status: 500, error: { detail } })),
      ),
    );

    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain(detail);
  });
});
//#endif
