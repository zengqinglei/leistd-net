import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { toast } from '@spartan-ng/brain/sonner';
import { of, Subject, throwError } from 'rxjs';

import { LanguageSwitcher } from './language-switcher';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../core/i18n/transloco.testing';
//#endif
import { AuthService } from '../../../core/services/auth-service';
import { LanguageService } from '../../../core/services/language-service';
import { SettingContextService } from '../../../core/settings/setting-context-service';
import { SettingService } from '../../../core/settings/setting-service';

import type { MockedObject } from 'vitest';

/**
 * 语言选择的归属：切换器是唯一决定「这次选择算谁的」的地方。
 *
 * 已登录的选择属于账户，只能写回设置；未登录的选择属于这台设备，才落本地存储。
 * 两边搞混就回到那个共享机器上的老问题：A 退出后，B 在登录页看到 A 的语言。
 * LanguageService 自己拦不住这种误用——它只是照吩咐做，所以这条边界得在调用点上钉。
 */
describe('LanguageSwitcher', () => {
  let component: LanguageSwitcher;
  let authService: Pick<MockedObject<AuthService>, 'isAuthenticated'>;
  let settingService: Pick<MockedObject<SettingService>, 'setForCurrentUser'>;

  function setUp(authenticated: boolean): void {
    authService = {
      isAuthenticated: vi.fn().mockName('AuthService.isAuthenticated'),
    };
    authService.isAuthenticated.mockReturnValue(authenticated);
    settingService = {
      setForCurrentUser: vi.fn().mockName('SettingService.setForCurrentUser'),
    };
    settingService.setForCurrentUser.mockReturnValue(of(undefined));

    TestBed.configureTestingModule({
      imports: [LanguageSwitcher],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // 真实 AuthService 会拉起认证栈；这里只关心「已登录还是没登录」。
        { provide: AuthService, useValue: authService },
        { provide: SettingService, useValue: settingService },
        ...provideTranslocoTesting(['en', 'zh-CN']),
      ],
    });
    localStorage.setItem(LanguageService.STORAGE_KEY, 'en');
    component = TestBed.createComponent(LanguageSwitcher).componentInstance;
  }

  afterEach(() => localStorage.removeItem(LanguageService.STORAGE_KEY));

  it('writes an authenticated choice to the account without touching the device preference', async () => {
    setUp(true);

    component.select('zh-CN');
    await vi.waitFor(() => expect(TestBed.inject(LanguageService).activeLang()).toBe('zh-CN'));

    expect(settingService.setForCurrentUser).toHaveBeenCalledWith({
      name: 'Display.Language',
      value: 'zh-CN',
    });
    expect(TestBed.inject(LanguageService).activeLang()).toBe('zh-CN');
    // 落盘就分不清「这台机器的偏好」和「上一个登录者的偏好」了。
    expect(localStorage.getItem(LanguageService.STORAGE_KEY)).toBe('en');
  });

  /**
   * 切语言之后，文案与日期必须同时换。
   *
   * 日期的书写方式取自**活动语言**（见 SettingContextService.displayLocale），
   * 与切换器改的是同一个东西；若哪天又改回从设置快照推导，这条就会红。
   */
  it('switches text and dates together once the account write succeeds', async () => {
    setUp(true);
    const settingContext = TestBed.inject(SettingContextService);
    expect(settingContext.displayLocale()).toBe('en');

    component.select('zh-CN');
    await vi.waitFor(() => expect(TestBed.inject(LanguageService).activeLang()).toBe('zh-CN'));

    expect(settingContext.displayLocale()).toBe('zh-CN');
  });

  /**
   * 先写回、成功后再切换。先切换的话，切换触发的设置页重取早于写入完成，
   * 偏好页显示旧值（全功能端到端发现）。
   */
  it('does not switch before the account write completes', () => {
    setUp(true);
    const pending = new Subject<void>();
    settingService.setForCurrentUser.mockReturnValue(pending);

    component.select('zh-CN');

    expect(TestBed.inject(LanguageService).activeLang()).toBe('en');
  });

  it('keeps the current language and says so when the account write fails', () => {
    setUp(true);
    settingService.setForCurrentUser.mockReturnValue(throwError(() => new Error('network down')));
    const error = vi.spyOn(toast, 'error').mockImplementation(() => '');

    component.select('zh-CN');

    // 不切换，界面与账户偏好就不会分叉；但必须说出来，否则用户以为存好了。
    expect(TestBed.inject(LanguageService).activeLang()).toBe('en');
    expect(error).toHaveBeenCalled();
  });

  it('records an anonymous choice as the device preference and writes no setting', async () => {
    setUp(false);

    component.select('zh-CN');

    // 访客没有账户可写；不落盘的话这次选择一刷新就没了。
    expect(localStorage.getItem(LanguageService.STORAGE_KEY)).toBe('zh-CN');
    await vi.waitFor(() => expect(TestBed.inject(LanguageService).activeLang()).toBe('zh-CN'));
    expect(settingService.setForCurrentUser).not.toHaveBeenCalled();
  });
});
