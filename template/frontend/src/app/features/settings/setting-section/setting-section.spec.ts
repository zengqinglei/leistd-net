import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif

import { SettingSection } from './setting-section';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../core/i18n/transloco.testing';
//#endif
import { AuthService } from '../../../core/services/auth-service';
import { SettingOutputDto } from '../../../core/settings/setting.dto';
import { SettingsPageState } from '../settings-page-state';

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
 * 用例的公共提供者。
 *
 * 设置快照来自外壳提供的 {@link SettingsPageState}，这里直接提供它：它一构造就取一次设置，
 * 所以每个用例开头都要先应答那次 GET。
 */
function pageProviders(): unknown[] {
  return [
    provideHttpClient(),
    provideHttpClientTesting(),
    provideRouter([]),
    SettingsPageState,
    // 页面经会话上下文刷新设置，那条链会碰到 AuthService；真实实现会拉起认证栈
    // （无本地身份的形态下是整个 OIDC 客户端），本组用例只关心页面的行为。
    { provide: AuthService, useValue: { clearAuthData: () => undefined } },
    //#if (IncludeLocalization)
    ...provideTranslocoTesting(['en']),
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

describe('SettingSection', () => {
  let fixture: ComponentFixture<SettingSection>;
  let http: HttpTestingController;

  const input = () =>
    fixture.nativeElement.querySelector(
      '[data-testid="setting-input-Display.FreeTextProbe"]',
    ) as HTMLInputElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [SettingSection],
      providers: [...pageProviders()],
    });
    fixture = TestBed.createComponent(SettingSection);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();

    // 构造时的首次载入
    http.expectOne('/api/v1/settings').flush([row()]);
    await fixture.whenStable();
    fixture.detectChanges();
    vi.useFakeTimers({
      toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date'],
    });
  });

  afterEach(() => {
    try {
      http.verify();
    } finally {
      try {
        fixture.destroy();
      } finally {
        vi.clearAllTimers();
        vi.useRealTimers();
      }
    }
  });

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
   * 用独立的 400 毫秒行为期望推进假时钟，不从生产常量计算边界。
   */
  async function settleSave(): Promise<void> {
    await vi.advanceTimersByTimeAsync(400);
    await fixture.whenStable();
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
    expect(input().disabled).toBe(false);
    expect(host.querySelector('hlm-spinner')).toBeTruthy();

    await flushSave();
    http.expectOne('/api/v1/settings').flush([row({ userValue: 'UTC' })]);
    await vi.advanceTimersByTimeAsync(399);
    fixture.detectChanges();
    expect(host.querySelector('hlm-spinner')).toBeTruthy();
    await vi.advanceTimersByTimeAsync(1);
    await fixture.whenStable();
    fixture.detectChanges();

    // 写完转圈消失，换成一次绿色对勾（用图标判定：hlm-spinner 自己也带 role=status）
    expect(host.querySelector('hlm-spinner')).toBeNull();
    expect(host.querySelector('ng-icon[name="lucideCircleCheck"]')).toBeTruthy();
    await vi.advanceTimersByTimeAsync(1999);
    fixture.detectChanges();
    expect(host.querySelector('ng-icon[name="lucideCircleCheck"]')).toBeTruthy();
    await vi.advanceTimersByTimeAsync(1);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(host.querySelector('ng-icon[name="lucideCircleCheck"]')).toBeNull();
  });

  /**
   * 离散控件在本行写入期间禁用，写完恢复。
   *
   * 拿每行都有的重置按钮来断言：它本身就是一次写入，在途时再点一下只会往队列里塞一条
   * 同样的写入。禁用同时也把"这一下已经收到了"说清楚——没有保存按钮之后，这是唯一的
   * "点到了"的反馈。用虚拟时间收尾，顺带钉住禁用态不会一直挂着。
   */
  it('disables the discrete controls on that row while it saves', async () => {
    const resetButton = () =>
      (fixture.nativeElement as HTMLElement).querySelector(
        '[data-testid="setting-reset-Display.FreeTextProbe"]',
      ) as HTMLButtonElement;

    await typeAndSave('UTC');
    expect(resetButton().disabled).toBe(true);

    await flushSave();
    http.expectOne('/api/v1/settings').flush([row({ userValue: 'UTC' })]);
    await settleSave();

    expect(resetButton().disabled).toBe(false);
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
    await vi.advanceTimersByTimeAsync(0);
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
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();

    // 草稿没清掉的话这里会是带空格的原始输入
    expect(input().value).toBe('UTC');
    expect(input().disabled).toBe(false);
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
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();

    expect(input().value).toBe('UTC');
    // 写入链路已结束，控件重新可用
    expect(input().disabled).toBe(false);
  });
});

/**
 * 控件按真实响应的形态判定。
 *
 * 服务端的 JSON 选项省掉值为 null 的属性：非数值型设置根本不带 `minimum` / `maximum`。只拿显式 null 的
 * 夹具测，会把"缺字段"误判成数值型、渲染成 number 框——主机名、账号这类文本一个字也输不进去。
 */
describe('SettingSection input kinds', () => {
  async function render(settings: SettingOutputDto[]): Promise<HTMLElement> {
    TestBed.configureTestingModule({
      imports: [SettingSection],
      providers: [...pageProviders()],
    });
    const fixture = TestBed.createComponent(SettingSection);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/v1/settings').flush(settings);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const inputOf = (host: HTMLElement, name: string) =>
    host.querySelector(`[data-testid="setting-input-${name}"]`) as HTMLInputElement;

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('renders a text box when the range fields are absent from the response', async () => {
    const text: SettingOutputDto = row({ name: 'Email.SmtpHost' });
    delete text.minimum;
    delete text.maximum;

    const host = await render([text]);

    expect(inputOf(host, 'Email.SmtpHost').type).toBe('text');
    expect(inputOf(host, 'Email.SmtpHost').getAttribute('inputmode')).toBeNull();
  });

  it('renders a number box with bounds when the setting has a range', async () => {
    const host = await render([row({ name: 'Email.SmtpPort', minimum: 1, maximum: 65535 })]);

    expect(inputOf(host, 'Email.SmtpPort').type).toBe('number');
    expect(inputOf(host, 'Email.SmtpPort').getAttribute('max')).toBe('65535');
  });
});

/**
 * 作用域由路由数据经组件输入给出，不是页内状态。
 *
 * 个人偏好与系统默认值写的是不同层级：作用域取错，保存下去就是把私人偏好写成了所有人的默认值——
 * 所以这里按渲染出的作用域断言。
 */
describe('SettingSection scope', () => {
  async function render(
    settings: SettingOutputDto[],
    inputs: Record<string, unknown> = {},
  ): Promise<ComponentFixture<SettingSection>> {
    TestBed.configureTestingModule({
      imports: [SettingSection],
      providers: [...pageProviders()],
    });
    const fixture = TestBed.createComponent(SettingSection);
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/v1/settings').flush(settings);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  const scoped = [
    row({ name: 'Account.Only', allowsTenantScope: false }),
    row({ name: 'System.Only', allowsUserScope: false }),
  ];

  const panel = (fixture: ComponentFixture<SettingSection>) =>
    fixture.nativeElement.querySelector('[data-testid="settings-panel"]') as HTMLElement;

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('lists only settings that allow a user override at account scope', async () => {
    const fixture = await render(scoped, { scope: 'account' });

    expect(panel(fixture).dataset['scope']).toBe('account');
    expect(hasRow(panel(fixture), 'Account.Only')).toBe(true);
    expect(hasRow(panel(fixture), 'System.Only')).toBe(false);
  });

  // 进程级设置（日志级别之类）两个层级标记都是 false：只按 allowsTenantScope 过滤
  // 会把它们全漏掉，界面上一项运维设置都看不到，而后端明明下发了。
  it('lists process-level settings at system scope as well', async () => {
    const fixture = await render(
      [
        row({
          name: 'Logging.MinimumLevel',
          allowsTenantScope: false,
          allowsUserScope: false,
          allowsHostScope: true,
        }),
      ],
      { scope: 'system' },
    );

    expect(hasRow(fixture.nativeElement as HTMLElement, 'Logging.MinimumLevel')).toBe(true);
  });

  it('lists only settings that allow a default value at system scope', async () => {
    const fixture = await render(scoped, { scope: 'system' });

    expect(panel(fixture).dataset['scope']).toBe('system');
    expect(hasRow(panel(fixture), 'System.Only')).toBe(true);
    expect(hasRow(panel(fixture), 'Account.Only')).toBe(false);
  });

  // 路由没写 scope 时按账户处理：宁可让人看到自己的偏好，也不要默认打开写全租户的那一页。
  it('falls back to account scope when no scope is declared', async () => {
    const fixture = await render(scoped);

    expect(panel(fixture).dataset['scope']).toBe('account');
  });
});

/**
 * 分组。
 *
 * 分组完全由后端下发的分组标识驱动，前端不另列一份清单——列一份的后果是新增设置忘了登记
 * 就从界面上消失，既不报错也查不出来。
 */
describe('SettingSection groups', () => {
  const grouped = [
    row({ name: 'Display.TimeZone', group: 'Display', groupDisplayName: '显示' }),
    row({ name: 'Display.Language', group: 'Display', groupDisplayName: '显示' }),
    row({ name: 'Logging.MinimumLevel', group: 'Operations', groupDisplayName: '运维' }),
    row({ name: 'Notify.Probe', group: 'NotificationPreferences', groupDisplayName: '通知' }),
  ];

  async function render(inputs: Record<string, unknown>): Promise<HTMLElement> {
    TestBed.configureTestingModule({
      imports: [SettingSection],
      providers: [...pageProviders()],
    });
    const fixture = TestBed.createComponent(SettingSection);
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/v1/settings').flush(grouped);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const sectionsOf = (host: HTMLElement) =>
    [...host.querySelectorAll<HTMLElement>('[data-testid="settings-group-section"]')].map(
      (section) => section.dataset['group'],
    );

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('renders all groups in backend order with headings when no group is given', async () => {
    const host = await render({});

    expect(sectionsOf(host)).toEqual(['Display', 'Operations', 'NotificationPreferences']);
    expect([...host.querySelectorAll('h3')].map((h) => h.textContent?.trim())).toEqual([
      '显示',
      '运维',
      '通知',
    ]);
  });

  // 系统设置的每个面板就是一个分组：URL 里是短横线写法，单组时不重复出分组标题（面板标题已经有了）
  it('renders only the specified group without a group heading', async () => {
    const host = await render({ group: 'notification-preferences' });

    expect(sectionsOf(host)).toEqual(['NotificationPreferences']);
    expect(host.querySelector('h3')).toBeNull();
  });

  // 有专属面板的分组从通用面板里排除，免得同一项设置出现在两个面板上
  it('skips excluded groups', async () => {
    const host = await render({ exclude: ['NotificationPreferences'] });

    expect(sectionsOf(host)).toEqual(['Display', 'Operations']);
  });
});

/**
 * 开关。
 *
 * 开关没有占位符，本层未覆盖时必须显示继承来的生效值：默认开启的项显示成关着，
 * 用户会以为自己没开。
 */
describe('SettingSection switches', () => {
  const switches = [
    row({ name: 'Probe.DefaultOn', isBoolean: true, defaultValue: 'true' }),
    row({ name: 'Probe.TenantOff', isBoolean: true, defaultValue: 'true', tenantValue: 'false' }),
    row({ name: 'Probe.UserOff', isBoolean: true, defaultValue: 'true', userValue: 'false' }),
  ];

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  async function checkedOf(scope: string): Promise<Record<string, string | null>> {
    TestBed.configureTestingModule({
      imports: [SettingSection],
      providers: [...pageProviders()],
    });
    const fixture = TestBed.createComponent(SettingSection);
    fixture.componentRef.setInput('scope', scope);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/v1/settings').flush(switches);
    await fixture.whenStable();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    return Object.fromEntries(
      switches.map((setting) => [
        setting.name,
        host
          .querySelector(`[data-testid="setting-switch-${setting.name}"] [role="switch"]`)
          ?.getAttribute('aria-checked') ?? null,
      ]),
    );
  }

  it('shows the inherited system or code default when the account has no override', async () => {
    expect(await checkedOf('account')).toEqual({
      'Probe.DefaultOn': 'true',
      'Probe.TenantOff': 'false',
      'Probe.UserOff': 'false',
    });
  });

  it('inherits only the code default at system scope', async () => {
    expect(await checkedOf('system')).toEqual({
      'Probe.DefaultOn': 'true',
      'Probe.TenantOff': 'false',
      'Probe.UserOff': 'true',
    });
  });
});
//#if (IncludeLocalization)

/**
 * 行内文案经模板结构指令的 t 取得。这张表单没有随语言变化的校验信号，
 * 切换语言后能换成新语言只靠结构指令重绘——文案改回组件里的 translate() 调用这里就会红。
 */
describe('SettingSection language', () => {
  it('re-renders row labels when the language changes', async () => {
    TestBed.configureTestingModule({
      imports: [SettingSection],
      // 需要第二种语言可切换；装在公共提供者之后，覆盖其中只有英文的那份配置
      providers: [...pageProviders(), ...provideTranslocoTesting(['en', 'zh-CN'])],
    });
    const fixture = TestBed.createComponent(SettingSection);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    // 可选语言变多后，语言服务按浏览器语言激活时会让设置再取一次；这里只关心文案
    http.match('/api/v1/settings').forEach((request) => request.flush([row()]));
    await fixture.whenStable();
    http.match('/api/v1/settings').forEach((request) => request.flush([row()]));
    fixture.detectChanges();

    const transloco = TestBed.inject(TranslocoService);
    transloco.setTranslation({ settings: { reset: '恢复默认' } }, 'zh-CN');
    const reset = () =>
      (fixture.nativeElement as HTMLElement).querySelector(
        '[data-testid="setting-reset-Display.FreeTextProbe"]',
      )!;
    transloco.setActiveLang('en');
    await fixture.whenStable();
    expect(reset().getAttribute('aria-label')).toBe('settings.reset');

    transloco.setActiveLang('zh-CN');
    await fixture.whenStable();

    expect(reset().getAttribute('aria-label')).toBe('恢复默认');
  });
});
//#endif
