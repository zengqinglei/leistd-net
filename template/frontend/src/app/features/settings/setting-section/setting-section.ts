import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleCheck, lucideCircleX, lucideRotateCcw } from '@ng-icons/lucide';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmComboboxImports } from '@spartan-ng/helm/combobox';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { firstValueFrom } from 'rxjs';

import { applicationErrorMessage } from '../../../core/errors/application-http-error';
//#if (IncludeLocalization)
import { translationReady } from '../../../core/i18n/translation-ready';
//#endif
//#if (IncludeLocalization)
import { LanguageService } from '../../../core/services/language-service';
//#endif
import { SessionContextService } from '../../../core/services/session-context-service';
import { SettingService } from '../../../core/settings/setting-service';
import { SETTINGS } from '../../../core/settings/setting.constants';
import { SettingOutputDto } from '../../../core/settings/setting.dto';
//#if (LocalIdentity)
import { EmailTest } from '../email-test/email-test';
//#endif
// prettier-ignore
import {
  browserTimeZone,
  LOG_LEVEL_DESCRIPTIONS,
  SETTING_CHOICES,
  SettingChoice,
  timeZoneGroups,
  timeZoneMatches,
} from '../setting-choices';
// prettier-ignore
import {
  groupPath,
  groupSettings,
  SettingGroup,
  SettingScope,
  settingsInScope,
  SettingsPageState,
} from '../settings-page-state';

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
 * 设置区块：按作用域与分组渲染一组设置项，逐行即改即存。
 *
 * 个人设置的「偏好」面板与系统设置的每个面板都用它。账户作用域写当前用户偏好（任何登录用户可改），
 * 系统作用域写当前上下文的默认值（需要 App.Settings）。设置快照来自外壳提供的
 * {@link SettingsPageState}，本组件不自己取数。
 *
 * 值为空即表示"未覆盖"，界面以占位符展示继承来的值，避免把继承值渲染成
 * 用户自己设过的值——那会让"恢复默认"看起来没有效果。
 */
@Component({
  selector: 'app-setting-section',
  //#if (IncludeLocalization)
  imports: [
    NgIcon,
    HlmBadge,
    HlmButton,
    HlmInput,
    HlmSpinner,
    ...HlmComboboxImports,
    ...HlmFieldImports,
    ...HlmSelectImports,
    ...HlmSwitchImports,
    ...HlmTooltipImports,
    TranslocoModule,
    //#if (LocalIdentity)
    EmailTest,
    //#endif
  ],
  //#else
  imports: [
    NgIcon,
    HlmBadge,
    HlmButton,
    HlmInput,
    HlmSpinner,
    ...HlmComboboxImports,
    ...HlmFieldImports,
    ...HlmSelectImports,
    ...HlmSwitchImports,
    ...HlmTooltipImports,
    //#if (LocalIdentity)
    EmailTest,
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
  //#if (IncludeLocalization)
  private readonly languageService = inject(LanguageService);
  //#endif
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  private readonly translationReady = translationReady(this.transloco);
  //#endif

  /**
   * 作用域由**路由数据**给出（经组件输入绑定），不是页内状态。
   *
   * 个人偏好在 `/workspace/settings`、系统默认值在 `/platform/settings`：两者的读者、权限与影响范围
   * 都不同，混在一处会让人把私人偏好当成租户默认值来改（反之亦然）。没声明时按账户处理：
   * 宁可让人看到自己的偏好，也不要默认打开写全租户的那一页。
   */
  readonly scope = input<SettingScope>('account');

  /** 只渲染这一个分组（路由参数，URL 写法见 {@link groupPath}）；不给则渲染本作用域的全部分组。 */
  readonly group = input<string | undefined>(undefined);

  /** 不渲染的分组标识：有自己专属面板的分组（如通知偏好）从通用面板里排除。 */
  readonly exclude = input<readonly string[]>([]);

  protected readonly loading = this.pageState.loading;

  /**
   * 每一项自己的写入状态。
   *
   * 刻意是**按行**的，不是整页一个：没有保存按钮之后，用户会连着点好几个控件，
   * 整页一个状态只记得住最后一项，而反馈也就落不到用户刚碰的那一行上。
   */
  private readonly rowState = signal<Record<string, RowState>>({});

  /** 写入链的队尾。见 {@link write} 里为什么必须串行。 */
  private writeQueue: Promise<void> = Promise.resolve();
  protected readonly settings = this.pageState.settings;
  protected readonly drafts = signal<Record<string, string>>({});
  private readonly labelFns = new Map<string, (value: unknown) => string>();

  /**
   * 本区块要渲染的分组。
   *
   * 分组完全由后端下发的分组标识驱动，前端不另列一份清单——列一份的后果是新增设置忘了登记
   * 就从界面上消失，既不报错也查不出来。
   */
  /** 发信参数的面板底部给"发送测试邮件"：改完参数最想确认的就是"现在收不收得到"。 */
  protected readonly showEmailTest = computed(
    () => this.scope() === 'system' && this.groups().some((group) => group.key === 'Email'),
  );

  protected readonly groups = computed<SettingGroup[]>(() => {
    const groups = groupSettings(settingsInScope(this.settings(), this.scope()));
    const only = this.group();
    if (only) {
      return groups.filter((group) => groupPath(group.key) === only);
    }

    const excluded = new Set(this.exclude());
    return groups.filter((group) => !excluded.has(group.key));
  });

  //#if (IncludeLocalization)
  // 重置按钮只有图标，可访问名称必须显式给出，否则读屏软件只能念出"按钮"。
  protected readonly resetLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('settings.reset');
  });

  protected readonly timeZoneSearchLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('settings.timeZoneSearch');
  });

  protected readonly timeZoneEmptyLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('settings.timeZoneEmpty');
  });

  protected readonly timeZoneToggleLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('settings.timeZoneToggle');
  });

  protected readonly browserTimeZoneLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('settings.browserTimeZone');
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

  protected readonly secretSetLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('settings.secretSet');
  });

  protected readonly secretUnsetLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('settings.secretUnset');
  });
  //#else
  protected readonly resetLabel = computed(() => 'Reset to default');

  protected readonly timeZoneSearchLabel = computed(() => 'Search city or UTC offset');
  protected readonly timeZoneEmptyLabel = computed(() => 'No matching time zone');
  protected readonly timeZoneToggleLabel = computed(() => 'Show time zones');
  protected readonly browserTimeZoneLabel = computed(() => 'browser');
  protected readonly followSystemLabel = computed(() => 'Follows your system');
  protected readonly hostScopeHint = computed(() => 'Applies to all instances (~30s)');
  protected readonly savedLabel = computed(() => 'Saved');
  protected readonly savingLabel = computed(() => 'Saving…');
  protected readonly secretSetLabel = computed(() => 'Set (type a new value to replace it)');
  protected readonly secretUnsetLabel = computed(() => 'Not set');
  //#endif

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

  /**
   * 真值只有两种的设置用开关，而不是两项下拉：一次点击 vs 两次。
   * 由服务端标注：前端不另列清单，漏登记的布尔设置就不会渲染成要手打 true 的文本框。
   */
  protected isBoolean(setting: SettingOutputDto): boolean {
    return setting.isBoolean === true;
  }

  /**
   * 输入框类型。机密设置（加密落库）用口令框：服务端从不下发它的值，框里只会是这次新输入的内容。
   */
  protected inputTypeOf(setting: SettingOutputDto): string {
    if (setting.isSecret) {
      return 'password';
    }
    return this.isNumeric(setting) ? 'number' : 'text';
  }

  /** 占位符：机密设置只说"设过没有"，其余显示继承来的值或系统默认值。 */
  protected placeholderOf(setting: SettingOutputDto, scope: SettingScope): string {
    if (setting.isSecret) {
      return setting.hasSecretValue ? this.secretSetLabel() : this.secretUnsetLabel();
    }
    return this.inheritedOf(setting, scope) || this.systemDefaultOf(setting);
  }

  /** 带取值区间的设置用数字输入框，并把上下界交给浏览器。 */
  protected isNumeric(setting: SettingOutputDto): boolean {
    // 按"是不是数"判定，不按"是不是 null"：服务端省掉值为 null 的属性，非数值型设置根本不带这两个字段
    return typeof setting.minimum === 'number' && typeof setting.maximum === 'number';
  }

  /**
   * 开关显示的是**生效值**：本层没覆盖时取继承来的值。
   *
   * 开关没有占位符可以表达"跟随上层"，只看本层的值会把默认开启的项显示成关着——
   * 用户会以为自己没开，而它其实正在生效。
   */
  protected isChecked(setting: SettingOutputDto, scope: SettingScope): boolean {
    return (this.draftOf(setting, scope) || this.inheritedOf(setting, scope)) === 'true';
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
    const description = LOG_LEVEL_DESCRIPTIONS[effective];
    //#if (IncludeLocalization)
    return description ? this.transloco.translate(description) : '';
    //#else
    return description ?? '';
    //#endif
  }

  /** 本层没设值时实际生效的那个值（继承来的，或跟随系统探测到的）。 */
  protected systemOrInheritedValue(setting: SettingOutputDto, scope: SettingScope): string {
    return this.inheritedOf(setting, scope) || this.systemDefaultOf(setting);
  }

  // 同一项设置在两个作用域里各有独立草稿：同名共用一份会让个人偏好的未保存输入
  // 出现在系统默认值的输入框里。
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
        this.pageState.settings.set([...(await this.sessionContext.refreshSettings())]);

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
}
