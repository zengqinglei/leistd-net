import { computed, DestroyRef, inject, Injectable, PendingTasks, signal } from '@angular/core';
//#if (IncludeLocalization)
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { toast } from '@spartan-ng/brain/sonner';

import { SettingDependency } from './setting-choices';
import { applicationErrorMessage } from '../../core/errors/application-http-error';
import { SessionContextService } from '../../core/services/session-context-service';
import { SettingContextService } from '../../core/settings/setting-context-service';
import { SettingOutputDto } from '../../core/settings/setting.dto';

/** 设置作用域：`account` 写当前用户偏好；`system` 写当前租户（或宿主）下所有人的默认值，写入授权不同。 */
export type SettingScope = 'account' | 'system';

/** 一个分组及归到它下面的设置项；分组标识、顺序与显示名都来自后端。 */
export interface SettingGroup {
  key: string;
  label: string;
  settings: SettingOutputDto[];
}

/** 本作用域下能看到的设置。 */
export function settingsInScope(
  settings: readonly SettingOutputDto[],
  scope: SettingScope,
): SettingOutputDto[] {
  // 系统页：允许租户级或进程级（后者两个层级标记都是 false，只看 allowsTenantScope 会漏掉），
  // 且当前用户有该分组的管理权限；正确性以服务端算出的 canManage 为准
  return scope === 'account'
    ? settings.filter((s) => s.allowsUserScope)
    : settings.filter((s) => (s.allowsTenantScope || s.allowsHostScope) && s.canManage);
}

/** 按分组切分，分组标识与顺序来自后端（未分组的已归入 `Other`），前端不另列清单。 */
export function groupSettings(settings: readonly SettingOutputDto[]): SettingGroup[] {
  const byKey = new Map<string, SettingGroup>();
  for (const setting of settings) {
    const group = byKey.get(setting.group) ?? {
      key: setting.group,
      label: setting.groupDisplayName || setting.group,
      settings: [],
    };
    group.settings.push(setting);
    byKey.set(setting.group, group);
  }

  return [...byKey.values()];
}

/** 一行设置及挂在它下面的子项（见 {@link buildSettingTree}）。 */
export interface SettingRowNode {
  setting: SettingOutputDto;
  /** 按上级取值判断本行是否显示；顶层行没有上级，始终显示。 */
  shownWhen?: (parentValue: string) => boolean;
  /** 依赖本行的设置：本行的取值满足各自的条件时缩进显示在本行之下，可以再往下挂。 */
  children: SettingRowNode[];
}

/** 开关设置名的后缀：`{前缀}.Enabled` 管着同前缀的其他设置。 */
const SWITCH_SUFFIX = '.Enabled';

/** 命名约定的显示条件：开关的取值为真。 */
const switchedOn = (value: string): boolean => value === 'true';

/**
 * 按依赖把行组成树：先按显式依赖（`dependencies`，子设置名 → 上级）挂接，没有显式依赖的再按命名约定挂接——
 * 名为 `{前缀}.Enabled` 的布尔设置管着名字以 `{前缀}.` 开头的其他设置，挂到前缀最长的那个开关下。
 * 约定是设置名这一跨前后端的字符串契约，不需要后端下发界面元数据。
 *
 * 子项排在上级之后、保持下发顺序，子项还可以有子项。上级不在 `rows` 里（被分组或权限过滤掉）时子项留在顶层，
 * 不会消失。不做环检测：依赖表由开发者维护，成环的设置互为上级、不会出现在树上。
 */
export function buildSettingTree(
  rows: readonly SettingOutputDto[],
  dependencies: Readonly<Record<string, SettingDependency>>,
): SettingRowNode[] {
  const nodes = new Map(
    rows.map((setting): [string, SettingRowNode] => [setting.name, { setting, children: [] }]),
  );
  const switches = new Map<string, SettingRowNode>();
  for (const node of nodes.values()) {
    const { name, isBoolean } = node.setting;
    if (isBoolean === true && name.endsWith(SWITCH_SUFFIX) && name.length > SWITCH_SUFFIX.length) {
      switches.set(name.slice(0, -SWITCH_SUFFIX.length), node);
    }
  }

  const roots: SettingRowNode[] = [];
  for (const node of nodes.values()) {
    const dependency = dependencies[node.setting.name];
    const explicit = dependency ? nodes.get(dependency.parent) : undefined;
    const parent = explicit && explicit !== node ? explicit : switchOf(node, switches);
    if (!parent) {
      roots.push(node);
      continue;
    }

    node.shownWhen = parent === explicit ? dependency!.shownWhen : switchedOn;
    parent.children.push(node);
  }

  return roots;
}

/** 该行所属的开关：从最长的前缀往短找，跳过它自己（开关的前缀就是它本身管的那组）。 */
function switchOf(
  node: SettingRowNode,
  switches: ReadonlyMap<string, SettingRowNode>,
): SettingRowNode | undefined {
  const name = node.setting.name;
  for (let end = name.lastIndexOf('.'); end > 0; end = name.lastIndexOf('.', end - 1)) {
    const candidate = switches.get(name.slice(0, end));
    if (candidate && candidate !== node) {
      return candidate;
    }
  }

  return undefined;
}

/**
 * 当前应显示的子项：按上级此刻的取值逐个判断。树按层渲染，上级没显示时它的子项也不渲染，
 * 因此沿祖先链的每一级都要满足条件。
 *
 * @param parentValue 上级此刻的取值：未保存的输入优先，没有时为继承值。
 */
export function shownChildren(node: SettingRowNode, parentValue: string): SettingRowNode[] {
  return node.children.filter((child) => child.shownWhen?.(parentValue) ?? true);
}

/** 分组标识在 URL 里的写法：`RegistrationSecurity` → `registration-security`。 */
export function groupPath(key: string): string {
  return key.replace(/([a-z0-9])([A-Z])/g, '$1-$2').toLowerCase();
}

/**
 * 一个设置页（个人或系统设置）共用的设置快照：外壳据此决定面板，面板据此渲染与写入，
 * 避免重复请求以及导航与内容不一致。
 *
 * 快照就是会话的设置快照（{@link SettingContextService}），页面不另存一份；读取走会话刷新入口
 * （{@link SessionContextService.refreshSettings}），与切换器、保存后的刷新共用同一套新旧判断：晚到的旧响应
 * 不会覆盖新值，也不会把派生的界面语言改回旧值。页外的写入刷新了会话快照，本页随之更新。
 */
@Injectable()
export class SettingsPageState {
  private readonly context = inject(SettingContextService);
  private readonly sessionContext = inject(SessionContextService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly pendingTasks = inject(PendingTasks);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif

  readonly settings = computed<SettingOutputDto[]>(() => [...this.context.snapshot()]);
  /** 本页发起的读取还有未返回的。 */
  readonly loading = computed(() => this.inflight() > 0);
  /** 至少成功取到过一次。外壳据此决定何时把空地址导向第一个面板。 */
  readonly loaded = signal(false);
  /** 还没有快照可保留时的加载失败原因：有值时页面显示错误态与重试，不显示"暂无可配置项"。 */
  readonly loadError = signal<string | null>(null);

  private readonly inflight = signal(0);
  private destroyed = false;

  constructor() {
    this.destroyRef.onDestroy(() => (this.destroyed = true));
    this.load();
    //#if (IncludeLocalization)

    // 显示名由后端按请求语言本地化，切换语言须重取。langChanges$ 订阅时先发出当前语言，
    // 比对上次的语言以免进页面白发一次请求。
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

  load(): void {
    this.inflight.update((count) => count + 1);
    // 登记为待完成任务：应用稳定（测试的 whenStable、服务端渲染）要等到结果落到页面状态上
    this.pendingTasks.run(() =>
      this.sessionContext
        .refreshSettings()
        .then(() => {
          if (this.destroyed) {
            return;
          }

          this.loadError.set(null);
          this.loaded.set(true);
        })
        .catch((error: unknown) => {
          if (this.destroyed) {
            return;
          }

          // 已有快照时刷新失败（切换语言、写入后重取）：保留原内容，只做提示
          if (this.loaded()) {
            toast.error(applicationErrorMessage(error));
          } else {
            this.loadError.set(applicationErrorMessage(error));
          }
        })
        .finally(() => this.inflight.update((count) => count - 1)),
    );
  }
}
