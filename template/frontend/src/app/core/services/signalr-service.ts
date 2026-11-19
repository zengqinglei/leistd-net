import { DOCUMENT, isPlatformBrowser } from '@angular/common';
// prettier-ignore
import {
  DestroyRef,
  Injectable,
  PLATFORM_ID,
  computed,
  inject,
  signal,
  //#if (IncludeRealTime)
  assertInInjectionContext,
  //#endif
} from '@angular/core';
import {
  HubConnectionBuilder,
  HubConnection,
  HubConnectionState,
  LogLevel,
  HttpTransportType,
} from '@microsoft/signalr';
import { Observable, Subject } from 'rxjs';

import { environment } from '../../../environments/environment';
//#if (IncludeNotifications)
import { NotificationOutputDto } from '../../shared/dtos/notification.dto';
//#endif
import { MOCKED_URL } from '../mock/mocked-url';

/**
 * 连接是否还在有效生命周期内：只有 `Disconnected`（自动重连已放弃）需要重建，其余状态会自行恢复，
 * 重建只会丢掉已订阅的资源并留下孤儿连接。
 */
function isLive(connection: HubConnection | null): boolean {
  return connection != null && connection.state !== HubConnectionState.Disconnected;
}

/**
 * 实时连接状态：`idle` 尚未请求连接或已主动断开（登出、Mock 应答时也停在这里）；`reconnecting`
 * 掉线或建连失败后按退避持续重试，登录期间不放弃。
 */
export type RealtimeConnectionStatus = 'idle' | 'connected' | 'reconnecting';

/** 前几次快速重试的间隔（毫秒），服务短暂抖动或快速重启时在这里连回。 */
const QUICK_RETRY_DELAYS_MS = [0, 1000, 2000, 5000];

/** 退避间隔的起点与封顶（毫秒）。 */
const BACKOFF_BASE_MS = 10_000;
const BACKOFF_CAP_MS = 30_000;

/**
 * 第 `retryCount` 次重试前的等待（从 0 计）：前四次 0、1、2、5 秒；之后从 10 秒起翻倍、封顶 30 秒，
 * 再乘以 [0.5, 1] 的随机系数。抖动放在封顶之后，重新部署时各客户端到了封顶也不会在同一刻一起回来。
 */
export function realtimeRetryDelay(retryCount: number, random: () => number = Math.random): number {
  if (retryCount < QUICK_RETRY_DELAYS_MS.length) {
    return QUICK_RETRY_DELAYS_MS[retryCount];
  }
  const capped = Math.min(
    BACKOFF_CAP_MS,
    BACKOFF_BASE_MS * 2 ** (retryCount - QUICK_RETRY_DELAYS_MS.length),
  );
  return Math.round(capped * (0.5 + random() / 2));
}

/**
 * 失败是否疑似会话失效：negotiate 返回 401。SDK 把 `HttpError` 包成 `FailedToNegotiateWithServerError`，
 * 状态码只留在消息文本里（`… Status code '401'`），因此按文本判断，只作为重查会话的提示，不据此停止重连。
 */
function looksUnauthorized(reason: unknown): boolean {
  const message = reason instanceof Error ? reason.message : String(reason ?? '');
  return message.includes("Status code '401'");
}
//#if (IncludeRealTime)

/**
 * 实时资源键：与后端 `ICurrentTenant.ScopeKey` 一致——租户为 `{租户 Id 去连字符}:{资源}`，宿主为 `host:{资源}`。
 * 订阅只能落在自己的作用域里，后端按同一规则逐字比对。
 */
export function realtimeResourceKey(resource: string, tenantId: string | null | undefined): string {
  return tenantId ? `${tenantId.replace(/-/g, '').toLowerCase()}:${resource}` : `host:${resource}`;
}
//#endif

/**
//#if (IncludeNotifications && IncludeRealTime)
 * SignalR 全局服务：通知与实时业务事件共用一条连接（后端的实时 Hub）。
//#elseif (IncludeRealTime)
 * SignalR 全局服务：实时业务事件的连接（后端的实时 Hub）。
//#else
 * SignalR 全局服务：通知的连接（后端的通知 Hub）。
//#endif
 *
 * 地址经 resolveHubUrl 拼接；认证使用同源 Cookie 会话。
 */
@Injectable({ providedIn: 'root' })
export class SignalRService {
  //#if (IncludeRealTime)
  /** 实时 Hub 的路径：业务事件（与启用时的通知）都经它推送。 */
  static readonly hubPath = '/hubs/realtime';
  //#else
  static readonly hubPath = '/hubs/notifications';
  //#endif
  //#if (IncludeNotifications)

  /** 通知推送到客户端时调用的方法名（后端 NotificationClientMethods.Received）。 */
  static readonly notificationReceived = 'Notifications.Received';
  //#endif

  private readonly isMockedUrl = inject(MOCKED_URL);
  private connection: HubConnection | null = null;
  //#if (TenantWorkspace)
  private homeConnection: HubConnection | null = null;
  private workspaceName: string | null = null;
  private workspaceRevision = 0;
  //#if (IncludeNotifications)
  private readonly homeNotifications = signal<NotificationOutputDto[]>([]);
  //#endif

  async setWorkspace(name: string | null, platformStaff: boolean): Promise<void> {
    const revision = ++this.workspaceRevision;
    if (this.workspaceName === name) return;
    if (!this.workspaceName && name && platformStaff) {
      await this.connect();
      if (revision !== this.workspaceRevision) return;
      this.homeConnection = this.connection;
      this.connection = null;
      //#if (IncludeNotifications)
      this.homeNotifications.set(this.notifications());
      //#endif
    }
    // 目标范围与连接归属在旧连接收尾之前同步定下：收尾途中目标页主动 connect()，建出的就是目标工作空间的连接
    const resetting = this.resetCurrent();
    this.workspaceName = name;
    if (!name && this.homeConnection) {
      this.connection = this.homeConnection;
      this.homeConnection = null;
      //#if (IncludeNotifications)
      this.notifications.set(this.homeNotifications());
      //#endif
      if (this.connection.state === HubConnectionState.Connected) {
        this.status.set('connected');
      } else {
        // 首页连接在后台断了：它的退避等待不在界面上，丢掉重建，回到首页即重试
        void this.dropConnection();
        this.status.set('reconnecting');
      }
    }
    await resetting;
    if (revision !== this.workspaceRevision) return;
    await this.connect();
  }
  //#endif
  //#if (IncludeNotifications)

  readonly notifications = signal<NotificationOutputDto[]>([]);
  readonly unreadCount = computed(() => this.notifications().filter((n) => !n.isRead).length);
  //#endif
  //#if (IncludeRealTime)

  readonly lastResourceEvent = signal<{ eventName: string; payload: unknown } | null>(null);
  //#endif

  private readonly status = signal<RealtimeConnectionStatus>('idle');

  readonly connectionStatus = this.status.asReadonly();

  readonly isConnected = computed(() => this.status() === 'connected');

  private readonly nextRetry = signal<number | null>(null);

  /**
   * 下一次重试的时刻（`Date.now()` 毫秒），供界面倒计时；`null` 或已过去表示正在建连。只在 `reconnecting` 时有意义。
   */
  readonly nextRetryAt = this.nextRetry.asReadonly();

  private readonly sessionRejectedSubject = new Subject<void>();

  /**
   * 重连被拒、疑似会话失效（401）。会话上下文收到后重查 `/me`：确已失效时由拦截器清会话并 `reset()`，
   * 重连随之停止；会话仍有效时重连继续。
   */
  readonly sessionRejected$: Observable<void> = this.sessionRejectedSubject.asObservable();

  /** 服务自己的退避等待：SDK 自动重连不覆盖的建连失败与连接关闭由它接手。 */
  private retryTimer: ReturnType<typeof setTimeout> | null = null;

  /** 服务自己的退避计数，建连成功或立即重试时归零。 */
  private retryCount = 0;
  //#if (IncludeRealTime)

  // 需求（谁还持有哪个键）与现状（当前服务端连接上订阅了哪些键）分开记，两者之差由每个键一条
  // 对账链补齐，因此登记、销毁、建连、重连的先后不影响最终结果。

  /** 资源键 → 持有它的登记。集合为空即不再需要该订阅。 */
  private readonly resourceHolders = new Map<string, Set<object>>();

  /**
   * 当前服务端连接上已订阅的资源键。每次建连（含自动重连）换新集合；对账步骤只写回开始时的
   * 那个集合，连接换过时写进的是已作废的集合。
   */
  private subscribedResources = new Set<string>();

  /** 每个资源键的对账链：同一个键的 Subscribe/Unsubscribe 依次执行。 */
  private readonly reconcileQueues = new Map<string, Promise<void>>();

  private readonly resourceEventNames = new Set<string>();
  private readonly resourceSubscribedSubject = new Subject<string>();

  /**
   * 资源订阅已被服务端确认（首次、自动重连后、重建连接后），值为资源键。推送不持久化，
   * 把推送当作刷新提示的页面收到后补查一次；isConnected 不能代替它（那时订阅还没重建）。
   * 只在实际发起的 Subscribe 成功、连接未换且仍有持有者时发出；重复登记已订阅的共享键不另发。
   */
  readonly resourceSubscribed$: Observable<string> = this.resourceSubscribedSubject.asObservable();
  //#endif

  /** 进行中的连接过程，用于让并发的 connect() 复用同一次。 */
  private connecting: Promise<void> | null = null;

  /** 上面那个连接过程属于哪一代主体。跨代不可复用。 */
  private connectingGeneration = -1;

  /**
   * 认证主体代际，每次 reset() 递增。连接过程中途登出时，await 回来的连接属于上一个主体，
   * 须就地关掉，否则会以旧身份继续接收推送。
   */
  private generation = 0;

  /**
   * 当前认证主体的代际。await 之后要写用户态的地方都先核对它：reset() 清不掉上一个主体已发出、
   * 稍后才回来的异步操作，不核对会把 A 的数据落到 B 的界面上。
   */
  get authGeneration(): number {
    return this.generation;
  }

  /** 传入的代际是否仍是当前主体。 */
  isCurrentGeneration(generation: number): boolean {
    return generation === this.generation;
  }

  constructor() {
    const destroyRef = inject(DestroyRef);
    destroyRef.onDestroy(() => this.clearRetry());
    if (!isPlatformBrowser(inject(PLATFORM_ID))) {
      return;
    }

    // 网络恢复、页签回到前台时不必等满退避间隔
    const document = inject(DOCUMENT);
    const window = document.defaultView;
    const onOnline = () => this.retryNow();
    const onVisibilityChange = () => {
      if (document.visibilityState === 'visible') {
        this.retryNow();
      }
    };
    window?.addEventListener('online', onOnline);
    document.addEventListener('visibilitychange', onVisibilityChange);
    destroyRef.onDestroy(() => {
      window?.removeEventListener('online', onOnline);
      document.removeEventListener('visibilitychange', onVisibilityChange);
    });
  }

  /**
   * 建立 SignalR 连接（登录后调用）。幂等：并发调用复用同一次连接过程，已连上时直接返回；
   * 重建会让被覆盖的旧连接继续推送，造成泄漏和重复通知。通知组件每次初始化都会调用。
   */
  connect(): Promise<void> {
    // 实时连接本身由 Mock 应答时不建连：Mock 不模拟 SignalR，连不上的后端只会反复重试
    if (this.isMockedUrl(SignalRService.hubPath)) {
      return Promise.resolve();
    }

    if (this.connecting && this.connectingGeneration === this.generation) {
      return this.connecting;
    }

    // 上一个主体的连接过程还没收尾：它会断开自己建的连接，直接复用会得到"成功但没连上"的结果。
    // 等它结束再为本主体重建。
    const previous = this.connecting;
    const generation = this.generation;

    this.connectingGeneration = generation;
    this.connecting = (async () => {
      await previous?.catch(() => undefined);
      await this.connectAsync(generation);
    })().finally(() => {
      if (this.connectingGeneration === generation) {
        this.connecting = null;
      }
    });

    return this.connecting;
  }

  /**
   * @param generation 发起本次连接请求时的认证代际。由调用方传入：本方法要等上一个连接过程收尾
   * 才执行，那时读到的已是 reset() 递增后的值，校验将形同虚设。
   */
  private async connectAsync(generation: number): Promise<void> {
    if (isLive(this.connection)) {
      return;
    }

    // 手上的连接已经死了（服务端关闭了它，或被立即重试丢弃）：先清干净再重建。
    // 少了这一步，早退会把应用永久留在断线状态，不早退又会泄漏。状态不动：重建期间仍是 reconnecting。
    await this.dropConnection();

    // await 之后先核对代际：连接一旦写进字段，isCurrent() 的身份比对就一律为真，排除不了旧请求
    // 在 reset 之后新建的连接。本行到建连方法里的字段赋值之间不得插入 await。
    if (generation !== this.generation) {
      return;
    }

    try {
      // 页面主动建连时提前用掉退避等待：界面不再显示旧倒计时，原计时器也不再唤醒
      this.clearRetry();
      await this.startConnection();

      if (generation !== this.generation) {
        // 连接期间发生了主体切换：这条连接握的是上一个身份，不能留给下一个用户。
        await this.dropConnection();
      }
    } catch (err) {
      console.error('[SignalR] Connection failed:', err);
      await this.dropConnection();
      // SDK 的自动重连只接管建连成功之后的掉线，建连本身失败由这里按同一退避重试
      this.scheduleRetry(generation, err);
    }
  }

  /**
   * 跳过等待，马上重试（「立即重试」、网络恢复、页签回到前台）。只在等待重试时生效：正在建连时不重复发起，
   * 连接正常或已主动断开时什么都不做。退避计数从头开始。
   */
  retryNow(): void {
    const at = this.nextRetry();
    if (this.status() !== 'reconnecting' || at === null || at <= Date.now()) {
      return;
    }

    this.clearRetry();
    this.retryCount = 0;
    // SDK 正在等下一次自动重连：丢掉这条连接另建一条。字段先于 stop 清空，它的 onclose 认不出自己就不会再排重试
    if (this.connection?.state === HubConnectionState.Reconnecting) {
      void this.dropConnection();
    }
    void this.connect();
  }

  /** 排一次服务自己的退避重试；代际已变（登出、换人、切换工作空间）时不排。 */
  private scheduleRetry(generation: number, reason: unknown): void {
    if (generation !== this.generation) {
      return;
    }

    this.clearRetry();
    const delay = realtimeRetryDelay(this.retryCount++);
    this.status.set('reconnecting');
    this.nextRetry.set(Date.now() + delay);
    this.reportRejection(reason);
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null;
      void this.connect();
    }, delay);
  }

  /** 撤销等待中的重试，并清掉倒计时。退避计数不在这里归零：只有连上或立即重试才从头开始。 */
  private clearRetry(): void {
    if (this.retryTimer) {
      clearTimeout(this.retryTimer);
      this.retryTimer = null;
    }
    this.nextRetry.set(null);
  }

  private reportRejection(reason: unknown): void {
    if (looksUnauthorized(reason)) {
      this.sessionRejectedSubject.next();
    }
  }

  /** 连接已确认可用：重试状态归零。 */
  private markConnected(): void {
    this.clearRetry();
    this.retryCount = 0;
    this.status.set('connected');
  }

  /**
   * 认证主体切换时清空与该主体绑定的状态。SignalR 的 principal 在握手时定死，不断开就换人登录，
   * 下一个用户会以上一个人的身份继续收消息。
//#if (IncludeRealTime)
   *
   * resourceEventNames 不清：它与主体无关，清掉会让重连后所有监听失效。
//#endif
   */
  async reset(): Promise<void> {
    //#if (TenantWorkspace)
    this.workspaceRevision++;
    const home = this.homeConnection;
    this.homeConnection = null;
    this.workspaceName = null;
    const resetting = this.resetCurrent();
    await Promise.all([resetting, home?.stop()]);
    //#else
    await this.resetCurrent();
    //#endif
  }

  private async resetCurrent(): Promise<void> {
    this.generation++;
    this.retryCount = 0;
    //#if (IncludeRealTime)
    this.resourceHolders.clear();
    this.subscribedResources = new Set();
    this.reconcileQueues.clear();
    this.lastResourceEvent.set(null);
    //#endif
    //#if (IncludeNotifications)
    this.notifications.set([]);
    //#endif

    await this.disconnect();
  }

  /** 主动断开连接，回到 `idle`，等待中的重试一并撤销。只经 reset 调用：代际随之递增，在途建连失败后不会再排重试。 */
  private async disconnect(): Promise<void> {
    this.clearRetry();
    this.status.set('idle');
    await this.dropConnection();
  }

  /** 丢掉当前连接，不改状态。无论 stop 是否抛错，引用一律清空——留着就等于泄漏。 */
  private async dropConnection(): Promise<void> {
    const connection = this.connection;
    this.connection = null;

    if (!connection) {
      return;
    }

    try {
      await connection.stop();
    } catch (err) {
      console.error('[SignalR] stop failed:', err);
    }
  }
  //#if (IncludeRealTime)

  /** 注册一个业务事件名监听（推送到 lastResourceEvent 信号）。 */
  registerResourceEvent(eventName: string): void {
    if (this.resourceEventNames.has(eventName)) return;
    this.resourceEventNames.add(eventName);

    const connection = this.connection;
    connection?.on(eventName, (payload: unknown) => {
      // 动态注册的监听同样要判身份：注册时那条连接可能在主体切换后才收到事件。
      if (this.connection !== connection) {
        return;
      }

      this.lastResourceEvent.set({ eventName, payload });
    });
  }

  /**
   * 在 `destroyRef` 所属的组件（或注入器）存活期间订阅资源变更。登记即生效，销毁时自动撤销；
   * 实际的 Subscribe/Unsubscribe 在连接可用时补齐，与 `connect()` 先后无关，不要放进
   * `connect().then(...)`。同一个键在最后一个持有者撤销后才退订。
   *
   * 省略 `destroyRef` 时取当前注入上下文的，须在构造期或字段初始化时调用；已销毁的不登记。
   * `reset()` 清空全部登记。
   */
  watchResource(resourceKey: string, destroyRef?: DestroyRef): void {
    if (!destroyRef) {
      assertInInjectionContext(this.watchResource);
      destroyRef = inject(DestroyRef);
    }
    if (destroyRef.destroyed) {
      return;
    }

    const holder = {};
    const holders = this.resourceHolders.get(resourceKey) ?? new Set<object>();
    holders.add(holder);
    this.resourceHolders.set(resourceKey, holders);

    // 撤销只删自己这一次登记：reset() 后新主体同名键的持有是另一组登记，旧页面晚到的销毁碰不到它
    destroyRef.onDestroy(() => {
      const current = this.resourceHolders.get(resourceKey);
      current?.delete(holder);
      if (current?.size === 0) {
        this.resourceHolders.delete(resourceKey);
      }
      this.reconcile(resourceKey);
    });

    this.reconcile(resourceKey);
  }
  //#endif
  //#if (IncludeRealTime)

  /** 为该键排一次对账，排在同一个键已有的步骤之后。 */
  private reconcile(resourceKey: string): void {
    const generation = this.generation;
    const step = (this.reconcileQueues.get(resourceKey) ?? Promise.resolve()).then(() =>
      this.reconcileStep(resourceKey, generation),
    );
    this.reconcileQueues.set(resourceKey, step);
    void step.finally(() => {
      // 只清自己这一项：之后排进来的步骤，或 reset() 之后新主体的链，都不能被它删掉
      if (this.reconcileQueues.get(resourceKey) === step) {
        this.reconcileQueues.delete(resourceKey);
      }
    });
  }

  /**
   * 比较"是否仍有持有者"与"当前连接上是否已订阅"，按差值调一次 Hub。
   *
   * 不会拒绝：失败只记诊断，链上后面的步骤照常执行；连接重建时会再对账一次。
   * 没连上（含 Mock 不建连）时什么都不做，需求留着，连上后统一补齐。
   */
  private async reconcileStep(resourceKey: string, generation: number): Promise<void> {
    const connection = this.connection;
    if (
      !this.isCurrentGeneration(generation) ||
      connection?.state !== HubConnectionState.Connected
    ) {
      return;
    }

    const subscribed = this.subscribedResources;
    const wanted = (this.resourceHolders.get(resourceKey)?.size ?? 0) > 0;
    if (wanted === subscribed.has(resourceKey)) {
      return;
    }

    try {
      if (!wanted) {
        await connection.invoke('Unsubscribe', resourceKey);
      } else if (!(await connection.invoke<boolean>('Subscribe', resourceKey))) {
        // 服务端没有让本连接加入这个资源（无权订阅，或连接已在关闭）：不算已订阅、不报告，
        // 需求留着，连接重建时再对账
        console.warn('[SignalR] resource subscription rejected:', resourceKey);
        return;
      }
    } catch (err) {
      console.error('[SignalR] resource subscription failed:', resourceKey, err);
      return;
    }

    if (!wanted) {
      subscribed.delete(resourceKey);
      return;
    }

    subscribed.add(resourceKey);
    // 只报告属于当前连接、且仍有人持有的确认。连接要单独核对：SDK 收到确认后先完成 Promise，
    // 本方法 await 之后的续行可能晚于 reset、disconnect 或重建连接才执行——那时新主体
    // 可能已登记同一个键，旧连接的确认不能冒充它的。确认期间已无人持有时，链上随后的步骤会退订它
    if (this.connection === connection && (this.resourceHolders.get(resourceKey)?.size ?? 0) > 0) {
      this.resourceSubscribedSubject.next(resourceKey);
    }
  }

  /** 新的服务端连接上没有任何组：把仍有持有者的键全部重新对账。 */
  private resubscribeResources(): void {
    this.subscribedResources = new Set();
    for (const resourceKey of this.resourceHolders.keys()) {
      this.reconcile(resourceKey);
    }
  }
  //#endif

  /**
   * 解析 Hub 绝对地址：HubConnectionBuilder 不经过 HTTP 拦截器，需手动拼接
   * `environment.api.gateway`；网关为空时返回相对路径，由浏览器按当前源解析。
   */
  private resolveHubUrl(path: string): string {
    const gateway = environment.api.gateway || '';
    const gatewayPart = gateway.endsWith('/') ? gateway.slice(0, -1) : gateway;
    return gatewayPart ? `${gatewayPart}${path}` : path;
  }

  private async startConnection(): Promise<void> {
    // 策略只在建连成功后的掉线里被调用，那时 owner 已赋值
    let owner: HubConnection | null = null;
    const connection = new HubConnectionBuilder()
      //#if (TenantWorkspace)
      .withUrl(
        this.resolveHubUrl(SignalRService.hubPath) +
          (this.workspaceName ? `?workspace=${encodeURIComponent(this.workspaceName)}` : ''),
        {
          transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling,
        },
      )
      //#else
      .withUrl(this.resolveHubUrl(SignalRService.hubPath), {
        transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling,
      })
      //#endif
      // 登录期间不放弃：SDK 默认重试 4 次、数组形式重试完即关闭，重新部署稍长就再也连不回来
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: ({ previousRetryCount, retryReason }) => {
          const delay = realtimeRetryDelay(previousRetryCount);
          if (owner && this.connection === owner) {
            this.nextRetry.set(Date.now() + delay);
            this.reportRejection(retryReason);
          }
          return delay;
        },
      })
      // 普通连接日志保持安静；连接与重试错误仍可排查。
      .configureLogging(LogLevel.Warning)
      .build();
    owner = connection;

    // 每个回调先确认仍是当前连接。判据用身份而非代际：stop() 是异步的，旧连接的回调可能晚于
    // 主体切换才到达，而 disconnect() 已把字段置空。
    const isCurrent = () => this.connection === connection;
    //#if (IncludeNotifications)

    connection.on(SignalRService.notificationReceived, (notification: NotificationOutputDto) => {
      //#if (TenantWorkspace)
      if (this.homeConnection === connection) {
        this.homeNotifications.update((items) => [
          notification,
          ...items.filter((item) => item.id !== notification.id),
        ]);
        return;
      }
      //#endif
      if (!isCurrent()) {
        return;
      }

      this.notifications.update((list) => [notification, ...list]);
    });
    //#endif
    //#if (IncludeRealTime)

    for (const eventName of this.resourceEventNames) {
      connection.on(eventName, (payload: unknown) => {
        if (!isCurrent()) {
          return;
        }

        this.lastResourceEvent.set({ eventName, payload });
      });
    }
    //#endif

    connection.onreconnecting(() => isCurrent() && this.status.set('reconnecting'));
    // 主动断开先清字段，走到这里的都是意外关闭（如服务端发来不允许重连的关闭消息）：按退避重建
    connection.onclose((error) => isCurrent() && this.scheduleRetry(this.generation, error));
    connection.onreconnected(() => {
      if (!isCurrent()) {
        return;
      }

      this.markConnected();
      //#if (IncludeRealTime)

      // 重连后是一条新的服务端连接，分组订阅要重新建立。
      // SDK 不等待本回调返回的 Promise，恢复因此排进各键的对账链，不在这里直接调用：
      // 恢复途中被释放或重新持有的键，由链上后续步骤按当时的持有关系处理。
      this.resubscribeResources();
      //#endif
    });

    this.connection = connection;
    await connection.start();

    if (isCurrent()) {
      this.markConnected();
      //#if (IncludeRealTime)
      // 连上之前登记的需求（页面先于连接创建）在这里补齐
      this.resubscribeResources();
      //#endif
    }
  }
}
