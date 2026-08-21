// prettier-ignore
import {
  Injectable,
  signal,
  computed,
  //#if (ResourceService)
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
//#if (ResourceService)
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { firstValueFrom } from 'rxjs';
//#endif

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

/**
 * SignalR 全局服务：管理通知 Hub 与实时业务事件 Hub。
 *
 * 地址：Hub 经 resolveHubUrl 拼接 environment.api.gateway，与 HTTP 请求走同一后端（HubConnectionBuilder 不经过 HTTP 拦截器）。
 * 认证：Identity 使用 Cookie 会话；Resource 从 OIDC 会话提供短寿命 access token。
 * 浏览器 WebSocket/SSE 无法设置 Authorization 头，SignalR 会在 Hub 连接上使用 access_token query，
 * 后端只对两个 Hub 路径定向接受并立即从 QueryString 移除。
 */
@Injectable({ providedIn: 'root' })
export class SignalRService {
  //#if (ResourceService)
  private readonly oidc = inject(OidcSecurityService);
  //#endif
  private notificationConnection: HubConnection | null = null;
  private businessConnection: HubConnection | null = null;

  // ── 通知状态 ──
  readonly notifications = signal<NotificationOutputDto[]>([]);
  readonly unreadCount = computed(() => this.notifications().filter((n) => !n.isRead).length);

  // ── 业务事件（通用）：最近一次收到的资源事件 ──
  readonly lastResourceEvent = signal<{ eventName: string; payload: unknown } | null>(null);

  // ── 连接状态 ──
  private readonly notificationConnected = signal(false);
  private readonly businessConnected = signal(false);

  /**
   * 两个 Hub 是否都可用。
   *
   * 必须是两者的合取：各自会独立断线重连，只跟踪其中一个的话，
   * 业务 Hub 单独掉线时这里仍是 true，而通知 Hub 一恢复又会把它拉成 true——
   * 界面据此显示"实时已连接"，实际有一半没回来。
   */
  readonly isConnected = computed(() => this.notificationConnected() && this.businessConnected());

  // ── 已订阅资源（重连后重新订阅） ──
  private readonly subscribedResources = new Set<string>();
  private readonly resourceEventNames = new Set<string>();

  /** 进行中的连接过程，用于让并发的 connect() 复用同一次。 */
  private connecting: Promise<void> | null = null;

  /** 上面那个连接过程属于哪一代主体。跨代不可复用。 */
  private connectingGeneration = -1;

  /**
   * 认证主体代际。每次 reset() 递增。
   *
   * 连接过程中途发生登出时，await 回来的那对连接属于上一个主体，必须就地关掉——
   * 否则它们会被写进字段，成为一对没人再管、却仍在以旧身份接收推送的孤儿。
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
   * 只去重"进行中"的调用是不够的——连接成功后再调一次会新建一对并覆盖字段引用，
   * 旧的两条连同事件处理器继续往同一个 signal 里推，表现为连接泄漏加重复通知。
   * 通知组件每次初始化都会走到这里，重挂载就会触发。
   *
   * 全成功或全回滚：任一 Hub 启动失败时停掉本轮已经起来的连接并清空引用。
   */
  connect(): Promise<void> {
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
      await this.connectAllAsync(generation);
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
  private async connectAllAsync(generation: number): Promise<void> {
    if (this.hasLiveConnections()) {
      return;
    }

    // 手上的连接已经死了（自动重连耗尽）或只剩一半：先清干净再重建。
    // 少了这一步，早退会把应用永久留在断线状态，不早退又会泄漏。
    await this.disconnect();

    // await 之后先核对一次：连接尚未建立就已经不是当前主体，直接不建。
    // 连接一旦写进字段，下面那些 isCurrent() 的身份比对就一律为真——
    // 身份判据能排除"已退休的旧连接"，排除不了"旧请求在 reset 之后新建的连接"。
    //
    // 约束：本行到两个 Hub 建连方法里的字段赋值之间**不得插入 await**。
    // 一旦插入，reset 可以在核对之后、赋值之前发生，这道防护就静默失效了。
    if (generation !== this.generation) {
      return;
    }

    try {
      await Promise.all([this.connectNotificationHub(), this.connectBusinessHub()]);

      if (generation !== this.generation) {
        // 连接期间发生了主体切换：这对连接握的是上一个身份，不能留给下一个用户。
        await this.disconnect();
      }
    } catch (err) {
      console.error('[SignalR] Connection failed:', err);

      // 回滚本轮的全部连接：Promise.all 只在第一个失败时拒绝，另一条可能已经连上了。
      await this.disconnect();
    }
  }

  /**
   * 认证主体切换时清空一切与该主体绑定的状态。
   *
   * SignalR 的 principal 在握手时定死，连接不会因为前端清掉用户信号而重新授权。
   * 不断开就换人登录，下一个用户会复用上一个人的活连接，以对方的身份继续收消息，
   * 内存里的通知列表也照样留在界面上——这不是残留，是跨用户的数据泄漏。
   *
   * resourceEventNames 不清：那是应用关心哪些事件名，与主体无关，
   * 清掉会让重连后所有监听失效。
   */
  async reset(): Promise<void> {
    this.generation++;
    this.subscribedResources.clear();
    this.notifications.set([]);
    this.lastResourceEvent.set(null);

    await this.disconnect();
  }

  private hasLiveConnections(): boolean {
    return isLive(this.notificationConnection) && isLive(this.businessConnection);
  }

  /** 断开所有连接。无论 stop 是否抛错，引用一律清空——留着就等于泄漏。 */
  async disconnect(): Promise<void> {
    const connections = [this.notificationConnection, this.businessConnection];
    this.notificationConnection = null;
    this.businessConnection = null;
    this.notificationConnected.set(false);
    this.businessConnected.set(false);

    for (const connection of connections) {
      if (!connection) {
        continue;
      }

      try {
        await connection.stop();
      } catch (err) {
        console.error('[SignalR] stop failed:', err);
      }
    }
  }

  /** 注册一个业务事件名监听（推送到 lastResourceEvent 信号）。 */
  registerResourceEvent(eventName: string): void {
    if (this.resourceEventNames.has(eventName)) return;
    this.resourceEventNames.add(eventName);

    const connection = this.businessConnection;
    connection?.on(eventName, (payload: unknown) => {
      // 动态注册的监听同样要判身份：注册时那条连接可能在主体切换后才收到事件。
      if (this.businessConnection !== connection) {
        return;
      }

      this.lastResourceEvent.set({ eventName, payload });
    });
  }

  /** 订阅资源变更。 */
  async subscribeResource(resourceKey: string): Promise<void> {
    const conn = this.businessConnection;
    if (!conn) return;

    const generation = this.generation;
    try {
      if (conn.state === 'Connected') {
        await conn.invoke('Subscribe', resourceKey);
      }

      // reset() 清过集合之后才回填，等于把上一个主体的订阅塞回下一个人名下；
      // 后端的订阅授权默认关闭，那个 key 会被真的重新订阅上。
      if (this.isCurrentGeneration(generation)) {
        this.subscribedResources.add(resourceKey);
      }
    } catch (err) {
      console.error('[SignalR] subscribeResource failed:', err);
    }
  }

  /** 取消订阅资源变更。 */
  async unsubscribeResource(resourceKey: string): Promise<void> {
    this.subscribedResources.delete(resourceKey);
    if (this.businessConnection?.state === 'Connected') {
      await this.businessConnection.invoke('Unsubscribe', resourceKey);
    }
  }

  // ── 内部 ──

  /**
   * 解析 Hub 绝对地址。
   *
   * SignalR 的 HubConnectionBuilder 不经过 Angular HTTP 拦截器，
   * 因此需在此手动拼接 `environment.api.gateway` 前缀（与 urlFormatInterceptor 一致）。
   * 网关为空时返回相对路径，由浏览器按当前源解析（同源托管场景）。
   */
  private resolveHubUrl(path: string): string {
    const gateway = environment.api.gateway || '';
    const gatewayPart = gateway.endsWith('/') ? gateway.slice(0, -1) : gateway;
    return gatewayPart ? `${gatewayPart}${path}` : path;
  }

  private async connectNotificationHub(): Promise<void> {
    const connection = new HubConnectionBuilder()
      .withUrl(this.resolveHubUrl('/hubs/notifications'), {
        transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling,
        //#if (ResourceService)
        accessTokenFactory: () => firstValueFrom(this.oidc.getAccessToken()),
        //#endif
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Information)
      .build();

    // 每个回调都先确认自己仍是当前那条连接。判据用身份而不是代际：
    // stop() 是异步的，旧连接的推送与状态回调可能晚于主体切换才到达，
    // 而 disconnect() 已经把字段置空，身份比对天然为假。
    // 身份判据与它保护的对象绑在一起，不会出现"又漏了一处没加检查"。
    const isCurrent = () => this.notificationConnection === connection;

    connection.on('NotificationReceived', (notification: NotificationOutputDto) => {
      if (!isCurrent()) {
        return;
      }

      this.notifications.update((list) => [notification, ...list]);
    });

    connection.onreconnecting(() => isCurrent() && this.notificationConnected.set(false));
    connection.onreconnected(() => isCurrent() && this.notificationConnected.set(true));
    connection.onclose(() => isCurrent() && this.notificationConnected.set(false));

    this.notificationConnection = connection;
    await connection.start();

    if (isCurrent()) {
      this.notificationConnected.set(true);
    }
  }

  private async connectBusinessHub(): Promise<void> {
    const connection = new HubConnectionBuilder()
      .withUrl(this.resolveHubUrl('/hubs/realtime'), {
        transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling,
        //#if (ResourceService)
        accessTokenFactory: () => firstValueFrom(this.oidc.getAccessToken()),
        //#endif
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Information)
      .build();

    const isCurrent = () => this.businessConnection === connection;

    // 重新挂载已注册的事件监听。判据同样是身份，理由见通知 Hub。
    for (const eventName of this.resourceEventNames) {
      connection.on(eventName, (payload: unknown) => {
        if (!isCurrent()) {
          return;
        }

        this.lastResourceEvent.set({ eventName, payload });
      });
    }

    // 业务 Hub 同样要维护自身状态：只有通知 Hub 上报时，它单独掉线不会被察觉。
    connection.onreconnecting(() => isCurrent() && this.businessConnected.set(false));
    connection.onclose(() => isCurrent() && this.businessConnected.set(false));
    connection.onreconnected(async () => {
      if (!isCurrent()) {
        return;
      }

      this.businessConnected.set(true);

      // 先取快照再迭代：集合会被 reset 清空、被下一个主体重新填充，
      // 跨 await 直接迭代活集合，旧回调会读到新主体的 key。
      for (const resourceKey of [...this.subscribedResources]) {
        // 每轮复核身份：主体可能在上一次 invoke 期间切换，
        // 继续下去就是拿上一个人的连接去订阅剩下的资源。
        if (!isCurrent()) {
          return;
        }

        // 重订阅打在自己这条连接上，不走字段——旧连接的晚到回调若读字段，
        // 会拿当前主体的连接去订阅上一个人的资源。
        try {
          await connection.invoke('Subscribe', resourceKey);
        } catch (err) {
          console.error('[SignalR] re-subscribe failed:', resourceKey, err);
        }
      }
    });

    this.businessConnection = connection;
    await connection.start();

    if (isCurrent()) {
      this.businessConnected.set(true);
    }
  }
}
