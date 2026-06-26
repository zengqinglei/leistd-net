import { Injectable, signal, computed } from '@angular/core';
import {
  HubConnectionBuilder,
  HubConnection,
  LogLevel,
  HttpTransportType
} from '@microsoft/signalr';

/** 通知 DTO（与后端 Leistd.Notifications.AppNotification 对应，类型为字符串） */
export interface AppNotification {
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
 * 认证：模板使用 Cookie 会话，WebSocket 同源连接自动携带 Cookie，无需手动传 token。
 */
@Injectable({ providedIn: 'root' })
export class SignalRService {
  private notificationConnection: HubConnection | null = null;
  private businessConnection: HubConnection | null = null;

  // ── 通知状态 ──
  readonly notifications = signal<AppNotification[]>([]);
  readonly unreadCount = computed(() => this.notifications().filter(n => !n.isRead).length);

  // ── 业务事件（通用）：最近一次收到的资源事件 ──
  readonly lastResourceEvent = signal<{ eventName: string; payload: unknown } | null>(null);

  // ── 连接状态 ──
  readonly isConnected = signal(false);

  // ── 已订阅资源（重连后重新订阅） ──
  private readonly subscribedResources = new Set<string>();
  private readonly resourceEventNames = new Set<string>();

  /** 建立 SignalR 连接（在用户登录后调用）。 */
  async connect(): Promise<void> {
    try {
      await Promise.all([this.connectNotificationHub(), this.connectBusinessHub()]);
      this.isConnected.set(true);
    } catch (err) {
      console.error('[SignalR] Connection failed:', err);
      this.isConnected.set(false);
    }
  }

  /** 断开所有连接。 */
  async disconnect(): Promise<void> {
    if (this.notificationConnection) {
      await this.notificationConnection.stop();
      this.notificationConnection = null;
    }
    if (this.businessConnection) {
      await this.businessConnection.stop();
      this.businessConnection = null;
    }
    this.isConnected.set(false);
  }

  /** 注册一个业务事件名监听（推送到 lastResourceEvent 信号）。 */
  registerResourceEvent(eventName: string): void {
    if (this.resourceEventNames.has(eventName)) return;
    this.resourceEventNames.add(eventName);
    this.businessConnection?.on(eventName, (payload: unknown) =>
      this.lastResourceEvent.set({ eventName, payload })
    );
  }

  /** 订阅资源变更。 */
  async subscribeResource(resourceKey: string): Promise<void> {
    const conn = this.businessConnection;
    if (!conn) return;
    try {
      if (conn.state === 'Connected') {
        await conn.invoke('Subscribe', resourceKey);
      }
      this.subscribedResources.add(resourceKey);
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

  private async connectNotificationHub(): Promise<void> {
    this.notificationConnection = new HubConnectionBuilder()
      .withUrl('/hubs/notifications', {
        transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Information)
      .build();

    this.notificationConnection.on('NotificationReceived', (notification: AppNotification) => {
      this.notifications.update(list => [notification, ...list]);
    });

    this.notificationConnection.onreconnecting(() => this.isConnected.set(false));
    this.notificationConnection.onreconnected(() => this.isConnected.set(true));

    await this.notificationConnection.start();
  }

  private async connectBusinessHub(): Promise<void> {
    this.businessConnection = new HubConnectionBuilder()
      .withUrl('/hubs/realtime', {
        transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Information)
      .build();

    // 重新挂载已注册的事件监听
    for (const eventName of this.resourceEventNames) {
      this.businessConnection.on(eventName, (payload: unknown) =>
        this.lastResourceEvent.set({ eventName, payload })
      );
    }

    this.businessConnection.onreconnected(async () => {
      for (const resourceKey of this.subscribedResources) {
        try {
          await this.businessConnection!.invoke('Subscribe', resourceKey);
        } catch (err) {
          console.error('[SignalR] re-subscribe failed:', resourceKey, err);
        }
      }
    });

    await this.businessConnection.start();
  }
}
