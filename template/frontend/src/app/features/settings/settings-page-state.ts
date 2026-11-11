import {
  computed,
  DestroyRef,
  inject,
  Injectable,
  InjectionToken,
  PendingTasks,
  signal,
} from '@angular/core';
//#if (IncludeLocalization)
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { toast } from '@spartan-ng/brain/sonner';
import { Observable } from 'rxjs';

import { SettingDependency } from './setting-choices';
import { applicationErrorMessage } from '../../core/errors/application-http-error';
import { SessionContextService } from '../../core/services/session-context-service';
import { SettingOutputDto } from '../../core/settings/setting.dto';

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
 * 按依赖把行组成树：声明了显式依赖（`dependencies`，子设置名 → 上级）的只挂到声明的上级下，没有声明的按命名约定挂接——
 * 名为 `{前缀}.Enabled` 的布尔设置管着名字以 `{前缀}.` 开头的其他设置，挂到前缀最长的那个开关下。
 * 约定是设置名这一跨前后端的字符串契约，不需要后端下发界面元数据。
 *
 * 子项排在上级之后、保持下发顺序，子项还可以有子项。上级不在 `rows` 里（被分组或权限过滤掉）时子项留在顶层，
 * 不会消失。不做环检测：依赖表由开发者维护，成环的设置互为上级、不会出现在树上。
 */
export function buildSettingTree(
  rows: readonly SettingOutputDto[],
  dependencies: Readonly<Partial<Record<string, SettingDependency>>>,
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
    // 声明了显式依赖就只认它：上级缺席时留在顶层，不再按命名约定挂到别的开关下
    const dependency = dependencies[node.setting.name];
    const parent = dependency ? nodes.get(dependency.parent) : switchOf(node, switches);
    if (!parent || parent === node) {
      roots.push(node);
      continue;
    }

    node.shownWhen = dependency ? dependency.shownWhen : switchedOn;
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

/**
 * 页面是否读取设置。外壳在当前用户看不到任何设置时提供 `false`（例如设置页还承载别的面板、入口不要求设置权限）：
 * 不发请求，页面按已载入的空快照处理。
 */
export const SETTINGS_LOAD_ENABLED = new InjectionToken<boolean>('SETTINGS_LOAD_ENABLED', {
  factory: () => true,
});

/**
 * 设置写入的包装，由外壳提供，例如写入前确认身份：收到发出写入的函数，返回包装后的写入。包装后的写入不发值就完成
 * 表示本人取消（与 `Reauthentication.execute` 的约定一致），该行回到常态、保留输入、不报错。默认原样发出。
 */
export type SettingWriteGuard = (send: () => Observable<void>) => Observable<void>;

export const SETTING_WRITE_GUARD = new InjectionToken<SettingWriteGuard>('SETTING_WRITE_GUARD', {
  factory: () => (send) => send(),
});

/** 一次 {@link SettingsPageState.refresh} 的调用方。 */
interface RefreshWaiter {
  resolve: () => void;
  reject: (error: unknown) => void;
}

/**
 * 一个设置页（个人或系统设置）共用的设置快照：外壳据此决定面板，面板据此渲染与写入，
 * 避免重复请求以及导航与内容不一致。
 *
 * 快照取会话刷新入口（{@link SessionContextService.refreshSettings}）的返回值：语言等派生状态随会话快照一起更新，
 * 而本页显示的是返回给本页的那一份——项目的会话快照与设置页的读取范围可以不同（如按工作区读的快照与平台设置页），
 * 由刷新入口决定返回哪一份。初次读取、重试与写入后的重取共用一个序号，只认最后发起的那次：较早的读取晚到不覆盖，
 * 被取代的读取跟随最新一次落定。
 */
@Injectable()
export class SettingsPageState {
  private readonly loadEnabled = inject(SETTINGS_LOAD_ENABLED);
  private readonly sessionContext = inject(SessionContextService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly pendingTasks = inject(PendingTasks);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif

  private readonly snapshot = signal<readonly SettingOutputDto[]>([]);
  readonly settings = computed<SettingOutputDto[]>(() => [...this.snapshot()]);
  /** 本页最近一次读取还没落定；被取代的较早读取不再左右加载态。 */
  readonly loading = computed(() => this.reading());
  /** 至少成功取到过一次。外壳据此决定何时把空地址导向第一个面板。 */
  readonly loaded = signal(false);
  /** 还没有快照可保留时的加载失败原因：有值时页面显示错误态与重试，不显示"暂无可配置项"。 */
  readonly loadError = signal<string | null>(null);

  private readonly reading = signal(false);
  /** 本页最近一次读取的序号：只有它的结果落到页面上。 */
  private sequence = 0;
  /** 还没落定的 {@link refresh} 调用方，由最近一次读取一并落定。 */
  private readonly waiters: RefreshWaiter[] = [];

  constructor() {
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

  /** 读取并更新页面状态；失败按有无快照分别显示错误态或提示。 */
  load(): void {
    if (!this.loadEnabled) {
      this.loaded.set(true);
      return;
    }

    // 登记为待完成任务：应用稳定（测试的 whenStable、服务端渲染）要等到结果落到页面状态上
    this.pendingTasks.run(() =>
      this.refresh().catch((error: unknown) => {
        if (this.destroyRef.destroyed) {
          return;
        }

        // 已有快照时刷新失败（切换语言、写入后重取）：保留原内容，只做提示
        if (this.loaded()) {
          toast.error(applicationErrorMessage(error));
        } else {
          this.loadError.set(applicationErrorMessage(error));
        }
      }),
    );
  }

  /**
   * 重取快照，落定后返回；失败时抛出并保留上一份快照。写入成功后经它重取：快照到手才算写入落定。
   * 期间又发起了读取时，结果以最新那次为准。
   */
  refresh(): Promise<void> {
    if (!this.loadEnabled) {
      return Promise.resolve();
    }

    const sequence = ++this.sequence;
    this.reading.set(true);
    // 等待者挂在页面上而不是这一次请求上：新读取一发起，较早的调用方就跟着最新一次落定，
    // 不必等自己那次请求返回（它可能一直挂着，写入队列会被卡住）
    const settled = new Promise<void>((resolve, reject) => this.waiters.push({ resolve, reject }));
    this.sessionContext.refreshSettings().then(
      (settings) => {
        if (sequence !== this.sequence) {
          return;
        }

        if (!this.destroyRef.destroyed) {
          this.snapshot.set(settings);
          this.loadError.set(null);
          this.loaded.set(true);
          this.reading.set(false);
        }
        this.settle((waiter) => waiter.resolve());
      },
      (error: unknown) => {
        if (sequence !== this.sequence) {
          return;
        }

        this.reading.set(false);
        this.settle((waiter) => waiter.reject(error));
      },
    );
    return settled;
  }

  private settle(action: (waiter: RefreshWaiter) => void): void {
    this.waiters.splice(0).forEach(action);
  }
}
