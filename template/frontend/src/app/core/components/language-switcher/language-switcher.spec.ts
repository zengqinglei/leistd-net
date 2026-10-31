import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { toast } from '@spartan-ng/brain/sonner';

import { LanguageSwitcher } from './language-switcher';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../i18n/transloco.testing';
//#endif
import {
  AccountPreferencesService,
  PreferenceWriteOutcome,
} from '../../services/account-preferences-service';
import { AuthService } from '../../services/auth-service';
import { LanguageService } from '../../services/language-service';

import type { MockedObject } from 'vitest';

/** 语言选择的归属由切换器决定：已登录由账户偏好状态保存并应用，未登录生效后记在设备。 */
describe('LanguageSwitcher', () => {
  let component: LanguageSwitcher;
  let authService: Pick<MockedObject<AuthService>, 'isAuthenticated'>;
  let preferences: Pick<MockedObject<AccountPreferencesService>, 'write'>;

  function setUp(
    authenticated: boolean,
    outcome: PreferenceWriteOutcome = { status: 'saved' },
  ): void {
    authService = {
      isAuthenticated: vi.fn().mockName('AuthService.isAuthenticated'),
    };
    authService.isAuthenticated.mockReturnValue(authenticated);
    preferences = {
      write: vi.fn().mockName('AccountPreferencesService.write'),
    };
    preferences.write.mockResolvedValue(outcome);

    TestBed.configureTestingModule({
      imports: [LanguageSwitcher],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // 真实 AuthService 会拉起认证栈；这里只关心「已登录还是没登录」。
        { provide: AuthService, useValue: authService },
        { provide: AccountPreferencesService, useValue: preferences },
        ...provideTranslocoTesting(['en', 'zh-CN']),
      ],
    });
    localStorage.setItem(LanguageService.STORAGE_KEY, 'en');
    component = TestBed.createComponent(LanguageSwitcher).componentInstance;
  }

  afterEach(() => localStorage.removeItem(LanguageService.STORAGE_KEY));

  // 切换由偏好状态按写入响应完成，不在这里先切：先切的话保存失败时界面与账户就分叉了
  it('delegates an authenticated choice to the account without applying it early', async () => {
    setUp(true);

    component.select('zh-CN');
    await Promise.resolve();

    expect(preferences.write).toHaveBeenCalledWith('language', 'zh-CN');
    // 替身没有应用账户响应，本组件也不能提前切换或落盘。
    expect(localStorage.getItem(LanguageService.STORAGE_KEY)).toBe('en');
    expect(TestBed.inject(LanguageService).activeLang()).toBe('en');
  });

  it('says so when the account rejects the choice', async () => {
    setUp(true, { status: 'rejected', error: new Error('bad') });
    const error = vi.spyOn(toast, 'error').mockImplementation(() => '');

    component.select('zh-CN');

    // 不切换，界面与账户偏好就不会分叉；但必须说出来，否则用户以为存好了。
    await vi.waitFor(() => expect(error).toHaveBeenCalled());
    expect(error.mock.calls[0][0]).toBe('language.saveFailed');
  });

  // 没有可判定的响应：可能已经保存，不说成"未保存"，也不重放
  it('reports an unconfirmed save as such', async () => {
    setUp(true, { status: 'unconfirmed', error: new Error('lost') });
    const warning = vi.spyOn(toast, 'warning').mockImplementation(() => '');
    const error = vi.spyOn(toast, 'error').mockImplementation(() => '');

    component.select('zh-CN');

    await vi.waitFor(() => expect(warning).toHaveBeenCalled());
    expect(warning.mock.calls[0][0]).toBe('preferences.saveUnconfirmed');
    expect(error).not.toHaveBeenCalled();
    expect(preferences.write).toHaveBeenCalledOnce();
  });

  it('records an anonymous choice only after it becomes active and writes no setting', async () => {
    setUp(false);

    component.select('zh-CN');

    // 加载期间保留最后生效值，成功切换后才记住本次选择；访客不写账户。
    expect(localStorage.getItem(LanguageService.STORAGE_KEY)).toBe('en');
    await vi.waitFor(() => expect(TestBed.inject(LanguageService).activeLang()).toBe('zh-CN'));
    expect(localStorage.getItem(LanguageService.STORAGE_KEY)).toBe('zh-CN');
    expect(preferences.write).not.toHaveBeenCalled();
  });

  // 只支持一种语言时没有可切换的：整个切换器隐藏，不在操作区留空位
  it('hides itself when only one language is supported', () => {
    TestBed.configureTestingModule({
      imports: [LanguageSwitcher],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { isAuthenticated: () => false } },
        ...provideTranslocoTesting(['en']),
      ],
    });
    const languages = TestBed.inject(LanguageService);
    Object.defineProperty(languages, 'options', { value: [languages.options[0]] });

    const fixture = TestBed.createComponent(LanguageSwitcher);
    fixture.detectChanges();

    // 断言实际样式而非类名：两个设 display 的类同时存在时，类名在、按钮却照样显示
    const host = fixture.nativeElement as HTMLElement;
    expect(getComputedStyle(host).display).toBe('none');
    expect(host.getBoundingClientRect().width).toBe(0);
  });

  it('shows itself when more than one language is supported', () => {
    setUp(false);
    const fixture = TestBed.createComponent(LanguageSwitcher);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(getComputedStyle(host).display).toBe('inline-flex');
    expect(host.getBoundingClientRect().width).toBeGreaterThan(0);
  });
});
