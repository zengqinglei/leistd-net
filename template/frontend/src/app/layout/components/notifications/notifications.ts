import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
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
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

import { NotificationOutputDto, NotificationService } from './notification-service';
import { applicationErrorMessage } from '../../../core/errors/application-http-error';
import { ConfirmService } from '../../../core/feedback/confirm-service';
//#if (IncludeLocalization)
import { translationReady } from '../../../core/i18n/translation-ready';
//#endif
import { SettingContextService } from '../../../core/settings/setting-context-service';
import { PopoverAria } from '../../../shared/directives/popover-aria';
import { AppDate } from '../../../shared/pipes/app-date-pipe';

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
    AppDate,
    ...HlmPopoverImports,
    ...HlmTooltipImports,
    PopoverAria,
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
  // 时间统一按设置里的展示时区渲染：服务端存 UTC，每处各自用浏览器时区
  // 会让同一时刻在不同页面显示成不同时间。
  protected readonly displayTimeZone = inject(SettingContextService).timeZone;
  protected readonly displayLocale = inject(SettingContextService).displayLocale;
  private readonly confirmService = inject(ConfirmService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  // 追踪「翻译就绪」：资源加载完成与语言切换时让下方 ARIA 文案 computed 重新求值。
  private readonly translationReady = translationReady(this.transloco);
  //#endif
  readonly notificationService = inject(NotificationService);
  readonly notificationCount = this.notificationService.unreadCount;
  readonly notifications = this.notificationService.notifications;
  // 通知 popover 开合状态（Spartan popover 的 state 受控绑定）。
  readonly notificationOpen = signal<'open' | 'closed'>('closed');

  /** 面板（overlay dialog）的可访问名。 */
  //#if (IncludeLocalization)
  readonly panelLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('layout.notifications.title');
  });
  //#else
  readonly panelLabel = computed(() => 'Notifications');
  //#endif

  /** 铃铛按钮的可访问名：本地化并带上未读数。 */
  readonly triggerLabel = computed(() => {
    const count = this.notificationCount();
    //#if (IncludeLocalization)
    this.translationReady();
    const title = this.transloco.translate('layout.notifications.title');
    return count > 0
      ? this.transloco.translate('layout.notifications.unreadAria', { count })
      : title;
    //#else
    return count > 0 ? `Notifications (${count} unread)` : 'Notifications';
    //#endif
  });

  async onNotificationClick(item: NotificationOutputDto): Promise<void> {
    if (!item.isRead) {
      try {
        await this.notificationService.markAsRead(item.id);
      } catch (error: unknown) {
        // 标记已读是附带动作：失败只提示，不阻断跳转这一主动作。
        this.showRequestError(error);
      }
    }
    if (item.link) {
      this.notificationOpen.set('closed');
      this.router.navigateByUrl(item.link);
    }
  }

  async markAllNotificationsRead(): Promise<void> {
    try {
      await this.notificationService.markAllAsRead();
    } catch (error: unknown) {
      this.showRequestError(error);
    }
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
    try {
      await this.notificationService.clearAll();
    } catch (error: unknown) {
      // 删除失败：保持面板打开并提示，避免「看起来成功、重开还在」。
      this.showRequestError(error);
      return;
    }
    this.notificationOpen.set('closed');
  }

  async clearOneNotification(id: string): Promise<void> {
    try {
      await this.notificationService.clearOne(id);
    } catch (error: unknown) {
      this.showRequestError(error);
    }
  }

  private showRequestError(error: unknown): void {
    //#if (IncludeLocalization)
    toast.error(this.transloco.translate('common.requestError'), {
      description: applicationErrorMessage(error),
    });
    //#else
    toast.error('Request failed', { description: applicationErrorMessage(error) });
    //#endif
  }

  notificationIcon(type: string): string {
    return this.notificationService.getIcon(type);
  }

  ngOnInit(): void {
    void this.notificationService.init();
  }
}
