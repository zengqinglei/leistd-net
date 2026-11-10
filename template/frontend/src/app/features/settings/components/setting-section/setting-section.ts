import { NgTemplateOutlet } from '@angular/common';
import {
  afterRenderEffect,
  ElementRef,
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  linkedSignal,
  signal,
} from '@angular/core';
import { FieldTree, form, FormField, validateTree, ValidationError } from '@angular/forms/signals';
//#if (IncludeLocalization)
import { TranslocoDirective, TranslocoService, translateObjectSignal } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleCheck, lucideCircleX, lucideRotateCcw } from '@ng-icons/lucide';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmComboboxImports } from '@spartan-ng/helm/combobox';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { firstValueFrom } from 'rxjs';

//#if (Email)
import { EmailTest } from './widgets/email-test/email-test';
//#endif
//#if (ExternalLogin)
import {
  EXTERNAL_LOGIN_GROUP,
  ExternalLoginProviders,
  ExternalLoginProviderSettings,
  partitionExternalLoginSettings,
} from './widgets/external-login-providers/external-login-providers';
//#endif
import { mapFormErrors } from '../../../../core/errors/form-errors';
//#if (IncludeLocalization)
import { textAt } from '../../../../core/i18n/translation-text';
//#endif
import {
  AccountPreferences,
  AccountPreferencesService,
} from '../../../../core/services/account-preferences-service';
//#if (IncludeLocalization)
import { LanguageService } from '../../../../core/services/language-service';
//#endif
import { SessionContextService } from '../../../../core/services/session-context-service';
import { ThemeService } from '../../../../core/services/theme-service';
import { SettingService } from '../../../../core/settings/setting-service';
import { SettingOutputDto } from '../../../../core/settings/setting.dto';
import { InfoHint } from '../../../../shared/components/info-hint/info-hint';
import { ComboboxRevealSelected } from '../../../../shared/directives/combobox-reveal-selected';
import { AccountPreferenceField } from '../../../../shared/dtos/account-preferences.dto';
//#if (!IncludeLocalization)
import { englishText } from '../../../../shared/utils/english-text';
//#endif
import {
  ACCOUNT_PREFERENCE_SETTINGS,
  browserTimeZone,
  SETTING_CONTROLS,
  SETTING_DEPENDENCIES,
  SETTING_DESCRIPTIONS,
  SETTING_VALUE_DESCRIPTIONS,
  SETTING_VALUE_LABELS,
  SettingControl,
  timeZoneGroups,
  timeZoneMatches,
  timeZoneOffsetLabel,
} from '../../setting-choices';
import {
  buildSettingTree,
  groupPath,
  groupSettings,
  SettingGroup,
  SettingRowNode,
  SettingScope,
  settingsInScope,
  SettingsPageState,
  shownChildren,
} from '../../settings-page-state';

/**
 * 单项设置的写入状态。`saved` 在 {@link SAVED_HINT_MS} 后回到常态；
 * `error` 保留到下一次尝试，失败必须一直可见。
 */
interface RowState {
  status: 'saving' | 'saved' | 'error';
  /** 已成文的说明（服务端给出的本地化原因）。 */
  message?: string;
  /** 本地词条键：留在界面上的文案存键，由模板按当前语言取，切换语言后跟着变。 */
  messageKey?: string;
}

/** 个人页的账户偏好分组：行由会话偏好（`/me`）提供，不取设置快照，三种部署形态一致。 */
const PREFERENCES_GROUP = 'Display';

/** 账户偏好对应的设置名（与后端设置定义同名，控件与占位规则按名字复用）→ 偏好字段。 */
const PREFERENCE_FIELDS: Readonly<Partial<Record<string, AccountPreferenceField>>> =
  Object.fromEntries(
    ACCOUNT_PREFERENCE_SETTINGS.map((preference) => [preference.name, preference.field]),
  );

/** 一个分组的渲染内容：分组本身加上按专属布局拆走之后剩下的普通行。 */
interface GroupView extends SettingGroup {
  /**
   * 按通用网格渲染的行，依赖上级的子项已挂到上级下面（见 {@link buildSettingTree}）。
   * 在专属布局分流之后挂：外部登录的开关与凭据已经分到提供方卡片里，不参与。
   */
  rows: SettingRowNode[];
  /** 账户偏好暂时读不到：分组里不出行，只说明原因（不能显示成"没有覆盖"）。 */
  preferencesUnavailable: boolean;
  /** 整组都是进程级设置：生效范围在分组顶部说一次，不在每行重复。 */
  hostScopeOnce: boolean;
  //#if (ExternalLogin)
  /** 外部登录分组按提供方拆出的卡片；其他分组为空。 */
  externalLoginProviders: ExternalLoginProviderSettings[];
  //#endif
}

/** 继承来源的文案，由模板按当前语言传入。 */
interface InheritedSourceLabels {
  followSystem: string;
  followOrganization: string;
  deploymentDefault: string;
}

/** "已保存"提示的停留时长：看得见，又不与下一次改动的状态混淆。 */
const SAVED_HINT_MS = 2000;

/**
 * "保存中"的最短可见时长：同机部署下写入几十毫秒就返回，转圈一闪而过，
 * 用户分不清"已保存"是这次点击的结果还是上次的残留。
 */
export const SAVING_MIN_MS = 400;

/**
 * 设置区块：按作用域与分组渲染设置项，逐行即改即存。
 *
 * 账户作用域写当前用户偏好，系统作用域写当前上下文的默认值（需要 App.Settings）。
 * 快照由外壳的 {@link SettingsPageState} 提供，本组件不取数。值为空表示未覆盖，
 * 继承值只作占位符展示，否则"恢复默认"看起来没有效果。
 *
 * 组件里没有具体的设置名：专用控件、候选项文案、说明与依赖都按 `setting-choices.ts` 的声明表分派。
 */
@Component({
  selector: 'app-setting-section',
  //#if (IncludeLocalization)
  imports: [
    ...HlmEmptyImports,
    FormField,
    NgIcon,
    NgTemplateOutlet,
    HlmBadge,
    HlmButton,
    HlmInput,
    HlmSpinner,
    ...HlmComboboxImports,
    ...HlmFieldImports,
    ...HlmSelectImports,
    ...HlmSwitchImports,
    ...HlmTooltipImports,
    TranslocoDirective,
    ComboboxRevealSelected,
    InfoHint,
    //#if (Email)
    EmailTest,
    //#endif
    //#if (ExternalLogin)
    ExternalLoginProviders,
    //#endif
  ],
  //#else
  imports: [
    ...HlmEmptyImports,
    FormField,
    NgIcon,
    NgTemplateOutlet,
    HlmBadge,
    HlmButton,
    HlmInput,
    HlmSpinner,
    ...HlmComboboxImports,
    ...HlmFieldImports,
    ...HlmSelectImports,
    ...HlmSwitchImports,
    ...HlmTooltipImports,
    ComboboxRevealSelected,
    InfoHint,
    //#if (Email)
    EmailTest,
    //#endif
    //#if (ExternalLogin)
    ExternalLoginProviders,
    //#endif
  ],
  //#endif
  providers: [provideIcons({ lucideCircleCheck, lucideCircleX, lucideRotateCcw })],
  templateUrl: './setting-section.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SettingSection {
  private readonly settingService = inject(SettingService);
  private readonly sessionContext = inject(SessionContextService);
  private readonly pageState = inject(SettingsPageState);
  private readonly preferences = inject(AccountPreferencesService);
  private readonly themeService = inject(ThemeService);
  //#if (IncludeLocalization)
  private readonly languageService = inject(LanguageService);
  private readonly transloco = inject(TranslocoService);
  /** 账户偏好分组与三行的显示名：整段取成对象，词条到达与语言切换时随之重算。 */
  private readonly preferenceTexts = translateObjectSignal(
    'settings.preferences',
    {},
    { scope: 'settings' },
  );
  //#else
  protected readonly t = englishText(ENGLISH);
  //#endif

  /**
   * 作用域由路由数据经输入绑定给出：个人偏好在 `/workspace/settings`，系统默认值在
   * `/platform/settings`。未声明时按账户处理，不默认打开写全租户的页面。
   */
  readonly scope = input<SettingScope>('account');

  /** 只渲染这一个分组（路由参数，URL 写法见 {@link groupPath}）；不给则渲染本作用域的全部分组。 */
  readonly group = input<string | undefined>(undefined);

  /** 不渲染的分组标识：有自己专属面板的分组（如通知偏好）从通用面板里排除。 */
  readonly exclude = input<readonly string[]>([]);

  protected readonly loading = this.pageState.loading;
  protected readonly loadError = this.pageState.loadError;

  /** 错误态里的重试：重取整页共用的快照，外壳的面板导航随之恢复。 */
  protected reload(): void {
    this.pageState.load();
  }

  /** 按行记录写入状态：用户会连续操作多个控件，整页一个状态无法把反馈落到对应行。 */
  private readonly rowState = signal<Record<string, RowState>>({});

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  constructor() {
    // aria-describedby 与输入框、下拉触发器的 aria-invalid 由 Helm 的字段关联或显式绑定给出。`hlm-switch` 与
    // `hlm-combobox-trigger` 透传了 forceInvalid，Brain 只用它算样式上的无效态，可操作元素的 aria-invalid
    // 不由它推导：只给这两种补 aria-invalid
    afterRenderEffect(() => {
      this.groupViews();
      this.rowState();
      this.serverErrors();
      // 依赖子项按上级的草稿显隐：草稿变化重建的子项同样要补上
      this.values();
      for (const row of this.host.nativeElement.querySelectorAll<HTMLElement>(
        '[data-setting-name]',
      )) {
        const control = row.querySelector<HTMLElement>(
          '[role="switch"], [data-slot="combobox-trigger"]',
        );
        if (row.dataset['settingInvalid'] === 'true') control?.setAttribute('aria-invalid', 'true');
        else control?.removeAttribute('aria-invalid');
      }
    });
  }

  /** 写入链的队尾。见 {@link write} 里为什么必须串行。 */
  private writeQueue: Promise<void> = Promise.resolve();
  /** 每行最近一次写入的序号：同一行连续写入时，只有最后一次给出这一行的状态。 */
  private readonly writeGenerations = new Map<string, number>();
  protected readonly settings = this.pageState.settings;
  private readonly labelFns = new Map<string, (value: unknown) => string>();

  /** 发信参数的面板底部给"发送测试邮件"：改完参数最想确认的就是能否收到。 */
  protected readonly showEmailTest = computed(
    () => this.scope() === 'system' && this.groups().some((group) => group.key === 'Email'),
  );
  //#if (ExternalLogin)

  /** 外部登录分组的写入成功次数：提供方卡片据此在保存或恢复默认后重取生效状态。 */
  protected readonly externalLoginRevision = signal(0);
  //#endif

  /** 分组由后端下发的分组标识驱动，前端不另列清单，否则漏登记的新设置会从界面上静默消失。 */
  protected readonly groups = computed<SettingGroup[]>(() => {
    let settings = settingsInScope(this.settings(), this.scope());
    if (this.scope() === 'account') {
      // 个人页的账户偏好取会话偏好，不取设置快照：资源服务没有这几项设置，持有方的快照也不是偏好的来源
      settings = [
        ...this.preferenceRows(),
        ...settings.filter((setting) => !(setting.name in PREFERENCE_FIELDS)),
      ];
    }
    //#if (IncludeLocalization)
    // 只支持一种语言时界面语言无从选择，不出这一行
    settings = settings.filter(
      (setting) =>
        this.controlOf(setting) !== 'language' || this.languageService.options.length > 1,
    );
    //#endif
    const groups = groupSettings(settings);
    // 偏好读不到时分组照常出现，由视图说明原因；设置也没载入成功时让位给载入失败的说明与重试
    if (this.scope() === 'account' && !this.preferences.known() && !this.loadError()) {
      groups.unshift({
        key: PREFERENCES_GROUP,
        label: this.preferenceText('group'),
        settings: [],
      });
    }
    const only = this.group();
    if (only) {
      return groups.filter((group) => groupPath(group.key) === only);
    }

    const excluded = new Set(this.exclude());
    return groups.filter((group) => !excluded.has(group.key));
  });

  /** 模板按它渲染：每个分组要走通用网格的行、是否统一说明生效范围，以及专属布局拆走的部分。 */
  protected readonly groupViews = computed<GroupView[]>(() =>
    this.groups().map((group): GroupView => {
      //#if (ExternalLogin)
      // 外部登录按提供方分卡片，开关打开了没生效时管理员要在同一张卡片上看到缺什么；不符合命名约定的
      // 设置留在通用网格里，不能静默消失。依赖只在分流后剩下的普通行上挂：提供方的开关与凭据由卡片自己排列
      const { providers, others } =
        this.scope() === 'system' && group.key === EXTERNAL_LOGIN_GROUP
          ? partitionExternalLoginSettings(group.settings)
          : { providers: [], others: group.settings };
      //#else
      const others = group.settings;
      //#endif

      return {
        ...group,
        rows: buildSettingTree(others, SETTING_DEPENDENCIES),
        preferencesUnavailable:
          this.scope() === 'account' &&
          group.key === PREFERENCES_GROUP &&
          !this.preferences.known(),
        hostScopeOnce: isHostScopeOnly(group.settings),
        //#if (ExternalLogin)
        externalLoginProviders: providers,
        //#endif
      };
    }),
  );

  /** 已在分组顶部说过生效范围的分组：其中的行不再各挂一个同样的徽章。 */
  private readonly hostScopeOnceGroups = computed(
    () => new Set(this.groupViews().flatMap((view) => (view.hostScopeOnce ? [view.key] : []))),
  );

  /**
   * 正在编辑或等待写入落定的行（键见 {@link draftKey}）：它们在表单里的值是用户的意图，快照刷新不覆盖；
   * 其余行的值跟随快照。写入落定（快照已刷新）后移出。
   */
  private readonly pending = signal<ReadonlySet<string>>(new Set());

  /**
   * 表单模型：本页每一行在本层的值（未覆盖为空串），键见 {@link draftKey}。由快照派生，正在编辑的行保留输入。
   * 控件显示本层的值而非生效值，否则租户页会显示个人偏好，保存即写成租户默认值。
   */
  private readonly values = linkedSignal<
    { settings: SettingOutputDto[]; scope: SettingScope; pending: ReadonlySet<string> },
    Record<string, string>
  >({
    source: () => ({
      settings: this.groups().flatMap((group) => group.settings),
      scope: this.scope(),
      pending: this.pending(),
    }),
    computation: ({ settings, scope, pending }, previous) => {
      const values: Record<string, string> = {};
      for (const setting of settings) {
        const key = draftKey(setting, scope);
        const kept = pending.has(key) ? previous?.value[key] : undefined;
        values[key] = kept ?? storedValueOf(setting, scope);
      }
      return values;
    },
  });

  /** 服务端按字段拒绝的错误，按行记录（键见 {@link draftKey}）；该行再次编辑或写入时清掉。 */
  private readonly serverErrors = signal<Readonly<Record<string, ValidationError.WithFieldTree[]>>>(
    {},
  );

  /**
   * 设置分区的表单：每行一个字段。输入框经 `[formField]` 绑定；下拉与开关是即改即存的 Helm 控件，
   * 经各自的值事件写回同一个模型（与对话框表单的写法一致）。客户端只有浏览器按服务端区间做的数值校验，
   * 值域以服务端为准，拒绝的字段错误经 `validateTree` 挂到该行字段上。
   */
  protected readonly settingForm = form(this.values, (path) => {
    validateTree(path, () => {
      const values = this.values();
      return Object.entries(this.serverErrors()).flatMap(([key, errors]) =>
        key in values ? errors : [],
      );
    });
  });

  /** 该行是否单独挂"全实例生效"徽章：进程级设置，且所在分组没有在顶部统一说明。 */
  protected showsRowHostScope(setting: SettingOutputDto): boolean {
    return setting.allowsHostScope && !this.hostScopeOnceGroups().has(setting.group);
  }

  /** 控件上本层的值：正在编辑的输入，否则为快照里本层的覆盖值（未覆盖为空串）。 */
  protected draftOf(setting: SettingOutputDto, scope: SettingScope): string {
    return this.values()[draftKey(setting, scope)] ?? storedValueOf(setting, scope);
  }

  protected fieldOf(setting: SettingOutputDto, scope: SettingScope): FieldTree<string> {
    return this.settingForm[draftKey(setting, scope)];
  }

  protected fieldErrorOf(setting: SettingOutputDto, scope: SettingScope): string {
    return this.fieldOf(setting, scope)()
      .errors()
      .map((error) => error.message)
      .join('\n');
  }

  protected isInvalid(setting: SettingOutputDto, scope: SettingScope): boolean {
    return (
      this.stateOf(setting, scope)?.status === 'error' || this.fieldOf(setting, scope)().invalid()
    );
  }

  private controlOf(setting: SettingOutputDto): SettingControl | undefined {
    return SETTING_CONTROLS[setting.name];
  }

  /**
   * 时区候选在字段上构造一次：要为四百多个时区构造 `Intl` formatter，结果只取决于运行时环境。
   * 不要改成模板里调用的函数，那会每轮变更检测重跑。
   */
  protected readonly timeZones = timeZoneGroups();

  private readonly timeZoneIndex = new Map(
    this.timeZones.flatMap((group) => group.zones.map((zone) => [zone.value, zone] as const)),
  );

  /** 时区用可搜索的分组下拉：四百多项无搜索难以选择，手挑短清单又会漏掉部署地。 */
  protected isTimeZone(setting: SettingOutputDto): boolean {
    return this.controlOf(setting) === 'timeZone';
  }

  /** combobox 的过滤器拿到的是选项的 value（IANA 名），按它取回选项再做匹配。 */
  protected readonly timeZoneFilter = (value: unknown, search: string): boolean => {
    const option = this.timeZoneIndex.get(String(value ?? ''));
    return option ? timeZoneMatches(option, search) : false;
  };

  /** 选项的文案取 IANA 名：存的就是它，偏移由触发器另行拼接（见 {@link timeZoneTextOf}）。 */
  protected readonly timeZoneToString = (value: unknown): string => String(value ?? '');

  /**
   * 时区在触发器上分两段写：`Asia/Shanghai · UTC+8`。放不下时只省略时区名，偏移始终可见——
   * 确认时区多半看的是偏移。选中值与继承来的占位同一写法；占位是"跟随系统"这类文案时没有偏移。
   */
  protected timeZoneTextOf(
    setting: SettingOutputDto,
    scope: SettingScope,
    labels: InheritedSourceLabels,
  ): { name: string; offset: string } {
    const name = this.displayTextOf(setting, scope, labels);
    return { name, offset: timeZoneOffsetLabel(name) };
  }

  protected stateOf(setting: SettingOutputDto, scope: SettingScope): RowState | undefined {
    return this.rowState()[draftKey(setting, scope)];
  }

  /** 后端给了候选值（`allowedValues`）的设置渲染下拉，其余渲染文本框；返回 undefined 表示没有候选项。 */
  protected choicesOf(setting: SettingOutputDto): readonly string[] | undefined {
    return setting.allowedValues?.length ? setting.allowedValues : undefined;
  }

  protected descriptionIdOf(setting: SettingOutputDto): string {
    return `${setting.name}-description`;
  }

  /**
   * 开关的 aria-describedby：`hlm-switch` 不会自动关联所在字段的描述与错误（输入框与下拉的触发器经
   * Helm 的字段关联自动带上），要显式传入本行描述，有字段错误时再加上它。
   */
  protected switchDescribedByOf(setting: SettingOutputDto, scope: SettingScope): string {
    const description = this.descriptionIdOf(setting);
    return this.fieldErrorOf(setting, scope) ? `${description} ${setting.name}-error` : description;
  }

  /** 布尔设置由服务端标注并渲染为开关；前端不另列清单，免得漏登记的项渲染成文本框。 */
  protected isBoolean(setting: SettingOutputDto): boolean {
    return setting.isBoolean === true;
  }

  /** 机密设置（加密落库）用口令框：服务端从不下发其值，框里只有本次输入。 */
  protected inputTypeOf(setting: SettingOutputDto): string {
    if (setting.isSecret) {
      return 'password';
    }
    return this.isNumeric(setting) ? 'number' : 'text';
  }

  /** 带取值区间的设置用数字输入框，并把上下界交给浏览器。 */
  protected isNumeric(setting: SettingOutputDto): boolean {
    // 按"是不是数"判定，不按"是不是 null"：服务端省掉值为 null 的属性，非数值型设置根本不带这两个字段
    return typeof setting.minimum === 'number' && typeof setting.maximum === 'number';
  }

  /**
   * 开关显示生效值（本层未覆盖时取继承值）：开关无法用占位符表达"跟随上层"。开关下的子项也按它决定
   * 显示与否，因此继承来的值、清除后的回落与未保存的草稿都算在内；只有 `'true'` 为真。
   */
  protected isChecked(setting: SettingOutputDto, scope: SettingScope): boolean {
    return (this.draftOf(setting, scope) || this.inheritedOf(setting, scope)) === 'true';
  }

  /**
   * 「跟随系统」时本设备上实际生效的值：语言、时区、外观未设置时，占位符须显示设备生效值，
   * 否则空框看着像没配置。只对个人页有意义——系统页的"跟随系统"指组织里每台设备各自决定，
   * 管理员自己这台设备的探测值不是组织的值，显示出来会误导。
   */
  protected systemDefaultOf(setting: SettingOutputDto, scope: SettingScope): string {
    if (scope !== 'account') {
      return '';
    }

    switch (this.controlOf(setting)) {
      case 'timeZone':
        return browserTimeZone() ?? '';
      case 'theme':
        return this.themeService.mode();
      //#if (IncludeLocalization)
      case 'language':
        return this.languageService.activeLang();
      //#endif
      default:
        return '';
    }
  }

  /**
   * 一个取值的展示文案，按声明表 {@link SETTING_VALUE_LABELS}：函数直接给出文案；词条前缀取 `<前缀>.<值>`，
   * 缺这条词条时显示原值；没有登记的设置显示原值。
   */
  protected labelOf(setting: SettingOutputDto, value: string): string {
    const label = SETTING_VALUE_LABELS[setting.name];
    if (typeof label === 'function') {
      return label(value);
    }
    if (label === undefined || value.length === 0) {
      return value;
    }

    const key = `${label}.${value}`;
    const text = this.text(key);
    return text === key ? value : text;
  }

  private preferenceText(key: 'group' | AccountPreferenceField): string {
    //#if (IncludeLocalization)
    // 词条未到时与模板 t 一致回落成完整键，不出空标题
    return textAt(this.preferenceTexts(), key) ?? `settings.preferences.${key}`;
    //#else
    return this.t(`settings.preferences.${key}`);
    //#endif
  }

  private text(key: string): string {
    //#if (IncludeLocalization)
    return this.transloco.translate(key);
    //#else
    return this.t(key);
    //#endif
  }

  /**
   * 账户偏好的三行，按设置行的形状给出，候选项、时区控件与占位规则与普通设置行共用。没有个人覆盖时
   * 生效值作为继承值（占位）展示，来源写"跟随组织"；生效值也没有时按设备探测值，写"跟随系统"。
   * 偏好未知时为空，分组由 {@link groups} 补上并说明原因。
   */
  private preferenceRows(): SettingOutputDto[] {
    const preferences = this.preferences.preferences();
    const overrides = this.preferences.overrides();
    if (!preferences || !overrides) {
      return [];
    }

    const groupLabel = this.preferenceText('group');
    // 行序与候选值见声明表；偏好没有代码默认值，未设置时按设备探测值
    return ACCOUNT_PREFERENCE_SETTINGS.map(({ name, field, allowedValues }): SettingOutputDto => ({
      name,
      displayName: this.preferenceText(field),
      group: PREFERENCES_GROUP,
      groupDisplayName: groupLabel,
      userValue: overrides[field],
      tenantValue: overrides[field] === null ? preferences[field] : null,
      defaultValue: null,
      allowsTenantScope: false,
      allowsUserScope: true,
      allowsHostScope: false,
      allowedValues,
      canManage: false,
    }));
  }

  /**
   * 下拉的取名函数按设置名（与界面语言）缓存：每轮新建闭包会让 `itemToString` 输入变化，触发反复重算；
   * 换了语言则要换一个新函数，选中项的文字才会跟着变。
   */
  protected labelFnOf(setting: SettingOutputDto): (value: unknown) => string {
    //#if (IncludeLocalization)
    const key = `${this.languageService.activeLang()}:${setting.name}`;
    //#else
    const key = setting.name;
    //#endif
    let fn = this.labelFns.get(key);
    if (!fn) {
      fn = (value: unknown) => this.labelOf(setting, String(value ?? ''));
      this.labelFns.set(key, fn);
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
   * 继承值的展示文案（占位符）：只写正在生效的值，有候选项时换成标签，避免同一控件上出现「中文」与
   * `zh-CN` 两种写法。值从哪来由 {@link inheritedSourceOf} 写在标签后面：两者拼在一起时，设置页的
   * 固定宽度放不下，被省略的正是值。没有继承值时看有没有"跟随系统"探测到的值，空占位符会让正在
   * 生效的值看不见；探测不到（系统页不探测管理员本机）时占位就是"跟随系统"。
   *
   * @param labels 来源文案，由模板按当前语言传入。
   */
  protected inheritedLabelOf(
    setting: SettingOutputDto,
    scope: SettingScope,
    labels: InheritedSourceLabels,
  ): string {
    const inherited = this.inheritedOf(setting, scope) || this.systemDefaultOf(setting, scope);
    if (inherited.length > 0) {
      return this.labelOf(setting, inherited);
    }

    return isDeviceDefaulted(setting) ? labels.followSystem : '';
  }

  /**
   * 语言与时区在本层未覆盖时，说清生效值从哪来（个人 → 组织 → 部署基线 → 设备）：个人页继承到的是
   * "跟随组织"（组织值或部署基线），系统页继承到的是"部署默认"，用的是设备探测值时是"跟随系统"。
   * 本层已覆盖、或占位本身已是"跟随系统"时不写。
   *
   * @param labels 来源文案，由模板按当前语言传入。
   */
  protected inheritedSourceOf(
    setting: SettingOutputDto,
    scope: SettingScope,
    labels: InheritedSourceLabels,
  ): string {
    if (!isDeviceDefaulted(setting) || this.draftOf(setting, scope).length > 0) {
      return '';
    }

    if (this.inheritedOf(setting, scope).length > 0) {
      return scope === 'account' ? labels.followOrganization : labels.deploymentDefault;
    }

    return this.systemDefaultOf(setting, scope).length > 0 ? labels.followSystem : '';
  }

  /** 触发器上正在显示的全文：本层的值或继承来的占位。放不下时控件里单行省略，悬停提示给出全文。 */
  protected displayTextOf(
    setting: SettingOutputDto,
    scope: SettingScope,
    labels: InheritedSourceLabels,
  ): string {
    const draft = this.draftOf(setting, scope);
    return draft.length > 0
      ? this.labelOf(setting, draft)
      : this.inheritedLabelOf(setting, scope, labels);
  }

  /** 输入框的值由 `[formField]` 写进表单；这里记下"正在编辑"，快照刷新不覆盖，上次的字段错误随之失效。 */
  protected onInput(setting: SettingOutputDto, scope: SettingScope): void {
    const key = draftKey(setting, scope);
    this.markPending(key);
    this.clearServerErrors(key);
  }

  /** 输入类控件在原生 `change`（失焦或回车）时提交，不按键即写：输入 60 会途经同样合法的 6。 */
  protected onCommit(setting: SettingOutputDto, scope: SettingScope, event: Event): void {
    const control = event.target as HTMLInputElement;
    const raw = control.value;
    const value = setting.isSecret ? raw : raw.trim();
    if (!control.validity.valid) {
      const key = draftKey(setting, scope);
      this.setDraft(key, value);
      this.setRowState(key, { status: 'error', message: control.validationMessage });
      return;
    }

    // 写入裁剪后的值：草稿随之同步（见 write），界面显示的就是实际存入的值。
    void this.write(setting, value.length === 0 ? null : value, scope);
  }

  /** 开关：状态本身就是提交，没有中间态。 */
  protected onToggle(setting: SettingOutputDto, scope: SettingScope, checked: boolean): void {
    void this.write(setting, checked ? 'true' : 'false', scope);
  }

  /**
   * 标签下方说明的词条键：登记了按值说明（{@link SETTING_VALUE_DESCRIPTIONS}）的按当前取值（本层的值，
   * 没有时为继承值）取 `<前缀>.<值>`，登记了固定说明的取那一句；都没有返回空串。
   */
  protected hintKeyOf(setting: SettingOutputDto, scope: SettingScope): string {
    const prefix = SETTING_VALUE_DESCRIPTIONS[setting.name];
    if (prefix === undefined) {
      return SETTING_DESCRIPTIONS[setting.name] ?? '';
    }

    const effective = this.draftOf(setting, scope) || this.systemOrInheritedValue(setting, scope);
    return effective ? `${prefix}.${effective}` : '';
  }

  /**
   * 当前显示的子项：上级此刻的取值（未保存的输入优先，没有时为继承值）满足各自的条件。
   * 收起只是不渲染（隐藏的控件不进 Tab 顺序），值不清空。
   */
  protected childrenOf(node: SettingRowNode, scope: SettingScope): SettingRowNode[] {
    return shownChildren(
      node,
      this.draftOf(node.setting, scope) || this.inheritedOf(node.setting, scope),
    );
  }

  /** 本层没设值时实际生效的那个值（继承来的，或跟随系统探测到的）。 */
  protected systemOrInheritedValue(setting: SettingOutputDto, scope: SettingScope): string {
    return this.inheritedOf(setting, scope) || this.systemDefaultOf(setting, scope);
  }

  /** 清除本层级的值，读取回落到下一层。 */
  protected reset(setting: SettingOutputDto, scope: SettingScope): void {
    void this.write(setting, null, scope);
  }

  /** 写入一项：经 {@link enqueue} 串行执行 PUT → 重取快照 → 全局发布。 */
  private write(setting: SettingOutputDto, value: string | null, scope: SettingScope): void {
    const key = draftKey(setting, scope);
    // 控件在写入期间照常可用，焦点留在原处：禁用会把焦点弹到页面上，键盘与读屏用户得从头找回来。
    // 再次操作就再排一次写入，以最后一次为准。表单里的值即最新意图：前一次写入刷新的快照不会把控件拨回旧值。
    this.setDraft(key, value ?? '');
    this.clearServerErrors(key);
    const generation = (this.writeGenerations.get(key) ?? 0) + 1;
    this.writeGenerations.set(key, generation);
    const isLatest = () => this.writeGenerations.get(key) === generation;
    this.setRowState(key, { status: 'saving' });

    // 串行：两条链并发时 GET 响应可能乱序，旧快照会覆盖新快照。
    const startedAt = Date.now();
    const preferenceField = scope === 'account' ? PREFERENCE_FIELDS[setting.name] : undefined;
    // 账户偏好在操作当下就交给偏好服务：它在这一刻记下主体，并自带串行队列。等本组件的队列轮到再交，
    // 期间主体离开（换人）时，上一个人的意图会以下一个人的会话发出。这里的队列只按序发布结果
    const preferenceWrite = preferenceField
      ? this.preferences.write(preferenceField, value as AccountPreferences[typeof preferenceField])
      : undefined;
    // 结果在轮到时才读：先挂一个处理，免得排队期间的失败被当成未处理
    preferenceWrite?.catch(() => undefined);
    this.enqueue(async () => {
      try {
        if (preferenceWrite) {
          // 账户偏好写 /me/preferences，成功时偏好状态已按响应更新，不必重取设置快照
          const outcome = await preferenceWrite;
          if (outcome.status === 'rejected') {
            throw outcome.error;
          }

          // 主体已经离开：这一项没有发出或没有应用。草稿保留，"保存中"退回常态，不显示已保存
          if (outcome.status === 'discarded') {
            if (isLatest()) {
              this.clearRowState(key);
            }
            return;
          }

          if (outcome.status === 'unconfirmed') {
            // 可能已经保存，不重放；下次重查账户偏好时对齐。草稿保留，界面不退回旧值
            await this.publishRowState(key, generation, startedAt, {
              status: 'error',
              messageKey: 'preferences.saveUnconfirmed',
            });
            return;
          }

          this.dropDraft(key, value, isLatest);
          await this.publishRowState(key, generation, startedAt, { status: 'saved' });
          return;
        }

        const request =
          scope === 'account'
            ? this.settingService.setForCurrentUser({ name: setting.name, value })
            : this.settingService.setForCurrentTenant({ name: setting.name, value });

        await firstValueFrom(request);
        //#if (ExternalLogin)
        // 写入已生效就重取状态，不等快照：快照刷新失败不影响本实例的方案目录已经变了
        if (setting.group === EXTERNAL_LOGIN_GROUP) {
          this.externalLoginRevision.update((revision) => revision + 1);
        }
        //#endif

        // 走会话上下文而非 load()：语言由设置派生，只刷新数据不会重新应用；
        // 本页的快照就是会话快照，刷新完即已更新。
        await this.sessionContext.refreshSettings();

        // 快照到手后才丢草稿，否则 PUT 成功而 GET 失败时界面会退回旧值
        this.dropDraft(key, value, isLatest);

        await this.publishRowState(key, generation, startedAt, { status: 'saved' });
      } catch (error: unknown) {
        // 失败就地报在该行且不自动消失（toast 会离开该行且易被顶掉）；上一份快照仍有效。
        // 按字段（写入请求的 `value`）拒绝的挂到该行字段上、显示在控件下方，其余原因报在行内
        //#if (IncludeLocalization)
        const fallback = this.transloco.translate('common.requestFailed');
        //#else
        const fallback = 'The request could not be completed.';
        //#endif
        const { errors, message } = mapFormErrors(
          error,
          this.settingForm,
          { value: this.fieldOf(setting, scope) },
          fallback,
        );
        await this.publishRowState(
          key,
          generation,
          startedAt,
          message === null ? null : { status: 'error', message },
          { errors, value: value ?? '' },
        );
      }
    });
  }

  /** 把一行的值写进表单并记为正在编辑：直到写入落定，快照刷新都不覆盖它。 */
  private setDraft(key: string, value: string): void {
    this.markPending(key);
    this.values.update((values) => ({ ...values, [key]: value }));
  }

  private markPending(key: string): void {
    if (!this.pending().has(key)) {
      this.pending.update((pending) => new Set(pending).add(key));
    }
  }

  private clearServerErrors(key: string): void {
    if (key in this.serverErrors()) {
      this.serverErrors.update((current) => {
        const next = { ...current };
        delete next[key];
        return next;
      });
    }
  }

  /**
   * 写入落定后该行回到跟随快照（或偏好）。只在最后一次写入、且表单里仍是本次写入的值时回去：
   * 之后又排了写入时值属于那一次（A→B→A 里两个 A 不是同一份意图）；回车提交后用户可以接着改，
   * 尚未提交的新输入也不能被抹掉。
   */
  private dropDraft(key: string, value: string | null, isLatest: () => boolean): void {
    if (!isLatest() || this.values()[key] !== (value ?? '')) {
      return;
    }

    this.pending.update((pending) => {
      const next = new Set(pending);
      next.delete(key);
      return next;
    });
  }

  /** 写入链串行化：前一条失败也要让后一条继续。 */
  private enqueue(task: () => Promise<void>): void {
    this.writeQueue = this.writeQueue.then(task, task);
  }

  private setRowState(key: string, state: RowState): void {
    this.rowState.update((current) => ({ ...current, [key]: state }));
  }

  private clearRowState(key: string): void {
    this.rowState.update((current) => {
      const next = { ...current };
      delete next[key];
      return next;
    });
  }

  /** 把"保存中"补到最短可见时长；已经够久了就立刻返回。 */
  private holdSavingState(startedAt: number): Promise<void> {
    const remaining = SAVING_MIN_MS - (Date.now() - startedAt);
    return remaining <= 0
      ? Promise.resolve()
      : new Promise<void>((resolve) => setTimeout(resolve, remaining));
  }

  /**
   * 发布一次写入的结果：这一行之后又有写入时，本次结果不再代表这一行，状态由最后一次写入给出。
   * 补足"保存中"的最短时长之后再判一次：等待期间又排进来的写入正在保存，旧结果不能盖住它。
   *
   * @param state 行状态；`null` 表示回到常态（失败原因全部挂到了字段上）。
   * @param rejected 服务端按字段拒绝的错误及被拒绝的值。只在表单里仍是这个值时挂到该行字段上：
   *   等待期间用户已经改了输入（尚未提交），旧的拒绝说的不是新值。
   */
  private async publishRowState(
    key: string,
    generation: number,
    startedAt: number,
    state: RowState | null,
    rejected?: { errors: ValidationError.WithFieldTree[]; value: string },
  ): Promise<void> {
    if (this.writeGenerations.get(key) !== generation) {
      return;
    }

    await this.holdSavingState(startedAt);
    if (this.writeGenerations.get(key) !== generation) {
      return;
    }

    if (rejected?.errors.length && this.values()[key] === rejected.value) {
      this.serverErrors.update((current) => ({ ...current, [key]: rejected.errors }));
    }
    if (state === null) {
      this.clearRowState(key);
    } else if (state.status === 'saved') {
      this.markSaved(key, generation);
    } else {
      this.setRowState(key, state);
    }
  }

  /**
   * 成功状态是暂时的：留一小会儿让人看见，然后回到常态。只清本次写入的提示：之后那次写入的提示
   * 按它自己的时长停留。
   */
  private markSaved(key: string, generation: number): void {
    this.setRowState(key, { status: 'saved' });
    setTimeout(() => {
      if (
        this.writeGenerations.get(key) === generation &&
        this.rowState()[key]?.status === 'saved'
      ) {
        this.rowState.update((current) => {
          const next = { ...current };
          delete next[key];
          return next;
        });
      }
    }, SAVED_HINT_MS);
  }
}

/** 组织与部署都没设时由设备决定的设置（语言与时区）：占位文案要说清值从哪层来。 */
function isDeviceDefaulted(setting: SettingOutputDto): boolean {
  const control = SETTING_CONTROLS[setting.name];
  return control === 'language' || control === 'timeZone';
}

/**
 * 表单与行状态的键。同一项设置在两个作用域里各有独立的值：同名共用一份会让个人偏好的未保存输入
 * 出现在系统默认值的输入框里。
 */
function draftKey(setting: SettingOutputDto, scope: SettingScope): string {
  return `${scope}:${setting.name}`;
}

/** 快照里本层的覆盖值，未覆盖为空串。进程级设置存在宿主的租户层那一行上，因此系统页同样读 tenantValue。 */
function storedValueOf(setting: SettingOutputDto, scope: SettingScope): string {
  return (scope === 'account' ? setting.userValue : setting.tenantValue) ?? '';
}

/** 整组都是进程级设置（与每行"全实例生效"徽章同一判据）；空组不算，没有行可说明。 */
function isHostScopeOnly(settings: readonly SettingOutputDto[]): boolean {
  return settings.length > 0 && settings.every((setting) => setting.allowsHostScope);
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'settings.preferences.group': 'Display',
  'settings.preferences.language': 'Language',
  'settings.preferences.timeZone': 'Time zone',
  'settings.preferences.theme': 'Appearance',
  'settings.preferences.unavailable': "Your preferences can't be loaded right now. Try again later",
  'preferences.saveUnconfirmed': "Couldn't confirm the change was saved",
  'settings.followSystem': 'Follows your system',
  'settings.followOrganization': 'Follows your organization',
  'settings.deploymentDefault': 'Deployment default',
  'settings.timeZoneSearch': 'Search city or UTC offset',
  'settings.timeZoneToggle': 'Show time zones',
  'settings.timeZoneEmpty': 'No matching time zone',
  'settings.browserTimeZone': 'browser',
  'settings.secretSet': 'Set (type a new value to replace it)',
  'settings.secretUnset': 'Not set',
  'settings.reset': 'Reset to default',
  'settings.hostScope.hint':
    'One value for the whole system; changes reach every server within about 30 seconds',
  'settings.savedEverywhere': 'Saved; reaches every server within about 30 seconds',
  'settings.saving': 'Saving…',
  'settings.saved': 'Saved',
  'settings.empty': 'No configurable items in this section',
  'settings.loadFailed': "Couldn't load settings",
  'theme.mode.light': 'Light',
  'theme.mode.system': 'System',
  'theme.mode.dark': 'Dark',
  'common.retry': 'Retry',
  'settings.logLevelHints.Trace':
    "Logs the most detail from the app's own code. Categories given their own level in the deployment configuration, such as database and web server, keep that level. For short troubleshooting only; left on, log volume grows quickly.",
  'settings.logLevelHints.Debug':
    'Logs debugging details. Turn it on while investigating; not recommended day to day.',
  'settings.logLevelHints.Information': 'Logs the normal business flow. The default level.',
  'settings.logLevelHints.Warning':
    'Logs only warnings and errors; the normal flow is not written.',
  'settings.logLevelHints.Error':
    'Logs only errors. You may miss clues that are wrong without raising an error.',
  'settings.logLevelHints.Critical':
    'Logs only failures that stop the service from working. Almost the same as turning logging off.',
  'settings.settingHints.telemetryExportEnabled':
    'Sends logs, metrics and traces over OTLP to a collector or observability platform.',
  'settings.settingHints.telemetryExportEndpoint':
    'OTLP base address, such as https://otel.example.com:4318. Logs, metrics and traces go to /v1/logs, /v1/metrics and /v1/traces under it, over http/protobuf only.',
  'settings.settingHints.telemetryExportHeaders':
    'Authentication headers for the collector or platform, written as key1=value1,key2=value2. Write-only: the saved value is never shown.',
};
//#endif
