import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AccountPreferencesService } from './account-preferences-service';
import { AuthService } from './auth-service';
//#if (IncludeNotifications || IncludeRealTime)
import { SignalRService } from './signalr-service';
//#endif
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../i18n/transloco.testing';
//#endif
import { SILENT_AUTH } from '../interceptors/http-context-tokens';
//#if (RemoteTokenAuth)
import { MOCKED_URL } from '../mock/mocked-url';
//#endif

describe('AuthService', () => {
  let service: AuthService;
  let httpTesting: HttpTestingController;
  //#if (RemoteTokenAuth)
  /** 退出接口是否由 Mock 应答；默认不是（部署构建）。 */
  let mockedSignOut: boolean;
  //#endif

  beforeEach(() => {
    TestBed.configureTestingModule({
      //#if (IncludeLocalization)
      providers: [
        AuthService,
        provideHttpClient(),
        provideHttpClientTesting(),
        // 读 /me 时按需取偏好服务，它经语言服务依赖 Transloco
        ...provideTranslocoTesting(['en', 'zh-CN']),
      ],
      //#else
      providers: [AuthService, provideHttpClient(), provideHttpClientTesting()],
      //#endif
    });
    //#if (RemoteTokenAuth)
    mockedSignOut = false;
    TestBed.overrideProvider(MOCKED_URL, { useValue: () => mockedSignOut });
    //#endif
    service = TestBed.inject(AuthService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  // 号在 /me 发出时取：回来之前主体离开或有写入成功，这份读取就过期，建立会话时不再应用
  it('takes the preference read ticket when the /me request is sent', async () => {
    const preferences = TestBed.inject(AccountPreferencesService);
    const loading = service.initializeAuth();
    const request = httpTesting.expectOne('/api/v1/auth/me');

    await preferences.clear();
    request.flush({ id: 'u1', username: 'alice', preferences: null, preferenceOverrides: null });
    await loading;

    expect(service.lastReadPreferences()?.ticket).not.toBe(preferences.beginRead());
  });

  it('marks the session probe as silent so the interceptor skips the 401 redirect', async () => {
    const initialization = service.initializeAuth();
    const request = httpTesting.expectOne('/api/v1/auth/me');

    expect(request.request.context.get(SILENT_AUTH)).toBe(true);
    request.flush(null, { status: 401, statusText: 'Unauthorized' });

    await expect(initialization).rejects.toThrow();
  });

  // 会话中途的重查：401 交给拦截器清会话并转去登录
  it('lets a mid-session re-read reach the interceptor on 401', () => {
    const subscription = service.fetchUser(false).subscribe({ error: () => undefined });
    const request = httpTesting.expectOne('/api/v1/auth/me');

    expect(request.request.context.get(SILENT_AUTH)).toBe(false);
    request.flush(null, { status: 401, statusText: 'Unauthorized' });
    subscription.unsubscribe();
  });

  it('propagates startup probe failures so callers can distinguish outages from 401', async () => {
    const initialization = service.initializeAuth();
    httpTesting
      .expectOne('/api/v1/auth/me')
      .flush({ detail: 'Gateway unavailable' }, { status: 503, statusText: 'Unavailable' });

    await expect(initialization).rejects.toThrow();
    expect(service.isAuthenticated()).toBe(false);
  });

  /** 已登录：退出前当前页面显示着这位用户。 */
  function signIn(): void {
    service.setCurrentUser({ id: 'u1', username: 'alice' } as never);
  }
  //#if (IncludeNotifications || IncludeRealTime)

  /** 实时连接的同步作废：旧主体的推送与在途异步写入不再落到界面上。 */
  function spyRealtimeReset(): () => void {
    const reset = vi.spyOn(TestBed.inject(SignalRService), 'reset').mockResolvedValue();
    return () => expect(reset).toHaveBeenCalledOnce();
  }
  //#endif
  //#if (LocalIdentity)

  // 退出请求在途时页面仍在：先清空主体的话，问候语与头像会在离开前消失
  for (const outcome of ['succeeds', 'fails'] as const) {
    it(`keeps the user on screen while signing out and leaves when the request ${outcome}`, () => {
      signIn();
      //#if (IncludeNotifications || IncludeRealTime)
      const expectRealtimeReset = spyRealtimeReset();
      //#endif
      const loadPage = vi.spyOn(service, 'loadPage').mockImplementation(() => undefined);

      service.logout();

      expect(service.currentUser()).not.toBeNull();
      //#if (IncludeNotifications || IncludeRealTime)
      expectRealtimeReset();
      //#endif
      const request = httpTesting.expectOne('/api/v1/auth/logout');
      if (outcome === 'succeeds') {
        request.flush({});
      } else {
        request.flush(null, { status: 500, statusText: 'Server Error' });
      }
      expect(loadPage).toHaveBeenCalledExactlyOnceWith('/auth/login');
    });
  }

  it('keeps the user on screen while signing out to sign in again', () => {
    signIn();
    const loadPage = vi.spyOn(service, 'loadPage').mockImplementation(() => undefined);

    service.signInAgain('/workspace/settings/security');

    expect(service.currentUser()).not.toBeNull();
    httpTesting.expectOne('/api/v1/auth/logout').flush({});
    expect(loadPage).toHaveBeenCalledExactlyOnceWith(
      '/auth/login?returnUrl=%2Fworkspace%2Fsettings%2Fsecurity',
    );
  });
  //#endif
  //#if (RemoteTokenAuth)

  // 整页表单退出：页面在提交之后才离开，主体留到那时
  it('keeps the user on screen while posting the sign-out form', () => {
    signIn();
    //#if (IncludeNotifications || IncludeRealTime)
    const expectRealtimeReset = spyRealtimeReset();
    //#endif
    const submit = vi
      .spyOn(HTMLFormElement.prototype, 'submit')
      .mockImplementation(() => undefined);

    service.logout();

    expect(service.currentUser()).not.toBeNull();
    //#if (IncludeNotifications || IncludeRealTime)
    expectRealtimeReset();
    //#endif
    expect(submit).toHaveBeenCalledOnce();
    const posted = submit.mock.contexts[0] as HTMLFormElement;
    expect(posted.method).toBe('post');
    expect(posted.getAttribute('action')).toBe('/api/v1/auth/logout');
    posted.remove();
  });

  // Mock 退出不经 OIDC：接口应答后跳一次，主体照旧先清空
  it('clears the user before the mocked sign-out request and then leaves', () => {
    mockedSignOut = true;
    signIn();
    const loadPage = vi.spyOn(service, 'loadPage').mockImplementation(() => undefined);

    service.logout();

    expect(service.currentUser()).toBeNull();
    httpTesting.expectOne('/api/v1/auth/logout').flush({});
    expect(loadPage).toHaveBeenCalledExactlyOnceWith('/auth/login');
  });
  //#endif
});
