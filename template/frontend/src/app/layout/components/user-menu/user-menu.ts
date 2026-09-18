import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
// prettier-ignore
import {
  lucideChevronsUpDown,
  lucideCog,
  lucideHouse,
  lucideLogOut,
  lucideUserCog,
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
 * 头像 + 姓名/邮箱两行 + 下拉：区域切换、个人设置、退出。
 * 具体项见 `userMenuItems`，构成由 user-menu.spec.ts 钉住。折叠为图标时自动收成头像方块。
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
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
  ],
  // prettier-ignore
  providers: [
    provideIcons({
      lucideChevronsUpDown,
      lucideHouse,
      lucideCog,
      lucideUserCog,
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
      // 个人资料、账户安全、偏好都是个人设置的面板，这里只留一个入口直达；
      // 管理人员从管理平台点它会回到工作空间——个人设置只有一处，管理平台不另放一份。
      // 不带 LocalIdentity 守卫：没有本地身份时个人设置里仍有偏好面板。
      {
        //#if (IncludeLocalization)
        label: t('menu.personalSettings'),
        //#else
        label: 'Personal settings',
        //#endif
        icon: 'lucideUserCog',
        action: () => this.router.navigate(['/workspace/settings']),
      },
      { separator: true },
      // 这里刻意没有「切换租户」：已登录会话的租户由 cookie claim 定案，换租户只能重新登录，
      // 所以那一项做的事其实就是退出登录，再单列一个入口只是让人以为存在会话内切换。
      // 换租户走登录页：退出 → 登录页按域名定案，或（域名不表态时）在那里清掉 / 换一个租户。
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

  handleLogout(): void {
    this.authService.logout();
  }
}
