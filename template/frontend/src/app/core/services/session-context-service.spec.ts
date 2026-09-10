import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AuthService } from './auth-service';
import { AuthorizationService } from './authorization-service';
//#if (IncludeLocalization)
import { LanguageService } from './language-service';
//#endif
import { SessionContextService } from './session-context-service';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../i18n/transloco.testing';
//#endif
import { SettingContextService } from '../settings/setting-context-service';
import { SettingOutputDto } from '../settings/setting.dto';

function timeZoneSetting(userValue: string | null): SettingOutputDto {
  return {
    name: 'Display.TimeZone',
    displayName: '时区',
    group: 'Display',
    groupDisplayName: '显示',
    userValue,
    tenantValue: null,
    defaultValue: 'Asia/Shanghai',
    allowsTenantScope: true,
    allowsUserScope: true,
    allowsHostScope: false,
    minimum: null,
    maximum: null,
  };
}
//#if (IncludeLocalization)

function languageSetting(userValue: string | null): SettingOutputDto {
  return {
    name: 'Display.Language',
    displayName: '界面语言',
    group: 'Display',
    groupDisplayName: '显示',
    userValue,
    tenantValue: null,
    defaultValue: 'en',
    allowsTenantScope: true,
    allowsUserScope: true,
    allowsHostScope: false,
    minimum: null,
    maximum: null,
  };
}
//#endif

/**
 * 会话上下文：主体确立与主体离开的唯一入口。
 *
 * 这里锁住的都是真实踩过的坑：SPA 内登录跳转不会重跑应用初始化器，所以登录成功后
 * 必须由这里把设置载入，否则保存过的显示偏好要硬刷新才生效；主体离开时权限与设置
 * 必须一起清掉，否则下一个登录的人会看到上一个人的偏好。
 */
describe('SessionContextService', () => {
  let service: SessionContextService;
  let settingContext: SettingContextService;
  let authService: jasmine.SpyObj<AuthService>;
  let http: HttpTestingController;

  beforeEach(() => {
    //#if (IncludeLocalization)
    // 显式定住本设备语言。不定的话初始语言会跟随**运行测试那台机器**的系统语言
    // （LanguageService 现在会读 navigator.languages），于是断言初始值是 'en' 的用例
    // 在系统语言为中文的机器上就红了——那不是被测行为变了，是用例依赖了环境。
    localStorage.setItem(LanguageService.STORAGE_KEY, 'en');
    //#endif
    authService = jasmine.createSpyObj<AuthService>('AuthService', ['clearAuthData']);
    TestBed.configureTestingModule({
      //#if (IncludeLocalization)
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // 真实 AuthService 会拉起认证栈（无本地身份的形态下是整个 OIDC 客户端）；
        // 用 spy 替身：既避开那条依赖链，又能断言清理确实把认证数据也带上了。
        { provide: AuthService, useValue: authService },
        ...provideTranslocoTesting(['en', 'zh-CN']),
      ],
      //#else
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // 真实 AuthService 会拉起认证栈（无本地身份的形态下是整个 OIDC 客户端）；
        // 用 spy 替身：既避开那条依赖链，又能断言清理确实把认证数据也带上了。
        { provide: AuthService, useValue: authService },
      ],
      //#endif
    });
    service = TestBed.inject(SessionContextService);
    settingContext = TestBed.inject(SettingContextService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    //#if (IncludeLocalization)
    localStorage.removeItem(LanguageService.STORAGE_KEY);
    //#endif
  });

  /**
   * 依次完成 establish() 内部的两次请求。
   *
   * 权限与设置是顺序 await，设置请求要等权限响应被消化后才发出——中间必须把控制权
   * 交回事件循环，否则 expectOne 找不到那条请求。权限那边还有一层 lastValueFrom，
   * 单个微任务不够，用一次宏任务把整条链推完。
   */
  async function flushEstablish(settings: SettingOutputDto[] | 'fail'): Promise<void> {
    http
      .expectOne('/api/v1/permissions/current')
      .flush({ permissions: [], isSuperAdmin: false, versionToken: 'v1' });
    await new Promise((resolve) => setTimeout(resolve));

    const settingsReq = http.expectOne('/api/v1/settings');
    if (settings === 'fail') {
      settingsReq.flush('boom', { status: 500, statusText: 'Server Error' });
    } else {
      settingsReq.flush(settings);
    }
  }

  it('loads permissions and settings when a subject is established', async () => {
    const establish = service.establish();
    await flushEstablish([timeZoneSetting('Asia/Tokyo')]);
    await establish;

    expect(TestBed.inject(AuthorizationService).loaded()).toBeTrue();
    expect(settingContext.timeZone()).toBe('Asia/Tokyo');
  });

  // 主体已经换人，上一份快照属于上一个用户——加载失败必须清空，不能沿用。
  it('does not carry the previous subject snapshot when the new load fails', async () => {
    const first = service.establish();
    await flushEstablish([timeZoneSetting('Asia/Tokyo')]);
    await first;
    expect(settingContext.timeZone()).toBe('Asia/Tokyo');

    const second = service.establish();
    await flushEstablish('fail');
    await second;

    expect(settingContext.timeZone()).toBeUndefined();
  });

  //#if (IncludeLocalization)
  // 语言是从设置派生的：只断言快照里有值不够，得断言界面语言真的换了。
  // 否则删掉整个语言应用逻辑，其它用例照样全绿——那正是「设了不生效」的形态。
  it('applies the language from settings', async () => {
    const languageService = TestBed.inject(LanguageService);
    expect(languageService.activeLang()).toBe('en');

    const establish = service.establish();
    await flushEstablish([languageSetting('zh-CN')]);
    await establish;

    expect(languageService.activeLang()).toBe('zh-CN');
  });

  // 设置页保存后走的是 refreshSettings()，它同样要重新应用语言——
  // 只刷新数据不应用，改完语言界面会停在旧语言。
  it('re-applies the language when settings are refreshed', async () => {
    const languageService = TestBed.inject(LanguageService);

    const refresh = service.refreshSettings();
    http.expectOne('/api/v1/settings').flush([languageSetting('zh-CN')]);
    await refresh;

    expect(languageService.activeLang()).toBe('zh-CN');
  });

  // 语言是唯一一个「清了快照也收不回来」的派生状态：它已经落在 LanguageService 上。
  // 共享机器上前一个人退出后，下一个人会在登录页看到前一个人的语言——这是最常见的形态，
  // 不需要任何请求失败。同时钉住根因：账户语言不能写进设备存储，否则两个来源分不开。
  it('reverts the language to the device preference when the subject leaves', async () => {
    localStorage.setItem(LanguageService.STORAGE_KEY, 'en');
    const languageService = TestBed.inject(LanguageService);

    try {
      const establish = service.establish();
      await flushEstablish([languageSetting('zh-CN')]);
      await establish;
      expect(languageService.activeLang()).toBe('zh-CN');
      expect(localStorage.getItem(LanguageService.STORAGE_KEY)).toBe('en');

      service.clear();

      expect(languageService.activeLang()).toBe('en');
    } finally {
      localStorage.removeItem(LanguageService.STORAGE_KEY);
    }
  });

  // 新主体的设置没加载上来时同理：快照清了，语言也得退回，不能沿用上一个人的。
  it('reverts the language when the new subject settings fail to load', async () => {
    localStorage.setItem(LanguageService.STORAGE_KEY, 'en');
    const languageService = TestBed.inject(LanguageService);

    try {
      const first = service.establish();
      await flushEstablish([languageSetting('zh-CN')]);
      await first;
      expect(languageService.activeLang()).toBe('zh-CN');

      const second = service.establish();
      await flushEstablish('fail');
      await second;

      expect(languageService.activeLang()).toBe('en');
    } finally {
      localStorage.removeItem(LanguageService.STORAGE_KEY);
    }
  });

  // 请求成功不等于拿到了可用的语言：缺项、或值是本端不认的语言（存量数据、绕过接口直写、
  // 服务端先支持了本端还没有的语言）时，这一支若什么都不做，就等于沿用上一个主体的语言——
  // 泄漏换了个门进来，而且这次连清理都没被触发。
  for (const [label, settings] of [
    ['缺少语言项', [timeZoneSetting(null)]],
    ['语言值本端不认', [languageSetting('ja')]],
  ] as const) {
    it(`reverts the language when the loaded settings carry no usable language (${label})`, async () => {
      localStorage.setItem(LanguageService.STORAGE_KEY, 'en');
      const languageService = TestBed.inject(LanguageService);

      try {
        const first = service.establish();
        await flushEstablish([languageSetting('zh-CN')]);
        await first;
        expect(languageService.activeLang()).toBe('zh-CN');

        const second = service.establish();
        await flushEstablish([...settings]);
        await second;

        expect(languageService.activeLang()).toBe('en');
      } finally {
        localStorage.removeItem(LanguageService.STORAGE_KEY);
      }
    });
  }

  //#endif
  it('clears permissions and settings when the subject leaves', async () => {
    const establish = service.establish();
    await flushEstablish([timeZoneSetting('Asia/Tokyo')]);
    await establish;

    service.clear();

    // 三样都要清掉。只断言权限和设置的话，从统一清理里删掉认证那一步不会失败。
    expect(authService.clearAuthData).toHaveBeenCalled();
    expect(TestBed.inject(AuthorizationService).loaded()).toBeFalse();
    expect(settingContext.timeZone()).toBeUndefined();
  });
});
