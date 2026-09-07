//#if (ExternalLogin)
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
//#if (IncludeLocalization)
import { provideTransloco, TRANSLOCO_LOADER } from '@jsverse/transloco';
//#endif
import { of } from 'rxjs';

import { ExternalAuthCallback } from './external-auth-callback';
import { AuthService } from '../../../../core/services/auth-service';
import { SessionContextService } from '../../../../core/services/session-context-service';
import { AccountService } from '../../services/account-service';

/**
 * 外部登录回调的接线。
 *
 * 这里只锁一件事：会话上下文必须在导航之前建立。删掉那一行，其它任何用例都不会变红，
 * 而表现是保存过的显示偏好在外部登录后不生效（SPA 内跳转不会重跑应用初始化器），
 * 以及落地页在权限未就位时按无权限渲染。
 */
describe('ExternalAuthCallback', () => {
  let fixture: ComponentFixture<ExternalAuthCallback>;
  let calls: string[];

  beforeEach(async () => {
    calls = [];

    const accountService = jasmine.createSpyObj<AccountService>('AccountService', [
      'externalLoginCallback',
    ]);
    accountService.externalLoginCallback.and.returnValue(of(undefined) as never);

    const authService = jasmine.createSpyObj<AuthService>('AuthService', ['loadUser']);
    authService.loadUser.and.returnValue(of(undefined) as never);

    const sessionContext = {
      establish: jasmine.createSpy('establish').and.callFake(async () => {
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
        provideTransloco({
          config: { availableLangs: ['en'], defaultLang: 'en', fallbackLang: 'en' },
        }),
        { provide: TRANSLOCO_LOADER, useValue: { getTranslation: () => Promise.resolve({}) } },
        //#endif
        { provide: AccountService, useValue: accountService },
        { provide: AuthService, useValue: authService },
        { provide: SessionContextService, useValue: sessionContext },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              queryParamMap: convertToParamMap({ code: 'c', state: 's' }),
              paramMap: convertToParamMap({ provider: 'github' }),
            },
          },
        },
      ],
    }).compileComponents();

    spyOn(TestBed.inject(Router), 'navigate').and.callFake(async () => {
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
});
//#endif
