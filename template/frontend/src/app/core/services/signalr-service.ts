// prettier-ignore
import {
  Injectable,
  signal,
  //#if (IncludeNotifications)
  computed,
  //#endif
  //#if (IncludeRealTime)
  DestroyRef,
  assertInInjectionContext,
  inject,
  //#endif
} from '@angular/core';
import {
  HubConnectionBuilder,
  HubConnection,
  HubConnectionState,
  LogLevel,
  HttpTransportType,
} from '@microsoft/signalr';
//#if (IncludeRealTime)
import { Observable, Subject } from 'rxjs';
//#endif

import { isMockedUrl } from '../../../../_mock/core/providers';
import { environment } from '../../../environments/environment';

/**
 * 连接是否还在有效生命周期内。
 *
 * `Disconnected` 表示自动重连也已放弃，此时必须重建；其余状态（连接中、已连接、重连中）
 * 都会自行恢复，重建只会白白丢掉已订阅的资源并留下一对孤儿连接。
 */
function isLive(connection: HubConnection | null): boolean {
  return connection != null && connection.state !== HubConnectionState.Disconnected;
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
//#if (IncludeNotifications)

/** 通知 DTO（与后端 Leistd.Notifications.NotificationOutputDto 对应，类型为字符串） */
export interface NotificationOutputDto {
  id: string;
  title: string;
  content?: string;
  type: string;
  link?: string;
  icon?: string;
  isRead: boolean;
  creationTime: string;
  relatedEntityId?: string;
  relatedEntityType?: string;
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
 * 地址：Hub 经 resolveHubUrl 拼接 environment.api.gateway，与 HTTP 请求走同一后端（HubConnectionBuilder 不经过 HTTP 拦截器）。
 * 认证：浏览器使用同源 Cookie 会话。
 */
@Injectable({ providedIn: 'root' })
export class SignalRService {
  //#if (IncludeRealTime)
  /** 实时 Hub 的路径：业务事件（与启用时的通知）都经它推送。 */
  static readonly hubPath = '/hubs/realtime';
  //#else
  /** 通知 Hub 的路径。 */
  static readonly hubPath = '/hubs/notifications';
  //#endif
  //#if (IncludeNotifications)

  /** 通知推送到客户端时调用的方法名（后端 NotificationClientMethods.Received）。 */
  static readonly notificationReceived = 'Notifications.Received';
  //#endif

  private connection: HubConnection | null = null;
  //#if (IncludeNotifications)

  // ── 通知状态 ──
  readonly notifications = signal<NotificationOutputDto[]>([]);
  readonly unreadCount = computed(() => this.notifications().filter((n) => !n.isRead).length);
  //#endif
  //#if (IncludeRealTime)

  // ── 业务事件（通用）：最近一次收到的资源事件 ──
  readonly lastResourceEvent = signal<{ eventName: string; payload: unknown } | null>(null);
  //#endif

  // ── 连接状态 ──
  private readonly connected = signal(false);

  /** 实时连接是否可用。 */
  readonly isConnected = this.connected.asReadonly();
  //#if (IncludeRealTime)

  // ── 资源订阅 ──
  // 需求与现状分开记：需求是"谁还持有哪个 key"，现状是"当前服务端连接上订阅了哪些 key"。
  // 两者之差由每个 key 一条的对账链补齐，所以登记、销毁、建连、重连谁先谁后都不影响最终结果。

  /** 资源键 → 持有它的登记。集合为空即不再需要该订阅。 */
  private readonly resourceHolders = new Map<string, Set<object>>();

  /**
   * 当前服务端连接上已成功订阅的资源键。
   *
   * 每建立一次服务端连接（首次连接、自动重连）就换一个新集合：新连接上没有任何组。
   * 对账步骤记下开始时的集合，调用返回后只写回那一个——连接换过时写进的是已作废的集合。
   */
  private subscribedResources = new Set<string>();

  /** 每个资源键的对账链：同一个键的 Subscribe/Unsubscribe 依次执行。 */
  private readonly reconcileQueues = new Map<string, Promise<void>>();

  private readonly resourceEventNames = new Set<string>();
  private readonly resourceSubscribedSubject = new Subject<string>();

  /**
   * 某个资源的订阅已被服务端确认（首次订阅、自动重连后、重建连接后都会发出），值为资源键。
   *
   * 推送不持久化：断线期间、以及查询完成到加入订阅之间的变更都不会补发。
   * 把推送当作"该重新查询了"的页面，收到这个事件后补查一次，就不会停在旧快照上。
   * 连接恢复（isConnected）不能代替它：那时订阅还没重新建立，立即查询仍会漏掉之后的变更。
   * 只在实际发起的 Subscribe 成功后发出：确认回来时连接已换（主体切换、断开或重建）、
   * 或已无人持有，都不发出；再次登记一个已经订阅着的共享键不会另发确认，页面照常自己做首次查询。
   */
  readonly resourceSubscribed$: Observable<string> = this.resourceSubscribedSubject.asObservable();
  //#endif

  /** 进行中的连接过程，用于让并发的 connect() 复用同一次。 */
  private connecting: Promise<void> | null = null;

  /** 上面那个连接过程属于哪一代主体。跨代不可复用。 */
  private connectingGeneration = -1;

  /**
   * 认证主体代际。每次 reset() 递增。
   *
   * 连接过程中途发生登出时，await 回来的那条连接属于上一个主体，必须就地关掉——
   * 否则它会被写进字段，成为一条没人再管、却仍在以旧身份接收推送的孤儿。
   */
  private generation = 0;

  /**
   * 当前认证主体的代际。
   *
   * 凡是"await 之后要写用户态"的地方都必须先核对它：reset() 只清得掉调用那一刻的
   * 状态，清不掉 A 已经发出、稍后才回来的异步操作。历史通知响应、Hub 回调、
   * 订阅回填都会写同一批共享 signal，不核对就会把 A 的数据落到 B 的界面上。
   */
  get authGeneration(): number {
    return this.generation;
  }

  /** 传入的代际是否仍是当前主体。 */
  isCurrentGeneration(generation: number): boolean {
    return generation === this.generation;
  }

  /**
   * 建立 SignalR 连接（在用户登录后调用）。
   *
   * 幂等：并发调用复用同一次连接过程；已经连上时直接返回，不重建。
   * 连接成功后再次调用也不能新建连接，否则被覆盖字段引用无法触达的连接及其处理器
   * 会继续向同一个 signal 推送，造成连接泄漏和重复通知。
   * 通知组件每次初始化都会走到这里，重挂载就会触发。
   */
  connect(): Promise<void> {
    // 实时连接本身由 Mock 应答时不建连：Mock 不模拟 SignalR，连不上的后端只会反复重试
    if (isMockedUrl(environment.useMock, SignalRService.hubPath)) {
      return Promise.resolve();
    }

    if (this.connecting && this.connectingGeneration === this.generation) {
      return this.connecting;
    }

    // 上一个主体的连接过程还没收尾：它发现代际变化后会把自己建的连接断掉，
    // 直接复用它的 Promise 会让本主体拿到一个"正常返回但什么都没连上"的结果，
    // 在组件重挂载前一直没有实时连接。等它结束，再为本主体重新建立。
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
   * @param generation 发起本次连接请求时的认证代际。
   *
   * 由调用方传入而不是在这里读：本方法要等上一个主体的连接过程收尾才开始执行，
   * 那时读到的已经是 reset() 递增过的值，代际校验永远相等、防护形同虚设。
   * 代际属于"这次 connect 请求"，不属于"这段代码碰巧执行的时刻"。
   */
  private async connectAsync(generation: number): Promise<void> {
    if (isLive(this.connection)) {
      return;
    }

    // 手上的连接已经死了（自动重连耗尽）：先清干净再重建。
    // 少了这一步，早退会把应用永久留在断线状态，不早退又会泄漏。
    await this.disconnect();

    // await 之后先核对一次：连接尚未建立就已经不是当前主体，直接不建。
    // 连接一旦写进字段，下面那些 isCurrent() 的身份比对就一律为真——
    // 身份判据能排除"已退休的旧连接"，排除不了"旧请求在 reset 之后新建的连接"。
    //
    // 约束：本行到建连方法里的字段赋值之间**不得插入 await**。
    // 一旦插入，reset 可以在核对之后、赋值之前发生，这道防护就静默失效了。
    if (generation !== this.generation) {
      return;
    }

    try {
      await this.startConnection();

      if (generation !== this.generation) {
        // 连接期间发生了主体切换：这条连接握的是上一个身份，不能留给下一个用户。
        await this.disconnect();
      }
    } catch (err) {
      console.error('[SignalR] Connection failed:', err);
      await this.disconnect();
    }
  }

  /**
   * 认证主体切换时清空一切与该主体绑定的状态。
   *
   * SignalR 的 principal 在握手时定死，连接不会因为前端清掉用户信号而重新授权。
   * 不断开就换人登录，下一个用户会复用上一个人的活连接，以对方的身份继续收消息，
   * 内存里的状态也照样留在界面上——这不是残留，是跨用户的数据泄漏。
//#if (IncludeRealTime)
   *
   * resourceEventNames 不清：那是应用关心哪些事件名，与主体无关，
   * 清掉会让重连后所有监听失效。
//#endif
   */
  async reset(): Promise<void> {
    this.generation++;
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

  /** 断开连接。无论 stop 是否抛错，引用一律清空——留着就等于泄漏。 */
  async disconnect(): Promise<void> {
    const connection = this.connection;
    this.connection = null;
    this.connected.set(false);

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
   * 在 `destroyRef` 所属的组件（或注入器）存活期间订阅资源变更。
   *
   * 调用即登记需求，销毁时自动撤销；真正的 Subscribe/Unsubscribe 由服务在连接可用时补齐，
   * 所以与 `connect()` 的先后无关——不要把它放进 `connect().then(...)`，那会让"销毁先于连上"的页面在连上后照样订阅。
   * 多个持有者共用同一个键时，最后一个撤销后才真正退订。
   *
   * 省略 `destroyRef` 时取当前注入上下文的（与 `takeUntilDestroyed` 同一惯例），须在构造期或字段初始化时调用；
   * 已销毁的 `destroyRef` 不登记。认证主体切换（`reset()`）会清空全部登记。
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

  // ── 内部 ──
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
      await connection.invoke(wanted ? 'Subscribe' : 'Unsubscribe', resourceKey);
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
   * 解析 Hub 绝对地址。
   *
   * SignalR 的 HubConnectionBuilder 不经过 Angular HTTP 拦截器，
   * 因此需在此手动拼接 `environment.api.gateway` 前缀（与 urlFormatInterceptor 一致）。
   * 网关为空时返回相对路径，由浏览器按当前源解析（同源托管与本机开发代理）。
   */
  private resolveHubUrl(path: string): string {
    const gateway = environment.api.gateway || '';
    const gatewayPart = gateway.endsWith('/') ? gateway.slice(0, -1) : gateway;
    return gatewayPart ? `${gatewayPart}${path}` : path;
  }

  private async startConnection(): Promise<void> {
    const connection = new HubConnectionBuilder()
      .withUrl(this.resolveHubUrl(SignalRService.hubPath), {
        transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      // 普通连接日志保持安静；连接与重试错误仍可排查。
      .configureLogging(LogLevel.Warning)
      .build();

    // 每个回调都先确认自己仍是当前那条连接。判据用身份而不是代际：
    // stop() 是异步的，旧连接的推送与状态回调可能晚于主体切换才到达，
    // 而 disconnect() 已经把字段置空，身份比对天然为假。
    // 身份判据与它保护的对象绑在一起，不会出现"又漏了一处没加检查"。
    const isCurrent = () => this.connection === connection;
    //#if (IncludeNotifications)

    connection.on(SignalRService.notificationReceived, (notification: NotificationOutputDto) => {
      if (!isCurrent()) {
        return;
      }

      this.notifications.update((list) => [notification, ...list]);
    });
    //#endif
    //#if (IncludeRealTime)

    // 重新挂载已注册的业务事件监听
    for (const eventName of this.resourceEventNames) {
      connection.on(eventName, (payload: unknown) => {
        if (!isCurrent()) {
          return;
        }

        this.lastResourceEvent.set({ eventName, payload });
      });
    }
    //#endif

    connection.onreconnecting(() => isCurrent() && this.connected.set(false));
    connection.onclose(() => isCurrent() && this.connected.set(false));
    connection.onreconnected(() => {
      if (!isCurrent()) {
        return;
      }

      this.connected.set(true);
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
      this.connected.set(true);
      //#if (IncludeRealTime)
      // 连上之前登记的需求（页面先于连接创建）在这里补齐
      this.resubscribeResources();
      //#endif
    }
  }
}
