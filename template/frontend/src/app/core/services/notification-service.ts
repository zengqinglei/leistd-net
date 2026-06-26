import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { lastValueFrom } from 'rxjs';

import { SignalRService, AppNotification } from './signalr-service';
export type { AppNotification } from './signalr-service';

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
    await this.loadNotifications();
    await this.signalR.connect();
  }

  /** 加载通知列表。 */
  async loadNotifications(maxCount = 50): Promise<void> {
    this.loading.set(true);
    try {
      const items = await lastValueFrom(
        this.http.get<AppNotification[]>('/api/v1/notifications', {
          params: { maxCount: maxCount.toString() }
        })
      );

      const incoming = items ?? [];
      // 合并 SignalR 已推送但不在历史中的通知
      const pushed = this.signalR.notifications();
      const merged = [...incoming];
      for (const n of pushed) {
        if (!merged.some(m => m.id === n.id)) {
          merged.unshift(n);
        }
      }
      merged.sort((a, b) => new Date(b.creationTime).getTime() - new Date(a.creationTime).getTime());
      this.signalR.notifications.set(merged);
    } catch (err) {
      console.error('[NotificationService] Load failed:', err);
    } finally {
      this.loading.set(false);
    }
  }

  /** 标记单条已读。 */
  async markAsRead(notificationId: string): Promise<void> {
    try {
      await lastValueFrom(this.http.put(`/api/v1/notifications/${notificationId}/read`, {}));
      this.signalR.notifications.update(list =>
        list.map(n => (n.id === notificationId ? { ...n, isRead: true } : n))
      );
    } catch (err) {
      console.error('[NotificationService] MarkAsRead failed:', err);
    }
  }

  /** 全部标记已读。 */
  async markAllAsRead(): Promise<void> {
    try {
      await lastValueFrom(this.http.put('/api/v1/notifications/read-all', {}));
      this.signalR.notifications.update(list => list.map(n => ({ ...n, isRead: true })));
    } catch (err) {
      console.error('[NotificationService] MarkAllAsRead failed:', err);
    }
  }

  /** 通知类型图标。 */
  getIcon(type: string): string {
    switch (type) {
      case 'DataChange': return 'pi pi-database';
      case 'Workflow': return 'pi pi-sitemap';
      case 'System':
      default: return 'pi pi-info-circle';
    }
  }

  /** 通知类型颜色。 */
  getColor(type: string): string {
    switch (type) {
      case 'DataChange': return 'text-blue-500';
      case 'Workflow': return 'text-orange-500';
      case 'System':
      default: return 'text-gray-500';
    }
  }
}
