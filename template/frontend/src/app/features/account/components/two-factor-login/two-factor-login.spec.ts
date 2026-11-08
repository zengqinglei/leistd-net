import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
//#if (IncludeLocalization)
import { provideTranslocoScope } from '@jsverse/transloco';
//#endif
import { toast } from '@spartan-ng/brain/sonner';
import { of, Subject, throwError } from 'rxjs';

import { TwoFactorLogin } from './two-factor-login';
import { ApplicationHttpError } from '../../../../core/errors/application-http-error';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../core/i18n/transloco.testing';
//#endif
//#if (OpenIddictServer)
import { FullPageNavigationService } from '../../../../core/routing/full-page-navigation-service';
//#endif
import { AuthService } from '../../../../core/services/auth-service';
//#if (IncludeLocalization)
import { LanguageService } from '../../../../core/services/language-service';
//#endif
import { SessionContextService } from '../../../../core/services/session-context-service';
import { TwoFactorLoginOutputDto } from '../../dtos/account.dto';
import { AccountService } from '../../services/account-service';

import type { MockedObject } from 'vitest';
//#if (OpenIddictServer)

/** 授权端点送来的原目标：缓存请求的引用与重新认证证明原样带回。 */
const AUTHORIZE =
  '/connect/authorize?client_id=resource&request_uri=urn%3Aabc&reauthentication=proof';
//#endif

const CONTEXT: TwoFactorLoginOutputDto = {
  twoFactorMethods: ['authenticator', 'recoveryCode'],
  defaultTwoFactorMethod: 'authenticator',
  maskedEmail: null,
};

function rejection(code: string, status = 401): ApplicationHttpError {
  return ApplicationHttpError.from(
    new HttpErrorResponse({ status, error: { status, code, title: code, detail: code } }),
  );
}

/** 按钮处于忙碌态：禁用并显示加载。 */
function busy(button: Element | null): boolean {
  return (button as HTMLButtonElement).disabled && button!.querySelector('hlm-spinner') !== null;
}

/** 等一串微任务（请求回来之后的收尾）落定。 */
function settle(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve));
}

describe('TwoFactorLogin', () => {
  let fixture: ComponentFixture<TwoFactorLogin>;
  let router: Router;
  let queryParams: Record<string, string>;
  let currentUser: WritableSignal<{ twoFactorSetupRequired?: boolean } | null>;
  let authService: Pick<MockedObject<AuthService>, 'loadUser' | 'clearAuthData' | 'currentUser'>;
  let account: Pick<
    MockedObject<AccountService>,
    'getTwoFactorLogin' | 'cancelTwoFactorLogin' | 'completeTwoFactorLogin'
  >;

  async function setUp(context = of(CONTEXT) as ReturnType<AccountService['getTwoFactorLogin']>) {
    currentUser = signal<{ twoFactorSetupRequired?: boolean } | null>(null);
    authService = {
      loadUser: vi.fn().mockName('AuthService.loadUser'),
      clearAuthData: vi.fn().mockName('AuthService.clearAuthData'),
      currentUser: currentUser as never,
    };
    authService.loadUser.mockReturnValue(of(undefined) as never);

    await TestBed.configureTestingModule({
      imports: [TwoFactorLogin],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        provideTranslocoScope('account'),
        //#endif
        { provide: AuthService, useValue: authService },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap(queryParams) } },
        },
      ],
    }).compileComponents();

    const accountService = TestBed.inject(AccountService);
    account = {
      getTwoFactorLogin: vi.spyOn(accountService, 'getTwoFactorLogin').mockReturnValue(context),
      cancelTwoFactorLogin: vi
        .spyOn(accountService, 'cancelTwoFactorLogin')
        .mockReturnValue(of(undefined)),
      completeTwoFactorLogin: vi
        .spyOn(accountService, 'completeTwoFactorLogin')
        .mockReturnValue(of(undefined)),
    } as never;
    // 收尾会建立会话上下文（拉权限 + 拉设置）：打桩，免得用例挂在永不返回的请求上
    const sessionContext = TestBed.inject(SessionContextService);
    vi.spyOn(sessionContext, 'establish').mockResolvedValue();
    vi.spyOn(sessionContext, 'enter').mockImplementation(async (navigate) => {
      await sessionContext.establish();
      return navigate();
    });

    //#if (IncludeLocalization)
    await TestBed.inject(LanguageService).initialized;
    await TestBed.inject(LanguageService).loadScopes(['account']);
    //#endif
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(TwoFactorLogin);
    fixture.detectChanges();
    await settle();
    fixture.detectChanges();
  }

  function host(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function challenge(): Element | null {
    return host().querySelector('[data-testid="two-factor-challenge"]');
  }

  async function submitCode(): Promise<void> {
    const input = host().querySelector<HTMLInputElement>(
      '[data-testid="two-factor-code-input"] input',
    )!;
    input.value = '123456';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    host().querySelector('form')!.dispatchEvent(new Event('submit'));
    // 验证请求回来之后还有一串收尾（读当前用户、建立会话上下文、跳转）
    for (let turn = 0; turn < 5; turn++) {
      await settle();
    }
    fixture.detectChanges();
  }

  /** 经 navigateByUrl 发出的导航，按地址栏形式读出（UrlTree 与字符串一视同仁）。 */
  function navigatedUrls(): string[] {
    return vi
      .mocked(router.navigateByUrl)
      .mock.calls.map(([url]) => (typeof url === 'string' ? url : router.serializeUrl(url)));
  }

  beforeEach(() => {
    queryParams = { returnUrl: '/workspace/settings/security' };
  });

  afterEach(() => vi.restoreAllMocks());

  // 刷新或从授权端点接回：凭临时 Cookie 读出可用方式，直接给出默认方式
  it('reads the pending sign-in and shows its default method', async () => {
    await setUp();

    expect(account.getTwoFactorLogin).toHaveBeenCalledOnce();
    expect(challenge()?.getAttribute('data-method')).toBe('authenticator');
  });

  it('shows a placeholder while the pending sign-in is being read', async () => {
    await setUp(new Subject<never>());

    expect(challenge()).toBeNull();
    expect(host().querySelector('[data-testid="two-factor-login-loading"]')).not.toBeNull();
  });

  // 没有待完成的登录是常态（已到期、已完成或从没开始）：安静地回登录页，原目标原样带回
  it('returns quietly to sign-in with the return address when nothing is pending', async () => {
    const error = vi.spyOn(toast, 'error').mockImplementation(() => '');
    await setUp(throwError(() => rejection('Auth:TwoFactorChallengeExpired')));

    expect(router.navigate).toHaveBeenCalledWith(['/auth/login'], {
      queryParams: { returnUrl: '/workspace/settings/security' },
      replaceUrl: true,
    });
    expect(error).not.toHaveBeenCalled();
  });

  // 连不上或服务端故障不当作"没有待完成的登录"：就地说明，重试后照常继续
  it('explains a failed read in place and reads again on retry', async () => {
    await setUp(throwError(() => rejection('Error:InternalServerError', 500)));

    expect(router.navigate).not.toHaveBeenCalled();
    expect(host().querySelector('[data-testid="two-factor-login-error"]')).not.toBeNull();

    account.getTwoFactorLogin.mockReturnValue(of(CONTEXT));
    (host().querySelector('[data-testid="two-factor-login-retry"]') as HTMLElement).click();
    await settle();
    fixture.detectChanges();

    expect(challenge()).not.toBeNull();
  });

  it('continues to the return address after the second step', async () => {
    await setUp();

    await submitCode();

    expect(account.completeTwoFactorLogin).toHaveBeenCalledExactlyOnceWith({ code: '123456' });
    expect(navigatedUrls()).toEqual(['/workspace/settings/security']);
    expect(TestBed.inject(SessionContextService).establish).toHaveBeenCalled();
  });

  // 第二步完成后才知道受限：原目标随之带去设置页
  it('carries the return address to the setup page of a restricted session', async () => {
    await setUp();
    authService.loadUser.mockImplementation(() => {
      currentUser.set({ twoFactorSetupRequired: true });
      return of(undefined) as never;
    });

    await submitCode();

    expect(navigatedUrls()).toEqual([
      '/auth/two-factor-setup?returnUrl=%2Fworkspace%2Fsettings%2Fsecurity',
    ]);
  });

  // 忙碌分两段：验证请求在途由第二步自己持有，通过后的收尾由本页持有，两段之间没有能再点一次的空档
  it('keeps the second step busy from verification through finishing and frees it on failure', async () => {
    await setUp();
    const verification = new Subject<void>();
    account.completeTwoFactorLogin.mockReturnValue(verification);
    const user = new Subject<never>();
    authService.loadUser.mockReturnValue(user as never);
    vi.spyOn(toast, 'error').mockImplementation(() => '');
    const submit = () => host().querySelector('[data-testid="two-factor-submit"]');

    await submitCode();
    expect(busy(submit())).toBe(true);

    verification.next();
    verification.complete();
    await settle();
    fixture.detectChanges();
    expect(authService.loadUser).toHaveBeenCalledOnce();
    expect(busy(submit())).toBe(true);

    user.error(new Error('me unavailable'));
    await settle();
    fixture.detectChanges();
    expect(busy(submit())).toBe(false);
    expect(TestBed.inject(SessionContextService).establish).not.toHaveBeenCalled();
  });

  // 返回（换账号）：放弃这次待完成的登录，再回登录页
  it('cancels the pending sign-in when going back', async () => {
    await setUp();

    (host().querySelector('[data-testid="two-factor-back"]') as HTMLElement).click();
    await settle();

    expect(account.cancelTwoFactorLogin).toHaveBeenCalledOnce();
    expect(router.navigate).toHaveBeenCalledWith(['/auth/login'], {
      queryParams: { returnUrl: '/workspace/settings/security' },
      replaceUrl: true,
    });
  });

  // 另一个标签页已经完成了这次登录：提交得到"已到期"，但会话已经在了，直接回到原目标
  it('continues to the return address when another tab already finished the sign-in', async () => {
    await setUp();
    account.completeTwoFactorLogin.mockReturnValue(
      throwError(() => rejection('Auth:TwoFactorChallengeExpired')),
    );
    const error = vi.spyOn(toast, 'error').mockImplementation(() => '');

    await submitCode();
    await settle();

    expect(navigatedUrls()).toEqual(['/workspace/settings/security']);
    expect(router.navigate).not.toHaveBeenCalledWith(['/auth/login'], expect.anything());
    expect(error).not.toHaveBeenCalled();
  });

  it('returns to sign-in with the reason when the pending sign-in expired and nobody is signed in', async () => {
    await setUp();
    account.completeTwoFactorLogin.mockReturnValue(
      throwError(() => rejection('Auth:TwoFactorChallengeExpired')),
    );
    authService.loadUser.mockReturnValue(
      throwError(() => rejection('Error:Unauthorized')) as never,
    );
    const error = vi.spyOn(toast, 'error').mockImplementation(() => '');

    await submitCode();
    await settle();

    expect(error).toHaveBeenCalledOnce();
    expect(router.navigate).toHaveBeenCalledWith(['/auth/login'], {
      queryParams: { returnUrl: '/workspace/settings/security' },
      replaceUrl: true,
    });
  });
  //#if (OpenIddictServer)

  // 回授权端点是整页离开：不读当前用户、不建立会话上下文，按钮转到页面离开为止
  it('keeps the second step busy until it leaves for an authorization request', async () => {
    queryParams = { returnUrl: AUTHORIZE };
    await setUp();
    const leave = vi
      .spyOn(TestBed.inject(FullPageNavigationService), 'replace')
      .mockImplementation(() => undefined);

    await submitCode();

    expect(leave).toHaveBeenCalledExactlyOnceWith(AUTHORIZE);
    expect(authService.loadUser).not.toHaveBeenCalled();
    expect(TestBed.inject(SessionContextService).establish).not.toHaveBeenCalled();
    expect(busy(host().querySelector('[data-testid="two-factor-submit"]'))).toBe(true);
  });
  //#endif
});
