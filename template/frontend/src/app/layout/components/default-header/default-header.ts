import { ChangeDetectionStrategy, Component, OnDestroy, ViewChild, computed, inject, output, signal } from '@angular/core';
import { Router, RouterModule } from '@angular/router';
import { MenuItem } from 'primeng/api';
import { AvatarModule } from 'primeng/avatar';
import { BadgeModule } from 'primeng/badge';
import { ButtonModule } from 'primeng/button';
import { MenuModule, Menu } from 'primeng/menu';
import { StyleClassModule } from 'primeng/styleclass';
import { TooltipModule } from 'primeng/tooltip';
//#if (IncludeNotifications)
import { OnInit } from '@angular/core';
import { DatePipe } from '@angular/common';
import { PopoverModule, Popover } from 'primeng/popover';
import { OverlayBadgeModule } from 'primeng/overlaybadge';
//#endif

import { AuthService } from '../../../core/services/auth-service';
//#if (IncludeNotifications)
import { NotificationService, AppNotification } from '../../../core/services/notification-service';
//#endif
//#if (IncludeIdentity)
import { ChangePasswordDialogComponent } from '../../../features/account/components/change-password-dialog/change-password-dialog';
import { ProfileSettingsDialogComponent } from '../../../features/account/components/profile-settings-dialog/profile-settings-dialog';
//#endif
import { ThemeConfigurator } from '../../../shared/components/theme-configurator/theme-configurator';
import { LayoutService } from '../../services/layout-service';

@Component({
  selector: 'app-default-header',
  standalone: true,
  imports: [
    RouterModule,
    ButtonModule,
    AvatarModule,
    BadgeModule,
    StyleClassModule,
    TooltipModule,
    MenuModule,
    ThemeConfigurator,
//#if (IncludeNotifications)
    PopoverModule,
    OverlayBadgeModule,
    DatePipe,
//#endif
//#if (IncludeIdentity)
    ProfileSettingsDialogComponent,
    ChangePasswordDialogComponent
//#endif
  ],
  templateUrl: './default-header.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
//#if (IncludeNotifications)
export class DefaultHeader implements OnInit, OnDestroy {
//#else
export class DefaultHeader implements OnDestroy {
//#endif
  readonly layoutService = inject(LayoutService);
  readonly authService = inject(AuthService);
  readonly router = inject(Router);

  @ViewChild('userMenu') userMenu!: Menu;

  readonly toggleMobileMenu = output<void>();
//#if (IncludeNotifications)
  readonly notificationService = inject(NotificationService);
  readonly notificationCount = this.notificationService.unreadCount;
  readonly notifications = this.notificationService.notifications;
  @ViewChild('notificationPopover') notificationPopover!: Popover;

  ngOnInit(): void {
    // 登录后初始化：连接 SignalR + 加载历史通知
    void this.notificationService.init();
  }

  toggleNotifications(event: Event): void {
    this.notificationPopover?.toggle(event);
  }

  async onNotificationClick(item: AppNotification): Promise<void> {
    if (!item.isRead) {
      await this.notificationService.markAsRead(item.id);
    }
    if (item.link) {
      this.notificationPopover?.hide();
      this.router.navigateByUrl(item.link);
    }
  }

  async markAllNotificationsRead(): Promise<void> {
    await this.notificationService.markAllAsRead();
  }

  notificationIcon(type: string): string {
    return this.notificationService.getIcon(type);
  }

  notificationColor(type: string): string {
    return this.notificationService.getColor(type);
  }
//#else
  readonly notificationCount = signal(0);
//#endif
//#if (IncludeIdentity)
  readonly profileDialogVisible = signal(false);
  readonly changePasswordDialogVisible = signal(false);
//#endif

  readonly userMenuItems = computed<MenuItem[]>(() => {
    const currentUser = this.authService.currentUser();
    const items: MenuItem[] = [];

    if (this.router.url.startsWith('/platform')) {
      items.push({
        label: '工作空间',
        icon: 'pi pi-home',
        command: () => this.closeMenuAndNavigate('/workspace')
      });
    } else if (this.router.url.startsWith('/workspace') && currentUser?.isAdmin()) {
      items.push({
        label: '管理平台',
        icon: 'pi pi-cog',
        command: () => this.closeMenuAndNavigate('/platform')
      });
    }

//#if (IncludeIdentity)
    if (items.length > 0) {
      items.push({ separator: true });
    }

    items.push(
      {
        label: '个人信息',
        icon: 'pi pi-user-edit',
        command: () => this.openProfileDialog()
      },
      {
        label: '修改密码',
        icon: 'pi pi-lock',
        command: () => this.openChangePasswordDialog()
      },
      {
        separator: true
      },
      {
        label: '退出登录',
        icon: 'pi pi-sign-out',
        command: () => this.handleLogout()
      }
    );
//#endif

    return items;
  });

//#if (IncludeIdentity)
  openProfileDialog(): void {
    this.forceCloseMenu();
    this.profileDialogVisible.set(true);
  }

  openChangePasswordDialog(): void {
    this.forceCloseMenu();
    this.profileDialogVisible.set(false);
    this.changePasswordDialogVisible.set(true);
  }

  handleLogout(): void {
    this.forceCloseMenu();
    this.authService.logout();
  }
//#endif

  handleMenuToggle(): void {
    if (this.layoutService.isMobileSidebarMode()) {
      this.toggleMobileMenu.emit();
      return;
    }

    this.layoutService.toggleSidebarCollapse();
  }

  private closeMenuAndNavigate(path: string): void {
    this.forceCloseMenu();
    this.router.navigate([path]);
  }

  private forceCloseMenu(): void {
    try {
      if (this.userMenu) {
        this.userMenu.hide();
      }
    } catch {
      // 忽略关闭异常，由兜底清理机制处理
    } finally {
      setTimeout(() => this.cleanupOverlay(), 50);
    }
  }

  private cleanupOverlay(): void {
    const overlays = document.querySelectorAll('.p-menu-overlay, .p-component-overlay');
    if (overlays.length > 0) {
      overlays.forEach(overlay => overlay.remove());
    }
  }

  ngOnDestroy(): void {
    this.forceCloseMenu();
  }
}
