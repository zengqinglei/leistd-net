import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
//#if (IncludeLocalization)
import { provideTransloco, TRANSLOCO_LOADER } from '@jsverse/transloco';
import { of } from 'rxjs';
//#endif

import { SettingContextService } from './setting-context-service';
import { SettingOutputDto } from './setting.dto';
//#if (IncludeLocalization)
import { LanguageService } from '../services/language-service';
//#endif

function setting(overrides: Partial<SettingOutputDto> = {}): SettingOutputDto {
  return {
    name: 'Display.TimeZone',
    displayName: '时区',
    group: 'Display',
    groupDisplayName: '显示',
    userValue: null,
    tenantValue: null,
    defaultValue: 'Asia/Shanghai',
    allowsTenantScope: true,
    allowsUserScope: true,
    allowsHostScope: false,
    minimum: null,
    maximum: null,
    ...overrides,
  };
}

describe('SettingContextService', () => {
  let service: SettingContextService;
  let http: HttpTestingController;

  beforeEach(() => {
    //#if (IncludeLocalization)
    // 显式定住本设备语言：不定的话初始活动语言会跟随**运行测试那台机器**的系统语言，
    // 断言 locale 初值的用例就成了环境依赖。
    localStorage.setItem(LanguageService.STORAGE_KEY, 'en');
    //#endif
    TestBed.configureTestingModule({
      //#if (IncludeLocalization)
      providers: [
        SettingContextService,
        provideHttpClient(),
        provideHttpClientTesting(),
        // 日期书写用的 locale 取自活动语言，本服务因此依赖 LanguageService；
        // 用真实 transloco 配空加载器，手写桩补不齐它依赖的内部配置。
        provideTransloco({
          config: { availableLangs: ['en', 'zh-CN'], defaultLang: 'en', fallbackLang: 'en' },
        }),
        { provide: TRANSLOCO_LOADER, useValue: { getTranslation: () => of({}) } },
      ],
      //#else
      providers: [SettingContextService, provideHttpClient(), provideHttpClientTesting()],
      //#endif
    });
    service = TestBed.inject(SettingContextService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    //#if (IncludeLocalization)
    localStorage.removeItem(LanguageService.STORAGE_KEY);
    //#endif
  });

  it('resolves values by user → tenant → default', async () => {
    const load = service.load();
    http
      .expectOne('/api/v1/settings')
      .flush([setting({ userValue: 'Asia/Tokyo', tenantValue: 'UTC' })]);
    await load;

    expect(service.timeZone()).toBe('Asia/Tokyo');
  });

  it('falls back to the tenant value, then the code default', async () => {
    const first = service.load();
    http.expectOne('/api/v1/settings').flush([setting({ tenantValue: 'UTC' })]);
    await first;
    expect(service.timeZone()).toBe('UTC');

    const second = service.load();
    http.expectOne('/api/v1/settings').flush([setting()]);
    await second;
    expect(service.timeZone()).toBe('Asia/Shanghai');
  });

  // 失败必须向上传播、且保留上一份有效快照：在这里吞掉并清空，会让「保存成功后
  // 刷新失败」表现成页面变空、时区退回浏览器，看起来像是保存本身出了问题。
  it('keeps the last good snapshot when a reload fails', async () => {
    const first = service.load();
    http.expectOne('/api/v1/settings').flush([setting({ userValue: 'Asia/Tokyo' })]);
    await first;

    const failing = service.load();
    http.expectOne('/api/v1/settings').flush('boom', { status: 500, statusText: 'Server Error' });

    await expectAsync(failing).toBeRejected();
    expect(service.timeZone()).toBe('Asia/Tokyo');
  });

  //#if (IncludeLocalization)
  /**
   * 日期书写用的 locale 取自**活动语言**，不是语言设置那一项。
   *
   * 两者不是同一个东西：设置是这份偏好的持久化，活动语言才是此刻界面正在用的那个。
   * 从快照去推导，就会出现"文案已经换了、日期还按旧地区写"——切语言时要等快照回来，
   * 写入失败时更是一直分叉，访客在登录页切的语言则根本推不出来。
   */
  it('日期 locale 跟随活动语言，与设置快照无关', async () => {
    const language = TestBed.inject(LanguageService);
    expect(service.displayLocale()).toBe('en');

    language.applyAccountLang('zh-CN');
    expect(service.displayLocale()).toBe('zh-CN');

    // 快照里那一项仍是旧值也不影响：把设置应用到活动语言上是会话上下文的事，
    // 本服务不再从快照里另算一份"当前语言"。
    const load = service.load();
    http
      .expectOne('/api/v1/settings')
      .flush([setting({ name: 'Display.Language', userValue: 'en', defaultValue: 'en' })]);
    await load;

    expect(service.displayLocale()).toBe('zh-CN');
  });

  //#endif
  it('clears the snapshot on sign-out', async () => {
    const load = service.load();
    http.expectOne('/api/v1/settings').flush([setting({ userValue: 'Asia/Tokyo' })]);
    await load;

    service.clear();

    expect(service.timeZone()).toBeUndefined();
  });
});
