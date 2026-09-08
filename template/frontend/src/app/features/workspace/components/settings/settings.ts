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
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideRotateCcw, lucideSlidersHorizontal, lucideUser } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmSeparator } from '@spartan-ng/helm/separator';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { catchError, EMPTY, finalize, firstValueFrom } from 'rxjs';

import { SETTING_CHOICES, SettingChoice } from './setting-choices';
import { applicationErrorMessage } from '../../../../core/errors/application-http-error';
//#if (IncludeLocalization)
import { translationReady } from '../../../../core/i18n/translation-ready';
//#endif
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { SessionContextService } from '../../../../core/services/session-context-service';
import { SettingService } from '../../../../core/settings/setting-service';
import { SettingOutputDto } from '../../../../core/settings/setting.dto';
import { LayoutService } from '../../../../layout/services/layout-service';
import { PERMISSIONS } from '../../../../shared/models/permission';

/**
 * 设置页的分组。
 *
 * `account` 写当前用户自己的偏好，`system` 写当前上下文的默认值——登录后租户已经确定，
 * 这里写的就是「本租户（或宿主）下所有人的默认值」，不是在管理别的租户；
 * 给指定租户配置属于租户管理的事。两者的写入授权也不同，因此分开。
 */
type SettingTab = 'account' | 'system';

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
    ...HlmFieldImports,
    ...HlmSelectImports,
    ...HlmTooltipImports,
    TranslocoModule,
  ],
  //#else
  imports: [
    NgIcon,
    HlmButton,
    HlmInput,
    HlmSeparator,
    ...HlmFieldImports,
    ...HlmSelectImports,
    ...HlmTooltipImports,
  ],
  //#endif
  providers: [provideIcons({ lucideRotateCcw, lucideSlidersHorizontal, lucideUser })],
  templateUrl: './settings.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Settings {
  private readonly settingService = inject(SettingService);
  private readonly authorizationService = inject(AuthorizationService);
  private readonly sessionContext = inject(SessionContextService);
  private readonly layoutService = inject(LayoutService);
  private readonly destroyRef = inject(DestroyRef);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  private readonly translationReady = translationReady(this.transloco);
  //#endif

  protected readonly activeTab = signal<SettingTab>('account');
  protected readonly loading = signal(false);
  /** 正在保存的设置名；非空即表示有一次写入在途，此时所有写入控件禁用。 */
  protected readonly saving = signal<string | null>(null);
  protected readonly isBusy = computed(() => this.saving() !== null);
  protected readonly settings = signal<SettingOutputDto[]>([]);
  protected readonly drafts = signal<Record<string, string>>({});
  private readonly labelFns = new Map<string, (value: unknown) => string>();

  protected readonly canManageSystemSettings = computed(() =>
    this.authorizationService.has(PERMISSIONS.settings.default),
  );

  /** 账户页只列允许用户覆盖的设置。 */
  protected readonly accountSettings = computed(() =>
    this.settings().filter((s) => s.allowsUserScope),
  );

  /** 系统页列允许在当前上下文上给默认值的设置。 */
  protected readonly systemSettings = computed(() =>
    this.settings().filter((s) => s.allowsTenantScope),
  );

  //#if (IncludeLocalization)
  // 重置按钮只有图标，可访问名称必须显式给出，否则读屏软件只能念出"按钮"。
  protected readonly resetLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('workspace.settings.reset');
  });

  protected readonly panelTitle = computed(() => {
    this.translationReady();
    return this.transloco.translate(`workspace.settings.${this.activeTab()}`);
  });

  protected readonly panelDescription = computed(() => {
    this.translationReady();
    return this.transloco.translate(`workspace.settings.${this.activeTab()}Description`);
  });
  //#else
  protected readonly resetLabel = computed(() => 'Reset to default');

  protected readonly panelTitle = computed(() =>
    this.activeTab() === 'account' ? 'Account' : 'System',
  );

  protected readonly panelDescription = computed(() =>
    this.activeTab() === 'account'
      ? 'Applies to you only; leave blank to inherit the system default'
      : 'Defaults for all users; each user may override them',
  );
  //#endif

  constructor() {
    // 面包屑末级文案由页面自行设置，与其他平台页保持同一约定。
    //#if (IncludeLocalization)
    effect(() => {
      this.translationReady();
      this.layoutService.title.set(this.transloco.translate('workspace.settings.title'));
    });
    //#else
    this.layoutService.title.set('Settings');
    //#endif

    this.load();
  }

  protected selectTab(tab: SettingTab): void {
    this.activeTab.set(tab);
  }

  /**
   * 输入框显示的是**本层的覆盖值**，不是回落后的生效值。
   *
   * 两者混用会让租户页显示出当前用户的个人偏好，保存即把私人偏好写成了租户默认值。
   */
  protected draftOf(setting: SettingOutputDto, scope: SettingTab): string {
    const draft = this.drafts()[this.draftKey(setting, scope)];
    if (draft !== undefined) {
      return draft;
    }

    return (scope === 'account' ? setting.userValue : setting.tenantValue) ?? '';
  }

  /** 取值封闭的设置渲染下拉，其余渲染文本框；返回 undefined 表示没有候选项。 */
  protected choicesOf(setting: SettingOutputDto): readonly SettingChoice[] | undefined {
    return SETTING_CHOICES[setting.name];
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
    scope: SettingTab,
    value: string | null | undefined,
  ): void {
    // 值没变就不写：spartan 的 select 在重新渲染时也会发一次 valueChange。
    if ((value ?? '') === this.draftOf(setting, scope)) {
      return;
    }

    void this.write(setting, value ? value : null, scope);
  }

  /** 本层未覆盖时以占位符展示继承来的值：账户继承系统默认值，系统继承代码默认值。 */
  protected inheritedOf(setting: SettingOutputDto, scope: SettingTab): string {
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
  protected inheritedLabelOf(setting: SettingOutputDto, scope: SettingTab): string {
    const inherited = this.inheritedOf(setting, scope);
    if (inherited.length === 0) {
      return '';
    }

    return this.labelOf(setting, inherited);
  }

  protected onInput(setting: SettingOutputDto, scope: SettingTab, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.drafts.update((drafts) => ({ ...drafts, [this.draftKey(setting, scope)]: value }));
  }

  protected save(setting: SettingOutputDto, scope: SettingTab): void {
    const draft = this.draftOf(setting, scope).trim();
    void this.write(setting, draft.length === 0 ? null : draft, scope);
  }

  // 同一项设置在两个页签里各有独立草稿：同名共用一份会让账户页的未保存输入
  // 出现在租户页的输入框里。
  private draftKey(setting: SettingOutputDto, scope: SettingTab): string {
    return `${scope}:${setting.name}`;
  }

  /** 清除本层级的值，读取回落到下一层。 */
  protected reset(setting: SettingOutputDto, scope: SettingTab): void {
    void this.write(setting, null, scope);
  }

  /**
   * 一次写一项，整条 PUT → GET → 发布快照 结束前不接受新的写入。
   *
   * 页面上的设置项个数是个位数，不值得为并发写入引入队列；而放任并发会踩三个坑：
   * 保存中状态只记得住最后一项、任一请求返回就把状态清掉、多个刷新响应乱序时旧快照
   * 盖掉新快照。用一个 single-flight 状态把这三件事一次挡掉。
   */
  private async write(
    setting: SettingOutputDto,
    value: string | null,
    scope: SettingTab,
  ): Promise<void> {
    if (this.saving() !== null) {
      return;
    }

    this.saving.set(setting.name);
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
      this.drafts.update((drafts) => {
        const next = { ...drafts };
        delete next[this.draftKey(setting, scope)];
        return next;
      });
    } catch (error: unknown) {
      // 刷新失败时上一份快照仍然有效，界面保持原样并提示，不要清空成一张白页。
      toast.error(applicationErrorMessage(error));
    } finally {
      this.saving.set(null);
    }
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
