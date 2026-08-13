import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { lastValueFrom } from 'rxjs';

import { environment } from '../../../../environments/environment';
import { SignalRService, NotificationOutputDto } from '../../../core/services/signalr-service';
export type { NotificationOutputDto } from '../../../core/services/signalr-service';

/**
 * 通知管理服务：通知列表/已读（HTTP）+ SignalR 实时推送桥接。
 * 铃铛面板使用此服务获取数据。
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly http = inject(HttpClient);
  private readonly signalR = inject(SignalRService);

  /** 通知列表（与 SignalR 推送共享同一信号）。 */
  readonly notifications = this.signalR.notifications;

  /** 未读数量。 */
  readonly unreadCount = this.signalR.unreadCount;

  /** 加载状态。 */
  readonly loading = signal(false);

  /** 初始化：加载历史通知 + 连接 SignalR。 */
  async init(): Promise<void> {
    const generation = this.signalR.authGeneration;

    await this.loadNotifications();

    const useMock = environment.useMock;
    if (typeof useMock === 'boolean' ? useMock : useMock.enable) {
      return;
    }

    // 加载历史期间发生了主体切换：这一轮 init 属于上一个用户，不能再去建连。
    // 服务端 Cookie 此刻可能仍然有效，建成的连接会把 principal 定在上一个人身上，
    // 下一个用户的 connect() 见到活连接就直接复用了它。
    if (!this.signalR.isCurrentGeneration(generation)) {
      return;
    }

    await this.signalR.connect();
  }

  /** 加载通知列表。 */
  async loadNotifications(maxCount = 50): Promise<void> {
    // 记下发起请求时的认证代际：请求在途时登出/换人登录，响应回来仍会写同一个
    // 共享 signal——那就是把上一个用户的历史通知落到下一个人的界面上。
    const generation = this.signalR.authGeneration;

    this.loading.set(true);
    try {
      const items = await lastValueFrom(
        this.http.get<NotificationOutputDto[]>('/api/v1/notifications', {
          params: { maxCount: maxCount.toString() },
        }),
      );

      const incoming = items ?? [];
      // 合并 SignalR 已推送但不在历史中的通知
      const pushed = this.signalR.notifications();
      const merged = [...incoming];
      for (const n of pushed) {
        if (!merged.some((m) => m.id === n.id)) {
          merged.unshift(n);
        }
      }
      merged.sort(
        (a, b) => new Date(b.creationTime).getTime() - new Date(a.creationTime).getTime(),
      );

      if (!this.signalR.isCurrentGeneration(generation)) {
        return;
      }

      this.signalR.notifications.set(merged);
    } catch (err) {
      console.error('[NotificationService] Load failed:', err);
    } finally {
      this.loading.set(false);
    }
  }

  /** 标记单条已读。 */
  async markAsRead(notificationId: string): Promise<void> {
    const generation = this.signalR.authGeneration;
    await lastValueFrom(this.http.put(`/api/v1/notifications/${notificationId}/read`, {}));

    if (!this.signalR.isCurrentGeneration(generation)) {
      return;
    }

    this.signalR.notifications.update((list) =>
      list.map((n) => (n.id === notificationId ? { ...n, isRead: true } : n)),
    );
  }

  /**
   * 全部标记已读。写操作失败时抛出（拦截器已归一化为 ApplicationHttpError），
   * 由调用方决定反馈方式；本地状态只在成功后更新。
   */
  async markAllAsRead(): Promise<void> {
    const generation = this.signalR.authGeneration;
    await lastValueFrom(this.http.put('/api/v1/notifications/read-all', {}));

    if (!this.signalR.isCurrentGeneration(generation)) {
      return;
    }

    this.signalR.notifications.update((list) => list.map((n) => ({ ...n, isRead: true })));
  }

  /** 清空全部通知（持久删除）。 */
  async clearAll(): Promise<void> {
    const generation = this.signalR.authGeneration;
    await lastValueFrom(this.http.delete('/api/v1/notifications'));

    // A 的 clearAll 在途、B 登录并加载完自己的列表，这一句会把 B 的列表清空。
    if (!this.signalR.isCurrentGeneration(generation)) {
      return;
    }

    this.signalR.notifications.set([]);
  }

  /** 删除单条通知（持久删除）。 */
  async clearOne(notificationId: string): Promise<void> {
    const generation = this.signalR.authGeneration;
    await lastValueFrom(this.http.delete(`/api/v1/notifications/${notificationId}`));

    if (!this.signalR.isCurrentGeneration(generation)) {
      return;
    }

    this.signalR.notifications.update((list) => list.filter((n) => n.id !== notificationId));
  }

  /** 通知类型图标（lucide 图标名；仅区分形状，颜色统一由视图控制）。 */
  getIcon(type: string): string {
    switch (type) {
      case 'DataChange':
        return 'lucideDatabase';
      case 'Workflow':
        return 'lucideNetwork';
      case 'System':
      default:
        return 'lucideInfo';
    }
  }
}
