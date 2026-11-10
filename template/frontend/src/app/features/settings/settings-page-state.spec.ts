import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { toast } from '@spartan-ng/brain/sonner';
import { Subject } from 'rxjs';

// prettier-ignore
import {
  buildSettingTree,
  SettingRowNode,
  SettingsPageState,
  shownChildren,
} from './settings-page-state';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../core/i18n/transloco.testing';
//#endif
import { AuthService } from '../../core/services/auth-service';
//#if (IncludeLocalization)
import { LanguageService } from '../../core/services/language-service';
//#endif
import { SessionContextService } from '../../core/services/session-context-service';
import { SettingService } from '../../core/settings/setting-service';
import { SettingOutputDto } from '../../core/settings/setting.dto';

/**
 * 设置页的快照就是会话快照：页面读取与会话刷新经同一个带代次保护的入口，
 * 晚到的旧响应不能覆盖较新的值，页外的写入（页头切换语言）刷新后本页随之更新。
 */
describe('SettingsPageState', () => {
  const setting = (userValue: string | null): SettingOutputDto =>
    ({ name: 'Display.Language', userValue }) as SettingOutputDto;
  // 让读取的 Promise 链走完
  const settle = () => new Promise((resolve) => setTimeout(resolve));

  /** 每次读取都返回一个由用例应答的响应。 */
  function stateWithManualResponses(): {
    state: SettingsPageState;
    responses: Subject<SettingOutputDto[]>[];
  } {
    const responses: Subject<SettingOutputDto[]>[] = [];
    TestBed.configureTestingModule({
      providers: [
        SettingsPageState,
        // 页面读取走会话刷新入口；真实 AuthService 会拉起认证栈，这里只需要清理入口
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { clearAuthData: () => undefined } },
        {
          provide: SettingService,
          useValue: {
            getSettings: () => {
              const response = new Subject<SettingOutputDto[]>();
              responses.push(response);
              return response;
            },
          },
        },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en', 'zh-CN']),
        //#endif
      ],
    });
    return { state: TestBed.inject(SettingsPageState), responses };
  }

  it('ignores a stale response once a newer load has started', async () => {
    const { state, responses } = stateWithManualResponses();

    state.load();
    responses[1].next([setting('zh-CN')]);
    await settle();
    responses[0].next([setting('en')]);
    await settle();

    expect(state.settings()[0].userValue).toBe('zh-CN');
  });

  // 页外的写入（如页头切换语言）刷新了会话快照：本页跟上，即使活动语言没变、不会触发重取
  it('follows a session refresh once the page has loaded', async () => {
    const { state, responses } = stateWithManualResponses();
    responses[0].next([setting(null)]);
    await settle();

    const refresh = TestBed.inject(SessionContextService).refreshSettings();
    responses[1].next([setting('en')]);
    await refresh;

    expect(state.settings()[0].userValue).toBe('en');
  });

  // 本页还有一次读取在途时会话刷新先回来：那次读取晚到也不能把旧值写回
  it('keeps a newer session refresh when an earlier page load answers late', async () => {
    const { state, responses } = stateWithManualResponses();
    responses[0].next([setting(null)]);
    await settle();

    state.load();
    const refresh = TestBed.inject(SessionContextService).refreshSettings();
    responses[2].next([setting('en')]);
    await refresh;
    responses[1].next([setting(null)]);
    await settle();

    expect(state.settings()[0].userValue).toBe('en');
  });

  // 首次读取还没回来时会话已刷新：首次读取晚到不能盖掉它，页面仍算载入完成
  it('keeps a session refresh that lands before the first page load answers', async () => {
    const { state, responses } = stateWithManualResponses();

    const refresh = TestBed.inject(SessionContextService).refreshSettings();
    responses[1].next([setting('en')]);
    await refresh;
    responses[0].next([setting(null)]);
    await settle();

    expect(state.settings()[0].userValue).toBe('en');
    expect(state.loaded()).toBe(true);
  });
  //#if (IncludeLocalization)

  // 页头切换语言：切换器激活新语言并发出会话刷新 A；语言一变，本页按新语言重取 B。
  // A 先返回也只是被 B 取代，不能把派生的界面语言改回旧值；最后生效的是最新快照里的语言
  it('ends on the saved language when a language switch and the page refetch overlap', async () => {
    const { state, responses } = stateWithManualResponses();
    const languageService = TestBed.inject(LanguageService);
    responses[0].next([setting('en')]);
    await settle();

    void languageService.applyAccountLang('zh-CN');
    const switcherRefresh = TestBed.inject(SessionContextService).refreshSettings();
    state.load();
    responses[1].next([setting('zh-CN')]);
    await switcherRefresh;
    responses[2].next([setting('zh-CN')]);
    await settle();
    // 新语言生效后本页还会按它再重取一次；服务端此时存的就是中文
    responses.slice(3).forEach((response) => response.next([setting('zh-CN')]));
    await settle();

    await vi.waitFor(() => expect(languageService.activeLang()).toBe('zh-CN'));
    expect(state.settings()[0].userValue).toBe('zh-CN');
  });
  //#endif

  it('reports a first-load failure as page state without a toast, and a retry clears it', async () => {
    const notify = vi.spyOn(toast, 'error').mockImplementation(() => '');
    const { state, responses } = stateWithManualResponses();

    responses[0].error(new Error('boom'));
    await settle();

    // 还没有快照可保留：页面据此显示错误态与重试，而不是"没有可配置项"
    expect(state.loadError()).toBe('boom');
    expect(state.loaded()).toBe(false);
    expect(notify).not.toHaveBeenCalled();

    state.load();
    responses[1].next([setting('en')]);
    await settle();

    expect(state.loadError()).toBeNull();
    expect(state.loaded()).toBe(true);
  });

  it('keeps the snapshot and only notifies when a later refresh fails', async () => {
    const notify = vi.spyOn(toast, 'error').mockImplementation(() => '');
    const { state, responses } = stateWithManualResponses();
    responses[0].next([setting('en')]);
    await settle();

    state.load();
    responses[1].error(new Error('boom'));
    await settle();

    expect(state.settings()[0].userValue).toBe('en');
    expect(state.loadError()).toBeNull();
    expect(notify).toHaveBeenCalledOnce();
  });

  it('reports loading only while one of its loads is pending', async () => {
    const { state, responses } = stateWithManualResponses();
    state.load();
    expect(state.loading()).toBe(true);

    responses[1].next([setting('en')]);
    await settle();
    expect(state.loading()).toBe(true);

    responses[0].next([setting('en')]);
    await settle();
    expect(state.loading()).toBe(false);
  });
});

/**
 * 依赖树：显式依赖（声明表）与命名约定（`{前缀}.Enabled` 的布尔设置管着名字以 `{前缀}.` 开头的其他设置）
 * 合成同一棵树。
 */
describe('buildSettingTree', () => {
  const setting = (name: string, isBoolean = false): SettingOutputDto =>
    ({ name, isBoolean }) as SettingOutputDto;
  const shape = (nodes: SettingRowNode[]): unknown[] =>
    nodes.map((node) =>
      node.children.length > 0 ? { [node.setting.name]: shape(node.children) } : node.setting.name,
    );

  it('puts settings with the same prefix under the switch, in delivered order', () => {
    expect(
      shape(
        buildSettingTree(
          [
            setting('Registration.CaptchaExpiryMinutes'),
            setting('Registration.Enabled', true),
            setting('Registration.EnableEmailVerification', true),
            setting('Security.RequireTwoFactor', true),
          ],
          {},
        ),
      ),
    ).toEqual([
      {
        'Registration.Enabled': [
          'Registration.CaptchaExpiryMinutes',
          'Registration.EnableEmailVerification',
        ],
      },
      'Security.RequireTwoFactor',
    ]);
  });

  it('nests under the longest matching switch', () => {
    expect(
      shape(
        buildSettingTree(
          [
            setting('Telemetry.Enabled', true),
            setting('Telemetry.Export.Enabled', true),
            setting('Telemetry.Export.Endpoint'),
            setting('Telemetry.SamplingRatio'),
          ],
          {},
        ),
      ),
    ).toEqual([
      {
        'Telemetry.Enabled': [
          { 'Telemetry.Export.Enabled': ['Telemetry.Export.Endpoint'] },
          'Telemetry.SamplingRatio',
        ],
      },
    ]);
  });

  it('leaves rows without a matching boolean switch at the top level', () => {
    expect(
      shape(
        buildSettingTree(
          [
            // 不是布尔：不算开关
            setting('Audit.Enabled'),
            setting('Audit.RetentionDays'),
            // 后缀不是整段 `.Enabled`
            setting('Audit.RetentionEnabled', true),
            // 前缀按整段匹配，RegistrationPolicy 不属于 Registration
            setting('Registration.Enabled', true),
            setting('RegistrationPolicy.Mode'),
          ],
          {},
        ),
      ),
    ).toEqual([
      'Audit.Enabled',
      'Audit.RetentionDays',
      'Audit.RetentionEnabled',
      'Registration.Enabled',
      'RegistrationPolicy.Mode',
    ]);
  });

  const notOff = (value: string) => value.length > 0 && value !== 'off';

  it('puts a setting under its explicit parent with that condition, wherever it is delivered', () => {
    const tree = buildSettingTree(
      [setting('Reports.Archive.Days'), setting('Reports.Mode'), setting('Reports.Owner')],
      { 'Reports.Archive.Days': { parent: 'Reports.Mode', shownWhen: notOff } },
    );

    expect(shape(tree)).toEqual([{ 'Reports.Mode': ['Reports.Archive.Days'] }, 'Reports.Owner']);
    expect(shownChildren(tree[0], 'monthly').map((node) => node.setting.name)).toEqual([
      'Reports.Archive.Days',
    ]);
    expect(shownChildren(tree[0], 'off')).toEqual([]);
  });

  it('combines explicit and convention dependencies over several levels', () => {
    const tree = buildSettingTree(
      [
        setting('Guard.Enabled', true),
        setting('Guard.Mode'),
        setting('Guard.Limits.PerHour'),
        setting('Guard.Fallback.Enabled', true),
        setting('Guard.Fallback.Total'),
      ],
      {
        'Guard.Limits.PerHour': { parent: 'Guard.Mode', shownWhen: notOff },
        'Guard.Fallback.Enabled': { parent: 'Guard.Mode', shownWhen: notOff },
      },
    );

    expect(shape(tree)).toEqual([
      {
        'Guard.Enabled': [
          {
            'Guard.Mode': [
              'Guard.Limits.PerHour',
              { 'Guard.Fallback.Enabled': ['Guard.Fallback.Total'] },
            ],
          },
        ],
      },
    ]);
    // 约定挂接的条件是上级为真
    const fallback = tree[0].children[0].children[1];
    expect(shownChildren(fallback, 'true')).toHaveLength(1);
    expect(shownChildren(fallback, 'false')).toEqual([]);
  });

  // 同前缀的开关在页面上也不改挂：声明了显式依赖就只认声明的上级
  it('keeps a setting at the top level when its explicit parent is not on the page', () => {
    expect(
      shape(
        buildSettingTree(
          [
            setting('Reports.Enabled', true),
            setting('Reports.Archive.Days'),
            setting('Reports.Owner'),
          ],
          { 'Reports.Archive.Days': { parent: 'Reports.Mode', shownWhen: notOff } },
        ),
      ),
    ).toEqual([{ 'Reports.Enabled': ['Reports.Owner'] }, 'Reports.Archive.Days']);
  });

  it('lets an explicit dependency win over the naming convention', () => {
    const tree = buildSettingTree(
      [setting('Guard.Enabled', true), setting('Guard.Mode'), setting('Guard.Window')],
      { 'Guard.Window': { parent: 'Guard.Mode', shownWhen: notOff } },
    );

    expect(shape(tree)).toEqual([{ 'Guard.Enabled': [{ 'Guard.Mode': ['Guard.Window'] }] }]);
    expect(shownChildren(tree[0].children[0], 'strict')).toHaveLength(1);
    expect(shownChildren(tree[0].children[0], 'off')).toEqual([]);
  });
});
