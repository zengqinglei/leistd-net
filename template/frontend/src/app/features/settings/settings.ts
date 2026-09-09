// prettier-ignore
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  //#if (IncludeLocalization)
  effect,
  //#endif
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleCheck, lucideCircleX, lucideRotateCcw } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmComboboxImports } from '@spartan-ng/helm/combobox';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmSeparator } from '@spartan-ng/helm/separator';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { catchError, EMPTY, finalize, firstValueFrom, map } from 'rxjs';

// prettier-ignore
import {
  BOOLEAN_SETTINGS,
  browserTimeZone,
  LOG_LEVEL_DESCRIPTIONS,
  SETTING_CHOICES,
  SettingChoice,
  timeZoneGroups,
  timeZoneMatches,
} from './setting-choices';
import { applicationErrorMessage } from '../../core/errors/application-http-error';
//#if (IncludeLocalization)
import { translationReady } from '../../core/i18n/translation-ready';
//#endif
//#if (IncludeLocalization)
import { LanguageService } from '../../core/services/language-service';
//#endif
import { SessionContextService } from '../../core/services/session-context-service';
import { SettingService } from '../../core/settings/setting-service';
import { SETTINGS } from '../../core/settings/setting.constants';
import { SettingOutputDto } from '../../core/settings/setting.dto';
import { LayoutService } from '../../layout/services/layout-service';

/**
 * 设置作用域。
 *
 * `account` 写当前用户自己的偏好，`system` 写当前上下文的默认值——登录后租户已经确定，
 * 这里写的就是「本租户（或宿主）下所有人的默认值」，不是在管理别的租户；
 * 给指定租户配置属于租户管理的事。两者的写入授权也不同，因此分开。
 */
type SettingScope = 'account' | 'system';

/**
 * 单项设置的写入状态。
 *
 * `saved` 是暂时的（{@link SAVED_HINT_MS} 后自动回到常态），`error` 不自动消失——
 * 一次失败的保存必须一直看得见，直到下一次尝试把它替换掉。
 */
interface RowState {
  status: 'saving' | 'saved' | 'error';
  message?: string;
}

/**
 * 左侧一个分类，以及归到它下面的设置项。
 *
 * 显式命名而不是让它从 computed 里推断出来：`activeGroup` 在一项设置都没有时是
 * `undefined`（`groups[0]` 在关掉 noUncheckedIndexedAccess 的配置下推断成非空），
 * 不标出来的话模板里那些 `?.` 与 `?? []` 会被编译器判成"多余的空值处理"而报诊断，
 * 而它们其实是必需的。
 */
interface SettingGroup {
  key: string;
  label: string;
  settings: SettingOutputDto[];
}

/** "已保存"提示的停留时长。够看见，又不至于长到和下一次改动的状态混在一起。 */
const SAVED_HINT_MS = 2000;

/**
 * "保存中"的最短可见时长。
 *
 * 同机部署下一次写入常常几十毫秒就回来了，转圈一闪而过，用户看到的只是描述行末尾
 * 忽然多了个"已保存"——分不清那是这次点击的结果，还是上一次操作的残留。撑到看得清，
 * 这个反馈才成立；也顺带保证禁用态不会短到像抖了一下。
 */
export const SAVING_MIN_MS = 400;

/**
 * 设置页。
 *
 * 左侧分组、右侧表单。账户页写当前用户偏好（任何登录用户可改），
 * 系统页写当前上下文的默认值（需要 App.Settings）。
 *
 * 值为空即表示"未覆盖"，界面以占位符展示继承来的值，避免把继承值渲染成
 * 用户自己设过的值——那会让"恢复默认"看起来没有效果。
 */
@Component({
  selector: 'app-settings',
  //#if (IncludeLocalization)
  imports: [
    NgIcon,
    HlmButton,
    HlmInput,
    HlmSeparator,
    HlmSpinner,
    ...HlmComboboxImports,
    ...HlmFieldImports,
    ...HlmSelectImports,
    ...HlmSwitchImports,
    ...HlmTooltipImports,
    TranslocoModule,
  ],
  //#else
  imports: [
    NgIcon,
    HlmButton,
    HlmInput,
    HlmSeparator,
    HlmSpinner,
    ...HlmComboboxImports,
    ...HlmFieldImports,
    ...HlmSelectImports,
    ...HlmSwitchImports,
    ...HlmTooltipImports,
  ],
  //#endif
  providers: [provideIcons({ lucideCircleCheck, lucideCircleX, lucideRotateCcw })],
  templateUrl: './settings.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Settings {
  private readonly route = inject(ActivatedRoute);
  private readonly settingService = inject(SettingService);
  private readonly sessionContext = inject(SessionContextService);
  private readonly layoutService = inject(LayoutService);
  //#if (IncludeLocalization)
  private readonly languageService = inject(LanguageService);
  //#endif
  private readonly destroyRef = inject(DestroyRef);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  private readonly translationReady = translationReady(this.transloco);
  //#endif

  /**
   * 本页的作用域由**路由**决定，不是页内状态。
   *
   * 账户偏好在 `/workspace/settings`、系统默认值在 `/platform/settings`：两者的读者、
   * 权限与影响范围都不同，混在一个页面用 Tab 切换，会让人把私人偏好当成租户默认值来改
   * （反之亦然）。作用域跟着区域走，导航也就交回各区的侧边栏。
   */
  protected readonly scope = computed<SettingScope>(() =>
    this.routeScope() === 'system' ? 'system' : 'account',
  );

  private readonly routeScope = toSignal(
    this.route.data.pipe(map((data) => data['scope'] as string | undefined)),
    { initialValue: this.route.snapshot.data['scope'] as string | undefined },
  );
  protected readonly loading = signal(false);

  /**
   * 每一项自己的写入状态。
   *
   * 刻意是**按行**的，不是整页一个：没有保存按钮之后，用户会连着点好几个控件，
   * 整页一个状态只记得住最后一项，而反馈也就落不到用户刚碰的那一行上。
   */
  private readonly rowState = signal<Record<string, RowState>>({});

  /** 写入链的队尾。见 {@link write} 里为什么必须串行。 */
  private writeQueue: Promise<void> = Promise.resolve();
  protected readonly settings = signal<SettingOutputDto[]>([]);
  protected readonly drafts = signal<Record<string, string>>({});
  private readonly labelFns = new Map<string, (value: unknown) => string>();

  /** 账户页只列允许用户覆盖的设置。 */
  protected readonly accountSettings = computed(() =>
    this.settings().filter((s) => s.allowsUserScope),
  );

  /**
   * 系统页列允许在当前上下文上给默认值的设置，以及进程级设置。
   *
   * 进程级设置（日志级别之类）两个层级标记都是 false，只按 `allowsTenantScope` 过滤
   * 会把它们全漏掉——后端只在宿主上下文下发它们，所以能看到就代表能改。
   */
  protected readonly systemSettings = computed(() =>
    this.settings().filter((s) => s.allowsTenantScope || s.allowsHostScope),
  );

  /** 本作用域下要显示的设置。 */
  private readonly scopedSettings = computed(() =>
    this.scope() === 'account' ? this.accountSettings() : this.systemSettings(),
  );

  /**
   * 按分组切分，分组标识与顺序都来自后端。
   *
   * 分类不在前端另列一份：漏登记一项设置的后果是它从界面上消失，而这既不报错也查不出来。
   * 后端已经把未分组的归入 `Other`，所以这里不需要再兜一次底。
   */
  protected readonly groups = computed<SettingGroup[]>(() => {
    const byKey = new Map<string, SettingGroup>();
    for (const setting of this.scopedSettings()) {
      const group = byKey.get(setting.group) ?? {
        key: setting.group,
        label: setting.groupDisplayName || setting.group,
        settings: [],
      };
      group.settings.push(setting);
      byKey.set(setting.group, group);
    }

    return [...byKey.values()];
  });

  /** 左侧选中的分组；`null` 表示还没选过，取第一组。 */
  private readonly selectedGroupKey = signal<string | null>(null);

  /**
   * 当前分组。
   *
   * 选中项按**标识**记，而不是按下标：切换作用域或换语言后分组集合会变，
   * 记下标会莫名跳到另一类去。选中的那一组不在了就回到第一组。
   */
  protected readonly activeGroup = computed<SettingGroup | undefined>(() => {
    const groups = this.groups();
    const selected = this.selectedGroupKey();
    return groups.find((group) => group.key === selected) ?? groups[0];
  });

  protected selectGroup(key: string): void {
    this.selectedGroupKey.set(key);
  }

  //#if (IncludeLocalization)
  // 重置按钮只有图标，可访问名称必须显式给出，否则读屏软件只能念出"按钮"。
  protected readonly resetLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('settings.reset');
  });

  protected readonly panelTitle = computed(() => {
    this.translationReady();
    return this.transloco.translate(`settings.${this.scope()}`);
  });

  protected readonly panelDescription = computed(() => {
    this.translationReady();
    return this.transloco.translate(`settings.${this.scope()}Description`);
  });

  protected readonly timeZoneSearchLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('settings.timeZoneSearch');
  });

  protected readonly timeZoneEmptyLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('settings.timeZoneEmpty');
  });

  protected readonly browserTimeZoneLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('settings.browserTimeZone');
  });

  // 左侧分类是一组按钮而不是链接，可访问名称必须显式给出
  protected readonly groupsLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('settings.groups');
  });

  protected readonly followSystemLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('settings.followSystem');
  });

  protected readonly hostScopeHint = computed(() => {
    this.translationReady();
    return this.transloco.translate('settings.hostScopeHint');
  });

  protected readonly savedLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('settings.saved');
  });

  protected readonly savingLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('settings.saving');
  });
  //#else
  protected readonly resetLabel = computed(() => 'Reset to default');

  protected readonly panelTitle = computed(() =>
    this.scope() === 'account' ? 'Account' : 'System',
  );

  protected readonly panelDescription = computed(() =>
    this.scope() === 'account'
      ? 'Applies to you only; leave blank to inherit the system default'
      : 'Defaults for all users; each user may override them',
  );

  protected readonly timeZoneSearchLabel = computed(() => 'Search city or UTC offset');
  protected readonly timeZoneEmptyLabel = computed(() => 'No matching time zone');
  protected readonly browserTimeZoneLabel = computed(() => 'browser');
  protected readonly groupsLabel = computed(() => 'Setting categories');
  protected readonly followSystemLabel = computed(() => 'Follows your system');
  protected readonly hostScopeHint = computed(() => 'Applies to all instances (~30s)');
  protected readonly savedLabel = computed(() => 'Saved');
  protected readonly savingLabel = computed(() => 'Saving…');
  //#endif

  constructor() {
    // 面包屑末级文案由页面自行设置，与其他平台页保持同一约定。
    //#if (IncludeLocalization)
    effect(() => {
      this.translationReady();
      this.layoutService.title.set(this.transloco.translate('settings.title'));
    });
    //#else
    this.layoutService.title.set('Settings');
    //#endif

    this.load();
    //#if (IncludeLocalization)

    // 设置项的 displayName 由**后端**按请求语言本地化（见后端 SettingDefinitionProvider：
    // 按 `Setting:{name}` 查词条）。切换语言只重绘视图没用——手里那份 DTO 仍是旧语言文案，
    // 必须重新取一次让服务端按新语言渲染。
    //
    // 用 langChanges$ 而不是 translationReady：这里要的是"语言真的换了"，
    // 而 translationReady 在首帧词条到达时也会发射。
    //
    // langChanges$ 自身也会在订阅那一刻发出**当前**语言（它背后是 BehaviorSubject），
    // 所以要跟上一次见到的语言比一下：不比就等于每次进页面都白发一次请求
    // （上面刚 load 过），而那次多余的请求还会盖在首次结果之后。
    let seenLang = this.transloco.getActiveLang();
    this.transloco.langChanges$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((lang) => {
      if (lang === seenLang) {
        return;
      }

      seenLang = lang;
      this.load();
    });
    //#endif
  }

  /**
   * 输入框显示的是**本层的覆盖值**，不是回落后的生效值。
   *
   * 两者混用会让租户页显示出当前用户的个人偏好，保存即把私人偏好写成了租户默认值。
   */
  protected draftOf(setting: SettingOutputDto, scope: SettingScope): string {
    const draft = this.drafts()[this.draftKey(setting, scope)];
    if (draft !== undefined) {
      return draft;
    }

    // 进程级设置存在宿主的租户层那一行上，因此这里同样读 tenantValue
    return (scope === 'account' ? setting.userValue : setting.tenantValue) ?? '';
  }

  /**
   * 时区候选在组件实例内**构造一次**。
   *
   * 它要为四百多个时区各构造几个 `Intl` formatter，而结果只取决于运行时环境
   * （可用时区、当前偏移），不随任何页内状态变化，所以放在字段上初始化一次即可。
   * 不要改成模板里直接调用的函数——那会每轮变更检测重跑一次。
   */
  protected readonly timeZones = timeZoneGroups();

  private readonly timeZoneIndex = new Map(
    this.timeZones.flatMap((group) => group.zones.map((zone) => [zone.value, zone] as const)),
  );

  /**
   * 时区用可搜索的分组下拉，不用普通下拉。
   *
   * 四百多项没有搜索只能靠滚，比手敲还难用；而手挑一份短清单必然漏掉某些部署地。
   */
  protected isTimeZone(setting: SettingOutputDto): boolean {
    return setting.name === SETTINGS.display.timeZone;
  }

  /** combobox 的过滤器拿到的是选项的 value（IANA 名），按它取回选项再做匹配。 */
  protected readonly timeZoneFilter = (value: unknown, search: string): boolean => {
    const option = this.timeZoneIndex.get(String(value ?? ''));
    return option ? timeZoneMatches(option, search) : false;
  };

  /** 选中项在触发器上的文案：带上偏移，省得为了确认选对了再点开一次。 */
  protected readonly timeZoneToString = (value: unknown): string => {
    const id = String(value ?? '');
    const option = this.timeZoneIndex.get(id);
    return option && option.offsetLabel ? `${option.value} · ${option.offsetLabel}` : id;
  };

  /** 这一项当前的写入状态；没有即常态。 */
  protected stateOf(setting: SettingOutputDto, scope: SettingScope): RowState | undefined {
    return this.rowState()[this.draftKey(setting, scope)];
  }

  /**
   * 这一项正在写入。
   *
   * 只用来禁用**离散**控件（开关、下拉、重置）：它们点一下就结束，写入在飞的时候再点
   * 不表达任何新意图，禁用同时也把"这一下已经收到了"说清楚。文本与数字框不跟着禁用，
   * 原因见模板里那处注释。
   */
  protected isRowSaving(setting: SettingOutputDto, scope: SettingScope): boolean {
    return this.stateOf(setting, scope)?.status === 'saving';
  }

  /** 取值封闭的设置渲染下拉，其余渲染文本框；返回 undefined 表示没有候选项。 */
  protected choicesOf(setting: SettingOutputDto): readonly SettingChoice[] | undefined {
    return SETTING_CHOICES[setting.name];
  }

  /** 真值只有两种的设置用开关，而不是两项下拉：一次点击 vs 两次。 */
  protected isBoolean(setting: SettingOutputDto): boolean {
    return BOOLEAN_SETTINGS.has(setting.name);
  }

  /** 带取值区间的设置用数字输入框，并把上下界交给浏览器。 */
  protected isNumeric(setting: SettingOutputDto): boolean {
    return setting.minimum !== null && setting.maximum !== null;
  }

  protected isChecked(setting: SettingOutputDto, scope: SettingScope): boolean {
    return this.draftOf(setting, scope) === 'true';
  }

  /**
   * 「跟随系统」时实际生效的值。
   *
   * 语言与时区没有租户默认值也没有代码默认值——留空即跟随系统。占位符必须把探测到的那个值
   * 显示出来：不然一个空输入框看着像"没配"，而它其实正在生效。
   */
  protected systemDefaultOf(setting: SettingOutputDto): string {
    if (setting.name === SETTINGS.display.timeZone) {
      return browserTimeZone() ?? '';
    }

    //#if (IncludeLocalization)
    if (setting.name === SETTINGS.display.language) {
      return this.languageService.activeLang();
    }

    //#endif
    return '';
  }

  /** 候选项的展示文案；没有匹配项时退回原始值。 */
  protected labelOf(setting: SettingOutputDto, value: string): string {
    return this.choicesOf(setting)?.find((c) => c.value === value)?.label ?? value;
  }

  /**
   * 下拉展示用的取名函数，按设置名缓存。
   *
   * 每次变更检测新建闭包会让 `itemToString` 这个 signal input 每轮都变，
   * 触发下拉反复重算。
   */
  protected labelFnOf(setting: SettingOutputDto): (value: unknown) => string {
    let fn = this.labelFns.get(setting.name);
    if (!fn) {
      fn = (value: unknown) => this.labelOf(setting, String(value ?? ''));
      this.labelFns.set(setting.name, fn);
    }

    return fn;
  }

  /** 下拉选中即写入：下拉没有"编辑中"的中间态，再要求点保存只是多一步。 */
  protected onSelect(
    setting: SettingOutputDto,
    scope: SettingScope,
    value: string | null | undefined,
  ): void {
    // 值没变就不写：spartan 的 select 在重新渲染时也会发一次 valueChange。
    if ((value ?? '') === this.draftOf(setting, scope)) {
      return;
    }

    void this.write(setting, value ? value : null, scope);
  }

  /** 本层未覆盖时以占位符展示继承来的值：账户继承系统默认值，系统继承代码默认值。 */
  protected inheritedOf(setting: SettingOutputDto, scope: SettingScope): string {
    const inherited =
      scope === 'account' ? (setting.tenantValue ?? setting.defaultValue) : setting.defaultValue;
    return inherited ?? '';
  }

  /**
   * 继承值的展示文案：有候选项时换成候选项的标签。
   *
   * 占位符直接显示原始值会让下拉里挑「中文」、没选中时却显示 <c>zh-CN</c>，
   * 同一个值在同一个控件上两种写法。
   */
  protected inheritedLabelOf(setting: SettingOutputDto, scope: SettingScope): string {
    const inherited = this.inheritedOf(setting, scope);
    if (inherited.length > 0) {
      return this.labelOf(setting, inherited);
    }

    // 没有继承值时看有没有"跟随系统"探测到的值：空占位符会让正在生效的值看不见。
    const system = this.systemDefaultOf(setting);
    return system.length === 0
      ? ''
      : `${this.followSystemLabel()} · ${this.labelOf(setting, system)}`;
  }

  protected onInput(setting: SettingOutputDto, scope: SettingScope, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.drafts.update((drafts) => ({ ...drafts, [this.draftKey(setting, scope)]: value }));
  }

  /**
   * 输入类控件的**提交**：原生 `change` 在失焦或按下回车时触发，正好是"这次输入结束了"。
   *
   * 刻意不在每次按键时写：输入 60 会途经 6，而 6 也是合法值——按键即写等于把一串
   * 中间状态依次存进去并生效。
   */
  protected onCommit(setting: SettingOutputDto, scope: SettingScope, event: Event): void {
    const value = (event.target as HTMLInputElement).value.trim();

    // 草稿同步成裁剪后的值，两件事都靠它：界面立刻显示"真正存进去的那个值"，
    // 而写入完成后的清草稿是按值比对的（见 write），草稿留着未裁剪的原文就永远对不上、
    // 于是永远清不掉。
    this.drafts.update((drafts) => ({ ...drafts, [this.draftKey(setting, scope)]: value }));
    void this.write(setting, value.length === 0 ? null : value, scope);
  }

  /** 开关：状态本身就是提交，没有中间态。 */
  protected onToggle(setting: SettingOutputDto, scope: SettingScope, checked: boolean): void {
    void this.write(setting, checked ? 'true' : 'false', scope);
  }

  /** 选中的日志级别的说明；不是日志级别或认不出的取值返回空串。 */
  protected levelDescriptionOf(setting: SettingOutputDto, scope: SettingScope): string {
    if (
      setting.name !== SETTINGS.logging.minimumLevel &&
      setting.name !== SETTINGS.logging.requestLevel
    ) {
      return '';
    }

    const effective = this.draftOf(setting, scope) || this.systemOrInheritedValue(setting, scope);
    return LOG_LEVEL_DESCRIPTIONS[effective] ?? '';
  }

  /** 本层没设值时实际生效的那个值（继承来的，或跟随系统探测到的）。 */
  protected systemOrInheritedValue(setting: SettingOutputDto, scope: SettingScope): string {
    return this.inheritedOf(setting, scope) || this.systemDefaultOf(setting);
  }

  // 同一项设置在两个页签里各有独立草稿：同名共用一份会让账户页的未保存输入
  // 出现在租户页的输入框里。
  private draftKey(setting: SettingOutputDto, scope: SettingScope): string {
    return `${scope}:${setting.name}`;
  }

  /** 清除本层级的值，读取回落到下一层。 */
  protected reset(setting: SettingOutputDto, scope: SettingScope): void {
    void this.write(setting, null, scope);
  }

  /**
   * 一次写一项，整条 PUT → GET → 发布快照 结束前不接受新的写入。
   *
   * 页面上的设置项个数是个位数，不值得为并发写入引入队列；而放任并发会踩三个坑：
   * 保存中状态只记得住最后一项、任一请求返回就把状态清掉、多个刷新响应乱序时旧快照
   * 盖掉新快照。用一个 single-flight 状态把这三件事一次挡掉。
   */
  private write(setting: SettingOutputDto, value: string | null, scope: SettingScope): void {
    const key = this.draftKey(setting, scope);
    this.setRowState(key, { status: 'saving' });

    // 排到队尾而不是直接发：写入不是单个 PUT，而是 PUT → 重取快照 → 全局发布一整条链。
    // 两条链并发时两个 GET 的响应可能乱序，旧快照会盖掉新快照——那是正确性问题。
    // 串行保证顺序，并且只有本行的离散控件被禁用，别的设置项照样能改。
    const startedAt = Date.now();
    this.enqueue(async () => {
      try {
        const request =
          scope === 'account'
            ? this.settingService.setForCurrentUser({ name: setting.name, value })
            : this.settingService.setForCurrentTenant({ name: setting.name, value });

        await firstValueFrom(request);

        // 走会话上下文而不是直接 load()：语言是从设置派生的，只刷新数据不重新应用，
        // 改完语言界面会停在旧语言。同时那份快照直接用来刷新本页列表——
        // 各自请求一次不仅浪费，还会在其中一次失败时让本页和全局的显示对不上。
        this.settings.set([...(await this.sessionContext.refreshSettings())]);

        // 草稿留到快照到手之后再丢：先丢的话，PUT 成功而这次 GET 失败时，
        // 界面会退回上一份快照显示旧值，而库里其实已经改了。
        //
        // 只在草稿仍是本次写入的那个值时才丢：用户按回车提交后光标还在输入框里，
        // 完全可以接着改。无条件清掉会把这期间的新输入抹掉——原来靠禁用控件挡这件事，
        // 而禁用会让整页看着卡住，用条件清除更精确。
        this.drafts.update((drafts) => {
          if (drafts[key] !== (value ?? '')) {
            return drafts;
          }

          const next = { ...drafts };
          delete next[key];
          return next;
        });

        await this.holdSavingState(startedAt);
        this.markSaved(key);
      } catch (error: unknown) {
        // 失败就地报在那一行上，并且**不自动消失**：改一项失败必须被看见，
        // 而 toast 会把反馈从用户刚碰的那一行挪到屏幕角落，还容易被下一条顶掉。
        // 界面保持原样（上一份快照仍然有效），不清空成一张白页。
        await this.holdSavingState(startedAt);
        this.setRowState(key, { status: 'error', message: applicationErrorMessage(error) });
      }
    });
  }

  /** 写入链串行化：前一条失败也要让后一条继续。 */
  private enqueue(task: () => Promise<void>): void {
    this.writeQueue = this.writeQueue.then(task, task);
  }

  private setRowState(key: string, state: RowState): void {
    this.rowState.update((current) => ({ ...current, [key]: state }));
  }

  /** 把"保存中"补到最短可见时长；已经够久了就立刻返回。 */
  private holdSavingState(startedAt: number): Promise<void> {
    const remaining = SAVING_MIN_MS - (Date.now() - startedAt);
    return remaining <= 0
      ? Promise.resolve()
      : new Promise<void>((resolve) => setTimeout(resolve, remaining));
  }

  /** 成功状态是暂时的：留一小会儿让人看见，然后回到常态。 */
  private markSaved(key: string): void {
    this.setRowState(key, { status: 'saved' });
    setTimeout(() => {
      if (this.rowState()[key]?.status === 'saved') {
        this.rowState.update((current) => {
          const next = { ...current };
          delete next[key];
          return next;
        });
      }
    }, SAVED_HINT_MS);
  }

  private load(): void {
    this.loading.set(true);
    this.settingService
      .getSettings()
      .pipe(
        catchError((error: unknown) => {
          toast.error(applicationErrorMessage(error));
          return EMPTY;
        }),
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((settings) => this.settings.set(settings));
  }
}
