import { TestBed } from '@angular/core/testing';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { toast } from '@spartan-ng/brain/sonner';

// prettier-ignore
import {
  buildSettingTree,
  SETTINGS_LOAD_ENABLED,
  SettingRowNode,
  SettingsPageState,
  shownChildren,
} from './settings-page-state';
import { applicationErrorMessage } from '../../core/errors/application-http-error';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../core/i18n/transloco.testing';
//#endif
import { SessionContextService } from '../../core/services/session-context-service';
import { SettingOutputDto } from '../../core/settings/setting.dto';

/**
 * 页面状态自己的保证：快照取会话刷新入口返回给本页的那一份，本页的读取只认最后发起的那次。会话服务用桩，
 * 由用例控制每次读取的应答顺序与失败；会话层的新旧判断由各项目自己的会话服务 spec 负责。
 */
describe('SettingsPageState', () => {
  const setting = (userValue: string | null): SettingOutputDto =>
    ({ name: 'Display.Language', userValue }) as SettingOutputDto;
  // 让读取的 Promise 链走完
  const settle = () => new Promise((resolve) => setTimeout(resolve));

  /** 会话刷新的一次读取，由用例应答。 */
  interface ManualRead {
    resolve: (settings: SettingOutputDto[]) => void;
    reject: (error: unknown) => void;
  }

  function stateWithManualReads(loadEnabled = true): {
    state: SettingsPageState;
    reads: ManualRead[];
  } {
    const reads: ManualRead[] = [];
    TestBed.configureTestingModule({
      providers: [
        SettingsPageState,
        { provide: SETTINGS_LOAD_ENABLED, useValue: loadEnabled },
        {
          provide: SessionContextService,
          useValue: {
            refreshSettings: () =>
              new Promise<SettingOutputDto[]>((resolve, reject) => reads.push({ resolve, reject })),
          },
        },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en', 'zh-CN']),
        //#endif
      ],
    });
    return { state: TestBed.inject(SettingsPageState), reads };
  }

  it('shows the settings the session refresh returns for this page', async () => {
    const { state, reads } = stateWithManualReads();

    reads[0].resolve([setting('zh-CN')]);
    await settle();

    expect(state.settings()[0].userValue).toBe('zh-CN');
    expect(state.loaded()).toBe(true);
  });

  it('ignores a stale response once a newer load has started', async () => {
    const { state, reads } = stateWithManualReads();

    state.load();
    reads[1].resolve([setting('zh-CN')]);
    await settle();
    reads[0].resolve([setting('en')]);
    await settle();

    expect(state.settings()[0].userValue).toBe('zh-CN');
  });

  // 写入后的重取被页面的新读取取代：它要等最新那次落定才返回，写入方此时丢草稿不会露出旧值
  it('settles a superseded refresh only once the latest read has landed', async () => {
    const { state, reads } = stateWithManualReads();
    reads[0].resolve([setting(null)]);
    await settle();

    let refreshed = false;
    void state.refresh().then(() => (refreshed = true));
    state.load();
    reads[1].resolve([setting('en')]);
    await settle();
    expect(refreshed).toBe(false);

    reads[2].resolve([setting('zh-CN')]);
    await vi.waitFor(() => expect(refreshed).toBe(true));
    expect(state.settings()[0].userValue).toBe('zh-CN');
  });

  // 较早的读取一直不返回、较新的已落定：较早的调用方也随之落定，写入队列不会被它卡住
  it('settles a superseded refresh with the latest read even if its own request never answers', async () => {
    const { state, reads } = stateWithManualReads();
    reads[0].resolve([setting(null)]);
    await settle();

    let refreshed = false;
    void state.refresh().then(() => (refreshed = true));
    state.load();
    reads[2].resolve([setting('zh-CN')]);

    await vi.waitFor(() => expect(refreshed).toBe(true));
    expect(state.settings()[0].userValue).toBe('zh-CN');
    expect(state.loading()).toBe(false);
  });

  // 最近一次读取失败：被它取代的调用方同样拿到失败，写入方据此在行上报错
  it('rejects a superseded refresh when the latest read fails', async () => {
    vi.spyOn(toast, 'error').mockImplementation(() => '');
    const { state, reads } = stateWithManualReads();
    reads[0].resolve([setting(null)]);
    await settle();

    const refreshed = state.refresh();
    state.load();
    const failure = new Error('boom');
    reads[2].reject(failure);

    await expect(refreshed).rejects.toBe(failure);
  });

  // 外壳判定当前用户看不到任何设置：不发请求，页面按已载入的空快照处理
  it('does not read when the shell disables loading', async () => {
    const { state, reads } = stateWithManualReads(false);

    state.load();
    await state.refresh();

    expect(reads).toHaveLength(0);
    expect(state.loaded()).toBe(true);
    expect(state.loading()).toBe(false);
    expect(state.settings()).toEqual([]);
  });

  // 文案经项目的错误映射得出：断言与映射同源，不写死原始异常
  it('reports a first-load failure as page state without a toast, and a retry clears it', async () => {
    const notify = vi.spyOn(toast, 'error').mockImplementation(() => '');
    const { state, reads } = stateWithManualReads();
    const failure = new Error('boom');

    reads[0].reject(failure);
    await settle();

    // 还没有快照可保留：页面据此显示错误态与重试，而不是"没有可配置项"
    expect(state.loadError()).toBe(applicationErrorMessage(failure));
    expect(state.loaded()).toBe(false);
    expect(notify).not.toHaveBeenCalled();

    state.load();
    reads[1].resolve([setting('en')]);
    await settle();

    expect(state.loadError()).toBeNull();
    expect(state.loaded()).toBe(true);
  });

  it('keeps the snapshot and only notifies when a later refresh fails', async () => {
    const notify = vi.spyOn(toast, 'error').mockImplementation(() => '');
    const { state, reads } = stateWithManualReads();
    reads[0].resolve([setting('en')]);
    await settle();

    state.load();
    reads[1].reject(new Error('boom'));
    await settle();

    expect(state.settings()[0].userValue).toBe('en');
    expect(state.loadError()).toBeNull();
    expect(notify).toHaveBeenCalledOnce();
  });

  // 只看最后发起的那次：被取代的较早读取还挂着时最新一次已落定，重试按钮不能一直禁用
  it('reports loading only while its latest load is pending', async () => {
    const { state, reads } = stateWithManualReads();
    state.load();
    expect(state.loading()).toBe(true);

    reads[1].resolve([setting('en')]);
    await settle();
    expect(state.loading()).toBe(false);

    reads[0].resolve([setting('en')]);
    await settle();
    expect(state.loading()).toBe(false);
  });
  //#if (IncludeLocalization)

  // 显示名由后端按请求语言本地化：换语言重取，进页面时订阅发出的当前语言不算
  it('reads again when the language changes, not for the language it starts with', async () => {
    const { reads } = stateWithManualReads();
    const transloco = TestBed.inject(TranslocoService);
    reads[0].resolve([setting('en')]);
    await settle();
    expect(reads).toHaveLength(1);

    transloco.setActiveLang(transloco.getActiveLang() === 'en' ? 'zh-CN' : 'en');
    await settle();

    expect(reads).toHaveLength(2);
  });
  //#endif
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
