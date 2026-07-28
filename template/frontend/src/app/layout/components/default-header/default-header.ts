//#if (IncludeNotifications)
import { DatePipe } from '@angular/common';
//#endif
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { Router, RouterModule } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBell,
  lucideChevronDown,
  lucideCog,
  lucideHouse,
  lucideLock,
  lucideLogOut,
  lucideUserPen,
  lucideBadgeCheck,
  //#if (IncludeNotifications)
  lucideDatabase,
  lucideInbox,
  lucideInfo,
  lucideNetwork,
  //#endif
} from '@ng-icons/lucide';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
import { HlmSidebarTrigger } from '@spartan-ng/helm/sidebar';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

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
//#if (IncludeIdentity)
import { ChangePasswordDialog } from '../../../features/account/components/change-password-dialog/change-password-dialog';
import { ProfileSettingsDialog } from '../../../features/account/components/profile-settings-dialog/profile-settings-dialog';
//#endif
//#if (IncludeLocalization)
import { LanguageSwitcher } from '../../../shared/components/language-switcher/language-switcher';
//#endif
import { ThemeModeToggle } from '../../../shared/components/theme-mode-toggle/theme-mode-toggle';
import { LayoutService } from '../../services/layout-service';

/** 用户下拉菜单项：普通项（label + lucide 图标 + 动作）或分隔线。 */
interface UserMenuItem {
  label?: string;
  icon?: string;
  action?: () => void;
  separator?: boolean;
}

@Component({
  selector: 'app-default-header',
  standalone: true,
  imports: [
    RouterModule,
    NgIcon,
    HlmButton,
    ThemeModeToggle,
    ...HlmAvatarImports,
    ...HlmDropdownMenuImports,
    ...HlmTooltipImports,
    ...HlmPopoverImports,
    HlmSidebarTrigger,
    //#if (IncludeLocalization)
    LanguageSwitcher,
    TranslocoModule,
    //#endif
    //#if (IncludeNotifications)
    DatePipe,
    //#endif
    //#if (IncludeIdentity)
    ProfileSettingsDialog,
    ChangePasswordDialog,
    //#endif
  ],
  providers: [
    provideIcons({
      lucideBell,
      lucideChevronDown,
      lucideHouse,
      lucideCog,
      lucideUserPen,
      lucideLock,
      lucideLogOut,
      lucideBadgeCheck,
      //#if (IncludeNotifications)
      lucideInbox,
      lucideDatabase,
      lucideNetwork,
      lucideInfo,
      //#endif
    }),
  ],
  templateUrl: './default-header.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DefaultHeader implements OnInit {
  readonly layoutService = inject(LayoutService);
  readonly authService = inject(AuthService);
  readonly router = inject(Router);
  //#if (IncludeLocalization)
  readonly languageService = inject(LanguageService);
  private readonly transloco = inject(TranslocoService);
  //#endif

  //#if (IncludeIdentity)
  // 管理员徽标 tooltip：模板属性区不支持内嵌条件指令，用 getter 承载条件文案。
  //#if (IncludeLocalization)
  readonly adminTooltip = () => this.transloco.translate('role.admin');
  //#else
  readonly adminTooltip = () => 'Administrator';
  //#endif
  //#endif
  //#if (IncludeNotifications)
  readonly notificationService = inject(NotificationService);
  readonly notificationCount = this.notificationService.unreadCount;
  readonly notifications = this.notificationService.notifications;
  // 通知 popover 开合状态（Spartan popover 的 state 受控绑定）。
  readonly notificationOpen = signal<'open' | 'closed'>('closed');

  // 铃铛 tooltip：模板属性区不支持内嵌条件指令，故用 getter 承载条件文案。
  //#if (IncludeLocalization)
  readonly notificationTooltip = () => this.transloco.translate('layout.notifications.title');
  //#else
  readonly notificationTooltip = () => 'Notifications';
  //#endif

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
  readonly userMenuItems = computed<UserMenuItem[]>(() => {
    const currentUser = this.authService.currentUser();
    //#if (IncludeLocalization)
    // 建立对活动语言的依赖，语言切换时重新计算菜单文案
    this.languageService.activeLang();
    const t = (key: string) => this.transloco.translate(key);
    //#endif
    const items: UserMenuItem[] = [];

    if (this.router.url.startsWith('/platform')) {
      items.push({
        //#if (IncludeLocalization)
        label: t('menu.workspace'),
        //#else
        label: 'Workspace',
        //#endif
        icon: 'lucideHouse',
        action: () => this.router.navigate(['/workspace']),
      });
    } else if (this.router.url.startsWith('/workspace') && currentUser?.isAdmin()) {
      items.push({
        //#if (IncludeLocalization)
        label: t('menu.platform'),
        //#else
        label: 'Admin platform',
        //#endif
        icon: 'lucideCog',
        action: () => this.router.navigate(['/platform']),
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
        icon: 'lucideUserPen',
        action: () => this.openProfileDialog(),
      },
      {
        //#if (IncludeLocalization)
        label: t('menu.changePassword'),
        //#else
        label: 'Change password',
        //#endif
        icon: 'lucideLock',
        action: () => this.openChangePasswordDialog(),
      },
      { separator: true },
      {
        //#if (IncludeLocalization)
        label: t('menu.logout'),
        //#else
        label: 'Sign out',
        //#endif
        icon: 'lucideLogOut',
        action: () => this.handleLogout(),
      },
    );
    //#endif
    return items;
  });

  //#if (IncludeIdentity)
  openProfileDialog(): void {
    this.profileDialogVisible.set(true);
  }

  openChangePasswordDialog(): void {
    this.profileDialogVisible.set(false);
    this.changePasswordDialogVisible.set(true);
  }

  handleLogout(): void {
    this.authService.logout();
  }
  //#endif
}
