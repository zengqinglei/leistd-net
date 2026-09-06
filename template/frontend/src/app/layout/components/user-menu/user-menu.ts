// prettier-ignore
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  //#if (LocalIdentity)
  signal,
  //#endif
} from '@angular/core';
import { Router } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
// prettier-ignore
import {
  lucideBuilding2,
  lucideChevronsUpDown,
  lucideCog,
  lucideHouse,
  lucideLock,
  lucideLogOut,
  lucideUserPen,
} from '@ng-icons/lucide';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';

import { AuthService } from '../../../core/services/auth-service';
import { AuthorizationService } from '../../../core/services/authorization-service';
//#if (IncludeLocalization)
import { LanguageService } from '../../../core/services/language-service';
//#endif
import { TenantContextService } from '../../../core/services/tenant-context-service';
//#if (LocalIdentity)
import { ChangePasswordDialog } from '../../../features/account/components/change-password-dialog/change-password-dialog';
import { ProfileSettingsDialog } from '../../../features/account/components/profile-settings-dialog/profile-settings-dialog';
//#endif
import { LayoutService } from '../../services/layout-service';

/** 用户菜单项：普通项（label + lucide 图标 + 动作）或分隔线。 */
interface UserMenuItem {
  label?: string;
  icon?: string;
  action?: () => void;
  separator?: boolean;
}

/**
 * 侧栏底部用户菜单（Spartan canonical：`hlm-sidebar-footer` 内的 nav-user 模式）。
 * 头像 + 姓名/邮箱两行 + 下拉（区段切换 / 个人信息 / 修改密码 / 退出），并承载对应对话框。
 * 折叠为图标时自动收成头像方块。
 */
@Component({
  selector: 'app-user-menu',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  // prettier-ignore
  imports: [
    NgIcon,
    ...HlmAvatarImports,
    ...HlmDropdownMenuImports,
    ...HlmSidebarImports,
    //#if (LocalIdentity)
    ProfileSettingsDialog,
    ChangePasswordDialog,
    //#endif
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
  ],
  // prettier-ignore
  providers: [
    provideIcons({
      lucideBuilding2,
      lucideChevronsUpDown,
      lucideHouse,
      lucideCog,
      lucideUserPen,
      lucideLock,
      lucideLogOut,
    }),
  ],
  template: `
    @if (authService.currentUser(); as user) {
      <ul hlmSidebarMenu>
        <li hlmSidebarMenuItem>
          <button
            hlmSidebarMenuButton
            size="lg"
            [closeMobileSidebarOnClick]="false"
            [hlmDropdownMenuTrigger]="userMenu"
            align="end"
            class="data-[state=open]:bg-sidebar-accent data-[state=open]:text-sidebar-accent-foreground"
          >
            <hlm-avatar class="rounded-lg">
              <img
                [src]="
                  user.avatar || 'https://api.dicebear.com/7.x/avataaars/svg?seed=' + user.username
                "
                hlmAvatarImage
                [alt]="user.username"
              />
              <span hlmAvatarFallback class="rounded-lg">{{
                (user.displayName || user.username)[0]
              }}</span>
            </hlm-avatar>
            <div class="grid flex-1 text-left text-sm leading-tight">
              <span class="truncate font-medium">{{ user.displayName || user.username }}</span>
              <span class="truncate text-xs text-muted-foreground">{{ user.email }}</span>
            </div>
            <ng-icon name="lucideChevronsUpDown" class="ml-auto text-base" />
          </button>

          <ng-template #userMenu>
            <hlm-dropdown-menu sideOffset="4" class="min-w-56 rounded-lg">
              <hlm-dropdown-menu-label>
                <div class="flex items-center gap-2 px-1 py-1.5 text-left text-sm">
                  <hlm-avatar class="rounded-lg">
                    <img
                      [src]="
                        user.avatar ||
                        'https://api.dicebear.com/7.x/avataaars/svg?seed=' + user.username
                      "
                      hlmAvatarImage
                      [alt]="user.username"
                    />
                    <span hlmAvatarFallback class="rounded-lg">{{
                      (user.displayName || user.username)[0]
                    }}</span>
                  </hlm-avatar>
                  <div class="grid flex-1 text-left text-sm leading-tight">
                    <span class="truncate font-medium">{{
                      user.displayName || user.username
                    }}</span>
                    <span class="truncate text-xs text-muted-foreground">{{ user.email }}</span>
                    <!-- 当前租户；未选租户即宿主（未启用多租户时恒为空串，不渲染）。 -->
                    @if (tenantLabel(); as label) {
                      <span class="truncate text-xs text-muted-foreground">{{ label }}</span>
                    }
                  </div>
                </div>
              </hlm-dropdown-menu-label>
              <hlm-dropdown-menu-separator />
              @for (item of userMenuItems(); track $index) {
                @if (item.separator) {
                  <hlm-dropdown-menu-separator />
                } @else {
                  <button hlmDropdownMenuItem (click)="item.action!()">
                    <ng-icon [name]="item.icon!" data-icon="inline-start" />
                    <span>{{ item.label }}</span>
                  </button>
                }
              }
            </hlm-dropdown-menu>
          </ng-template>
        </li>
      </ul>
    }
    //#if (LocalIdentity)
    <app-profile-settings-dialog
      [visible]="profileDialogVisible()"
      (visibleChange)="profileDialogVisible.set($event)"
    />
    <app-change-password-dialog
      [visible]="changePasswordDialogVisible()"
      (visibleChange)="changePasswordDialogVisible.set($event)"
    />
    //#endif
  `,
})
export class UserMenu {
  readonly authService = inject(AuthService);
  private readonly authorizationService = inject(AuthorizationService);
  private readonly router = inject(Router);
  private readonly layoutService = inject(LayoutService);
  //#if (IncludeLocalization)
  private readonly languageService = inject(LanguageService);
  private readonly transloco = inject(TranslocoService);
  //#endif
  private readonly tenantContext = inject(TenantContextService);
  //#if (LocalIdentity)
  readonly profileDialogVisible = signal(false);
  readonly changePasswordDialogVisible = signal(false);
  //#endif
  readonly userMenuItems = computed<UserMenuItem[]>(() => {
    //#if (IncludeLocalization)
    // 建立对活动语言的依赖，语言切换时重新计算菜单文案。
    this.languageService.activeLang();
    const t = (key: string) => this.transloco.translate(key);
    //#endif
    const items: UserMenuItem[] = [];

    if (this.layoutService.isPlatform()) {
      items.push({
        //#if (IncludeLocalization)
        label: t('menu.workspace'),
        //#else
        label: 'Workspace',
        //#endif
        icon: 'lucideHouse',
        action: () => this.router.navigate(['/workspace']),
      });
    } else if (
      this.layoutService.currentUrl().startsWith('/workspace') &&
      this.authorizationService.canAccessPlatform()
    ) {
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

    if (items.length > 0) {
      items.push({ separator: true });
    }

    items.push(
      //#if (LocalIdentity)
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
      //#endif
      { separator: true },
      // 切换租户 = 清除本地租户上下文并退出登录：已登录会话的租户由 cookie claim 定案，
      // 只有重新登录才能进入另一个租户。
      {
        //#if (IncludeLocalization)
        label: t('menu.switchTenant'),
        //#else
        label: 'Switch tenant',
        //#endif
        icon: 'lucideBuilding2',
        action: () => this.handleSwitchTenant(),
      },
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
    return items;
  });

  /** 当前租户显示名；未选租户即宿主。 */
  readonly tenantLabel = computed(() => {
    //#if (IncludeLocalization)
    this.languageService.activeLang();
    //#endif
    const tenant = this.tenantContext.current();
    if (tenant) {
      return tenant.displayName || tenant.name;
    }
    //#if (IncludeLocalization)
    return this.transloco.translate('menu.hostTenant');
    //#else
    return 'Host';
    //#endif
  });

  handleSwitchTenant(): void {
    this.tenantContext.clear();
    this.authService.logout();
  }
  //#if (LocalIdentity)
  openProfileDialog(): void {
    this.profileDialogVisible.set(true);
  }

  openChangePasswordDialog(): void {
    this.profileDialogVisible.set(false);
    this.changePasswordDialogVisible.set(true);
  }
  //#endif
  handleLogout(): void {
    this.authService.logout();
  }
}
