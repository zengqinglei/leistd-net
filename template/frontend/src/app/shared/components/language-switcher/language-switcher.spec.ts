import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTransloco, TRANSLOCO_LOADER } from '@jsverse/transloco';
import { toast } from '@spartan-ng/brain/sonner';
import { of, throwError } from 'rxjs';

import { LanguageSwitcher } from './language-switcher';
import { AuthService } from '../../../core/services/auth-service';
import { LanguageService } from '../../../core/services/language-service';
import { SettingService } from '../../../core/settings/setting-service';

/**
 * 语言选择的归属：切换器是唯一决定「这次选择算谁的」的地方。
 *
 * 已登录的选择属于账户，只能写回设置；未登录的选择属于这台设备，才落本地存储。
 * 两边搞混就回到那个共享机器上的老问题：A 退出后，B 在登录页看到 A 的语言。
 * LanguageService 自己拦不住这种误用——它只是照吩咐做，所以这条边界得在调用点上钉。
 */
describe('LanguageSwitcher', () => {
  let component: LanguageSwitcher;
  let authService: jasmine.SpyObj<AuthService>;
  let settingService: jasmine.SpyObj<SettingService>;

  function setUp(authenticated: boolean): void {
    authService = jasmine.createSpyObj<AuthService>('AuthService', ['isAuthenticated']);
    authService.isAuthenticated.and.returnValue(authenticated);
    settingService = jasmine.createSpyObj<SettingService>('SettingService', ['setForCurrentUser']);
    settingService.setForCurrentUser.and.returnValue(of(undefined));

    TestBed.configureTestingModule({
      imports: [LanguageSwitcher],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // 真实 AuthService 会拉起认证栈；这里只关心「已登录还是没登录」。
        { provide: AuthService, useValue: authService },
        { provide: SettingService, useValue: settingService },
        provideTransloco({
          config: { availableLangs: ['en', 'zh-CN'], defaultLang: 'en', fallbackLang: 'en' },
        }),
        { provide: TRANSLOCO_LOADER, useValue: { getTranslation: () => of({}) } },
      ],
    });
    localStorage.setItem(LanguageService.STORAGE_KEY, 'en');
    component = TestBed.createComponent(LanguageSwitcher).componentInstance;
  }

  afterEach(() => localStorage.removeItem(LanguageService.STORAGE_KEY));

  it('writes an authenticated choice to the account without touching the device preference', () => {
    setUp(true);

    component.select('zh-CN');

    expect(settingService.setForCurrentUser).toHaveBeenCalledWith({
      name: 'Display.Language',
      value: 'zh-CN',
    });
    expect(TestBed.inject(LanguageService).activeLang()).toBe('zh-CN');
    // 落盘就分不清「这台机器的偏好」和「上一个登录者的偏好」了。
    expect(localStorage.getItem(LanguageService.STORAGE_KEY)).toBe('en');
  });

  it('says so when the account write fails, instead of swallowing it', () => {
    setUp(true);
    settingService.setForCurrentUser.and.returnValue(throwError(() => new Error('network down')));
    const error = spyOn(toast, 'error');

    component.select('zh-CN');

    // 界面已经切了，不回滚；但静默吞掉的话，用户会以为账户偏好存好了，
    // 下次登录却发现回到旧语言——那正是最难被发现的一类假保存。
    expect(TestBed.inject(LanguageService).activeLang()).toBe('zh-CN');
    expect(error).toHaveBeenCalled();
  });

  it('records an anonymous choice as the device preference and writes no setting', () => {
    setUp(false);

    component.select('zh-CN');

    // 访客没有账户可写；不落盘的话这次选择一刷新就没了。
    expect(localStorage.getItem(LanguageService.STORAGE_KEY)).toBe('zh-CN');
    expect(TestBed.inject(LanguageService).activeLang()).toBe('zh-CN');
    expect(settingService.setForCurrentUser).not.toHaveBeenCalled();
  });
});
