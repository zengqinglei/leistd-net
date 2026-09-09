import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
//#if (IncludeLocalization)
import { provideTransloco, TRANSLOCO_LOADER } from '@jsverse/transloco';
//#endif
import { of } from 'rxjs';

import { SAVING_MIN_MS, Settings } from './settings';
import { AuthService } from '../../core/services/auth-service';
import { SettingOutputDto } from '../../core/settings/setting.dto';

/**
 * 这些用例验的是**取值开放**的设置那条链路（文本框 + 保存按钮）。
 *
 * 刻意用一个不在 `SETTING_CHOICES` 里的设置名：登记了候选项的设置渲染成下拉，
 * 没有输入框也没有独立保存键。拿 `Display.TimeZone` 之类的枚举型设置来验这条路，
 * 会在它某天登记候选项时整批变红——而那不是这条链路出了问题。
 */
function row(overrides: Partial<SettingOutputDto> = {}): SettingOutputDto {
  return {
    name: 'Display.FreeTextProbe',
    displayName: '自由文本设置',
    group: 'Other',
    groupDisplayName: '其他',
    userValue: null,
    tenantValue: null,
    defaultValue: 'probe-default',
    allowsTenantScope: true,
    allowsUserScope: true,
    allowsHostScope: false,
    minimum: null,
    maximum: null,
    ...overrides,
  };
}

/**
 * 保存链路的异步状态。
 *
 * 这一段是上一轮真的出过问题的地方：写入在途时输入没禁用、草稿在 PUT 一完成就删除，
 * 于是「保存后继续输入会被吞掉」和「PUT 成功但刷新失败时界面显示旧值」两个坑同时存在，
 * 而它们都不会让任何既有用例变红。这里按行为断言，而不是断言实现细节。
 */
/**
 * 页面用例的公共提供者。
 *
 * `provideRouter([])` 不只是为了路由跳转：本页的作用域取自 `ActivatedRoute.data`，
 * 缺了它组件连注入都过不去。
 */
function pageProviders(): unknown[] {
  return [
    provideHttpClient(),
    provideHttpClientTesting(),
    provideRouter([]),
    // 页面经会话上下文刷新设置，那条链会碰到 AuthService；真实实现会拉起认证栈
    // （无本地身份的形态下是整个 OIDC 客户端），本组用例只关心页面的行为。
    { provide: AuthService, useValue: { clearAuthData: () => undefined } },
    //#if (IncludeLocalization)
    provideTransloco({
      config: { availableLangs: ['en'], defaultLang: 'en', fallbackLang: 'en' },
    }),
    { provide: TRANSLOCO_LOADER, useValue: { getTranslation: () => Promise.resolve({}) } },
    //#endif
  ];
}

/**
 * 某一项设置在当前面板上渲染出来了。
 *
 * 按每行常驻的重置按钮判定，而不是找页面文本里的设置名——设置名是给开发看的标识，
 * 界面上刻意不显示（显示它会像是在解释这一项的含义）。按文本断言的用例会随文案改动
 * 一起红，而那时并不是"这一项没渲染"。
 */
function hasRow(host: HTMLElement, name: string): boolean {
  return host.querySelector(`[data-testid="setting-reset-${name}"]`) !== null;
}

describe('Settings page', () => {
  let fixture: ComponentFixture<Settings>;
  let http: HttpTestingController;

  const input = () =>
    fixture.nativeElement.querySelector(
      '[data-testid="setting-input-Display.FreeTextProbe"]',
    ) as HTMLInputElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [Settings],
      providers: [...pageProviders()],
    });
    fixture = TestBed.createComponent(Settings);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();

    // 构造时的首次载入
    http.expectOne('/api/v1/settings').flush([row()]);
    await fixture.whenStable();
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  /**
   * 输入并提交。
   *
   * 没有保存按钮了：输入类控件在原生 `change`（失焦 / 回车）时提交。
   * 用 `input` 事件先更新草稿，再用 `change` 提交，和真人操作的顺序一致。
   */
  async function typeAndSave(value: string): Promise<void> {
    const field = input();
    field.value = value;
    field.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    field.dispatchEvent(new Event('change'));
    // 写入排进串行队列，请求要等一个微任务才发出——同步 expectOne 会扑空
    await fixture.whenStable();
    fixture.detectChanges();
  }

  /**
   * 等到"保存中"的最短可见时长过去。
   *
   * 成功与失败态都刻意压在这个时长之后才落下（见 SAVING_MIN_MS：本地几十毫秒返回时
   * 转圈一闪而过，反馈就等于没有）。断言终态前必须把这段真时间等掉。
   */
  async function settleSave(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve, SAVING_MIN_MS + 50));
    fixture.detectChanges();
  }

  /** 完成写入后的 PUT，并把控制权交回微任务队列，让随后的 GET 真正发出。 */
  async function flushSave(): Promise<void> {
    http.expectOne('/api/v1/settings/current-user').flush(null);
    await fixture.whenStable();
  }

  // 写入期间那一行要有状态，而输入框**不跟着禁用**：提交发生在失焦或回车时，
  // 光标往往还在框里，禁用会当场把焦点弹走、接着输入的字也丢了。
  it('shows a saving state on that row without freezing the text input', async () => {
    await typeAndSave('UTC');

    const host = fixture.nativeElement as HTMLElement;
    expect(input().disabled).toBeFalse();
    expect(host.querySelector('hlm-spinner')).toBeTruthy();

    await flushSave();
    http.expectOne('/api/v1/settings').flush([row({ userValue: 'UTC' })]);
    // 串行队列是裸 Promise 链，Angular 不跟踪它；whenStable 不会等它的续体，
    // 得让出真实时间
    await settleSave();

    // 写完转圈消失，换成一次绿色对勾（用图标判定：hlm-spinner 自己也带 role=status）
    expect(host.querySelector('hlm-spinner')).toBeNull();
    expect(host.querySelector('ng-icon[name="lucideCircleCheck"]')).toBeTruthy();
  });

  /**
   * 离散控件在本行写入期间禁用，写完恢复。
   *
   * 拿每行都有的重置按钮来断言：它本身就是一次写入，在途时再点一下只会往队列里塞一条
   * 同样的写入。禁用同时也把"这一下已经收到了"说清楚——没有保存按钮之后，这是唯一的
   * "点到了"的反馈。用真实时长收尾，顺带钉住禁用态不会一直挂着。
   */
  it('disables the discrete controls on that row while it saves', async () => {
    const resetButton = () =>
      (fixture.nativeElement as HTMLElement).querySelector(
        '[data-testid="setting-reset-Display.FreeTextProbe"]',
      ) as HTMLButtonElement;

    await typeAndSave('UTC');
    expect(resetButton().disabled).toBeTrue();

    await flushSave();
    http.expectOne('/api/v1/settings').flush([row({ userValue: 'UTC' })]);
    await settleSave();

    expect(resetButton().disabled).toBeFalse();
  });

  // 原来靠禁用控件挡住的那件事：按回车提交后光标还在输入框里，用户接着改，
  // 而先前那次写入完成时会清草稿——无条件清掉就把新输入抹掉了。
  it('keeps newer input when an earlier save completes', async () => {
    await typeAndSave('UTC');

    // 写入在途，用户接着改（还没提交）
    const field = input();
    field.value = 'Asia/Tokyo';
    field.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    await flushSave();
    http.expectOne('/api/v1/settings').flush([row({ userValue: 'UTC' })]);
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();

    // 新输入必须还在：清草稿只该清掉"本次写入的那个值"
    expect(input().value).toBe('Asia/Tokyo');
  });

  // 草稿输入带空格、服务端存的是裁剪后的值：只有草稿真的被清掉，输入框才会显示
  // 服务端那份。两边都用同一个字符串的话，草稿留着也看不出来，这条用例会静默变绿。
  it('clears the draft only after the refreshed snapshot arrives', async () => {
    await typeAndSave('  UTC  ');

    const put = http.expectOne('/api/v1/settings/current-user');
    expect(put.request.body).toEqual({ name: 'Display.FreeTextProbe', value: 'UTC' });
    put.flush(null);
    await fixture.whenStable();

    http.expectOne('/api/v1/settings').flush([row({ userValue: 'UTC' })]);
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();

    // 草稿没清掉的话这里会是带空格的原始输入
    expect(input().value).toBe('UTC');
    expect(input().disabled).toBeFalse();
  });

  // 失败就地报在那一行上，不弹 toast：反馈要落在用户刚碰的那个控件下面。
  it('reports a failed save inline on the row', async () => {
    await typeAndSave('UTC');
    http
      .expectOne('/api/v1/settings/current-user')
      .flush('boom', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();
    await settleSave();

    const alert = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
    expect(alert).toBeTruthy();
    // 红叉之外必须带上原因：只有一个叉，用户既不知道是值不合法还是没权限，也就无从修正
    expect(alert?.querySelector('ng-icon[name="lucideCircleX"]')).toBeTruthy();
    expect(alert?.textContent?.trim().length).toBeGreaterThan(0);
  });

  // PUT 成功、刷新失败：库里已经是新值，界面不能退回旧值。
  it('keeps the typed value when the refresh after a successful save fails', async () => {
    await typeAndSave('UTC');
    await flushSave();
    http.expectOne('/api/v1/settings').flush('boom', { status: 500, statusText: 'Server Error' });
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();

    expect(input().value).toBe('UTC');
    // 写入链路已结束，控件重新可用
    expect(input().disabled).toBeFalse();
  });
});

/**
 * 作用域来自**路由**，不是页内状态。
 *
 * 账户页与系统页写的是不同层级：一个是私人偏好，一个是全租户默认值。作用域取错，
 * 保存下去就是把私人偏好写成了所有人的默认值——所以这里按渲染出的作用域断言，
 * 而不是断言页面上还有没有切换控件。
 */
describe('Settings page scope', () => {
  function stubRoute(scope?: string): unknown {
    const data = scope === undefined ? {} : { scope };
    return { provide: ActivatedRoute, useValue: { data: of(data), snapshot: { data } } };
  }

  async function render(scope?: string): Promise<ComponentFixture<Settings>> {
    TestBed.configureTestingModule({
      imports: [Settings],
      providers: [...pageProviders(), stubRoute(scope)],
    });
    const fixture = TestBed.createComponent(Settings);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http
      .expectOne('/api/v1/settings')
      .flush([
        row({ name: 'Account.Only', allowsTenantScope: false }),
        row({ name: 'System.Only', allowsUserScope: false }),
      ]);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  const panel = (fixture: ComponentFixture<Settings>) =>
    fixture.nativeElement.querySelector('[data-testid="settings-panel"]') as HTMLElement;

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('renders the account scope on /workspace/settings', async () => {
    const fixture = await render('account');

    expect(panel(fixture).dataset['scope']).toBe('account');
    expect(hasRow(panel(fixture), 'Account.Only')).toBeTrue();
    expect(hasRow(panel(fixture), 'System.Only')).toBeFalse();
  });

  // 进程级设置（日志级别之类）两个层级标记都是 false：只按 allowsTenantScope 过滤
  // 会把它们全漏掉，界面上一项日志设置都看不到，而后端明明下发了。
  it('系统页也列出进程级设置', async () => {
    TestBed.configureTestingModule({
      imports: [Settings],
      providers: [...pageProviders(), stubRoute('system')],
    });
    const fixture = TestBed.createComponent(Settings);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/settings')
      .flush([
        row({
          name: 'Logging.MinimumLevel',
          allowsTenantScope: false,
          allowsUserScope: false,
          allowsHostScope: true,
        }),
      ]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(hasRow(fixture.nativeElement as HTMLElement, 'Logging.MinimumLevel')).toBeTrue();
  });

  it('renders the system scope on /platform/settings', async () => {
    const fixture = await render('system');

    expect(panel(fixture).dataset['scope']).toBe('system');
    expect(hasRow(panel(fixture), 'System.Only')).toBeTrue();
    expect(hasRow(panel(fixture), 'Account.Only')).toBeFalse();
  });

  // 路由没写 scope 时按账户处理：宁可让人看到自己的偏好，也不要默认打开写全租户的那一页。
  it('falls back to the account scope when the route declares none', async () => {
    const fixture = await render();

    expect(panel(fixture).dataset['scope']).toBe('account');
  });
});
/**
 * 左侧分类。
 *
 * 分类完全由后端下发的分组标识驱动，前端不另列一份清单——列一份的后果是新增设置忘了登记
 * 就从界面上消失，既不报错也查不出来。
 */
describe('Settings page groups', () => {
  function grouped(): SettingOutputDto[] {
    return [
      row({ name: 'Display.TimeZone', group: 'Display', groupDisplayName: '显示' }),
      row({ name: 'Display.Language', group: 'Display', groupDisplayName: '显示' }),
      row({ name: 'Logging.MinimumLevel', group: 'Logging', groupDisplayName: '日志' }),
    ];
  }

  async function render(): Promise<ComponentFixture<Settings>> {
    TestBed.configureTestingModule({
      imports: [Settings],
      providers: [...pageProviders()],
    });
    const fixture = TestBed.createComponent(Settings);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/v1/settings').flush(grouped());
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  const host = (fixture: ComponentFixture<Settings>) => fixture.nativeElement as HTMLElement;
  const panelGroup = (fixture: ComponentFixture<Settings>) =>
    (host(fixture).querySelector('[data-testid="settings-panel"]') as HTMLElement).dataset['group'];

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('按后端分组渲染左侧分类，并默认选中第一组', async () => {
    const fixture = await render();

    const labels = [...host(fixture).querySelectorAll('[data-testid^="settings-group-"]')].map(
      (button) => (button.textContent ?? '').trim(),
    );

    expect(labels).toEqual(['显示', '日志']);
    expect(panelGroup(fixture)).toBe('Display');
    // 右侧只列当前分组的设置
    expect(host(fixture).querySelectorAll('hlm-field').length).toBe(2);
  });

  it('点另一个分类只换右侧内容', async () => {
    const fixture = await render();

    (
      host(fixture).querySelector('[data-testid="settings-group-Logging"]') as HTMLButtonElement
    ).click();
    fixture.detectChanges();

    expect(panelGroup(fixture)).toBe('Logging');
    expect(host(fixture).querySelectorAll('hlm-field').length).toBe(1);
    expect(hasRow(host(fixture), 'Logging.MinimumLevel')).toBeTrue();
  });
});
