//#if (IncludeNotifications)
import { DatePipe } from '@angular/common';
//#endif
import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  OnInit,
  ViewChild,
  computed,
  inject,
  output,
  signal,
} from '@angular/core';
import { Router, RouterModule } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { MenuItem } from 'primeng/api';
import { AvatarModule } from 'primeng/avatar';
import { BadgeModule } from 'primeng/badge';
import { ButtonModule } from 'primeng/button';
//#if (IncludeNotifications)
import { DividerModule } from 'primeng/divider';
//#endif
import { MenuModule, Menu } from 'primeng/menu';
//#if (IncludeNotifications)
import { OverlayBadgeModule } from 'primeng/overlaybadge';
import { PopoverModule, Popover } from 'primeng/popover';
//#endif
import { StyleClassModule } from 'primeng/styleclass';
import { TooltipModule } from 'primeng/tooltip';

import { AuthService } from '../../../core/services/auth-service';
//#if (IncludeLocalization)
import { LanguageService } from '../../../core/services/language-service';
//#endif
//#if (IncludeNotifications)
import {
  NotificationService,
  NotificationOutputDto,
} from '../../../core/services/notification-service';
//#endif
import { ThemeService } from '../../../core/services/theme-service';
//#if (IncludeIdentity)
import { ChangePasswordDialogComponent } from '../../../features/account/components/change-password-dialog/change-password-dialog';
import { ProfileSettingsDialogComponent } from '../../../features/account/components/profile-settings-dialog/profile-settings-dialog';
//#endif
//#if (IncludeLocalization)
import { LanguageSwitcher } from '../../../shared/components/language-switcher/language-switcher';
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
    //#if (IncludeLocalization)
    LanguageSwitcher,
    TranslocoModule,
    //#endif
    //#if (IncludeNotifications)
    PopoverModule,
    OverlayBadgeModule,
    DividerModule,
    DatePipe,
    //#endif
    //#if (IncludeIdentity)
    ProfileSettingsDialogComponent,
    ChangePasswordDialogComponent,
    //#endif
  ],
  templateUrl: './default-header.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DefaultHeader implements OnInit, OnDestroy {
  readonly layoutService = inject(LayoutService);
  readonly themeService = inject(ThemeService);
  readonly authService = inject(AuthService);
  readonly router = inject(Router);
  //#if (IncludeLocalization)
  readonly languageService = inject(LanguageService);
  private readonly transloco = inject(TranslocoService);
  //#endif

  @ViewChild('userMenu') userMenu!: Menu;

  //#if (IncludeIdentity)
  // 管理员徽标 tooltip：模板属性区不支持内嵌条件指令，用 getter 承载条件文案。
  //#if (IncludeLocalization)
  readonly adminTooltip = () => this.transloco.translate('role.admin');
  //#else
  readonly adminTooltip = () => 'Administrator';
  //#endif
  //#endif
  readonly toggleMobileMenu = output<void>();
  //#if (IncludeNotifications)
  readonly notificationService = inject(NotificationService);
  readonly notificationCount = this.notificationService.unreadCount;
  readonly notifications = this.notificationService.notifications;
  @ViewChild('notificationPopover') notificationPopover!: Popover;

  // 铃铛 tooltip：模板属性区不支持内嵌条件指令，故用 getter 承载条件文案。
  //#if (IncludeLocalization)
  readonly notificationTooltip = () => this.transloco.translate('layout.notifications.title');
  //#else
  readonly notificationTooltip = () => 'Notifications';
  //#endif

  toggleNotifications(event: Event): void {
    this.notificationPopover?.toggle(event);
  }

  async onNotificationClick(item: NotificationOutputDto): Promise<void> {
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
  //#else
  readonly notificationCount = signal(0);
  //#endif

  ngOnInit(): void {
    //#if (IncludeNotifications)
    void this.notificationService.init();
    //#else
    this.notificationCount.set(0);
    //#endif
  }

  //#if (IncludeIdentity)
  readonly profileDialogVisible = signal(false);
  readonly changePasswordDialogVisible = signal(false);
  //#endif
  readonly userMenuItems = computed<MenuItem[]>(() => {
    const currentUser = this.authService.currentUser();
    //#if (IncludeLocalization)
    // 建立对活动语言的依赖，语言切换时重新计算菜单文案
    this.languageService.activeLang();
    const t = (key: string) => this.transloco.translate(key);
    //#endif
    const items: MenuItem[] = [];

    if (this.router.url.startsWith('/platform')) {
      items.push({
        //#if (IncludeLocalization)
        label: t('menu.workspace'),
        //#else
        label: 'Workspace',
        //#endif
        icon: 'pi pi-home',
        command: () => this.closeMenuAndNavigate('/workspace'),
      });
    } else if (this.router.url.startsWith('/workspace') && currentUser?.isAdmin()) {
      items.push({
        //#if (IncludeLocalization)
        label: t('menu.platform'),
        //#else
        label: 'Admin platform',
        //#endif
        icon: 'pi pi-cog',
        command: () => this.closeMenuAndNavigate('/platform'),
      });
    }

    //#if (IncludeIdentity)
    if (items.length > 0) {
      items.push({ separator: true });
    }

    items.push(
      {
        //#if (IncludeLocalization)
        label: t('menu.profile'),
        //#else
        label: 'Profile',
        //#endif
        icon: 'pi pi-user-edit',
        command: () => this.openProfileDialog(),
      },
      {
        //#if (IncludeLocalization)
        label: t('menu.changePassword'),
        //#else
        label: 'Change password',
        //#endif
        icon: 'pi pi-lock',
        command: () => this.openChangePasswordDialog(),
      },
      {
        separator: true,
      },
      {
        //#if (IncludeLocalization)
        label: t('menu.logout'),
        //#else
        label: 'Sign out',
        //#endif
        icon: 'pi pi-sign-out',
        command: () => this.handleLogout(),
      },
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
      overlays.forEach((overlay) => overlay.remove());
    }
  }

  ngOnDestroy(): void {
    this.forceCloseMenu();
  }
}
