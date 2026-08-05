import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBell,
  lucideDatabase,
  lucideInbox,
  lucideInfo,
  lucideNetwork,
  lucideTrash2,
} from '@ng-icons/lucide';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

import { NotificationOutputDto, NotificationService } from './notification-service';
import { ConfirmService } from '../../../core/feedback/confirm-service';

/**
 * 通知中心：铃铛 + 未读角标 + popover 通知列表（标记已读 / 单条或全部清除）。
 * 独立共享组件，供 header 直接引用；仅在启用通知功能时编译（见 template.json 排除规则）。
 */
@Component({
  selector: 'app-notifications',
  standalone: true,
  host: { class: 'inline-flex' },
  // prettier-ignore
  imports: [
    NgIcon,
    HlmButton,
    HlmBadge,
    DatePipe,
    ...HlmPopoverImports,
    ...HlmTooltipImports,
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
  ],
  providers: [
    provideIcons({
      lucideBell,
      lucideInbox,
      lucideDatabase,
      lucideNetwork,
      lucideInfo,
      lucideTrash2,
    }),
  ],
  templateUrl: './notifications.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Notifications implements OnInit {
  private readonly router = inject(Router);
  private readonly confirmService = inject(ConfirmService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif
  readonly notificationService = inject(NotificationService);
  readonly notificationCount = this.notificationService.unreadCount;
  readonly notifications = this.notificationService.notifications;
  // 通知 popover 开合状态（Spartan popover 的 state 受控绑定）。
  readonly notificationOpen = signal<'open' | 'closed'>('closed');

  async onNotificationClick(item: NotificationOutputDto): Promise<void> {
    if (!item.isRead) {
      await this.notificationService.markAsRead(item.id);
    }
    if (item.link) {
      this.notificationOpen.set('closed');
      this.router.navigateByUrl(item.link);
    }
  }

  async markAllNotificationsRead(): Promise<void> {
    await this.notificationService.markAllAsRead();
  }

  async clearAllNotifications(): Promise<void> {
    // 清空不可撤销，先确认。
    const confirmed = await this.confirmService.open({
      //#if (IncludeLocalization)
      message: this.transloco.translate('layout.notifications.clearAllConfirm'),
      header: this.transloco.translate('layout.notifications.clearAll'),
      confirmText: this.transloco.translate('common.ok'),
      cancelText: this.transloco.translate('common.cancel'),
      //#else
      message: 'Clear all notifications? This cannot be undone.',
      header: 'Clear all',
      confirmText: 'OK',
      cancelText: 'Cancel',
      //#endif
      variant: 'destructive',
    });
    if (!confirmed) {
      return;
    }
    await this.notificationService.clearAll();
    this.notificationOpen.set('closed');
  }

  async clearOneNotification(id: string): Promise<void> {
    await this.notificationService.clearOne(id);
  }

  notificationIcon(type: string): string {
    return this.notificationService.getIcon(type);
  }

  ngOnInit(): void {
    void this.notificationService.init();
  }
}
