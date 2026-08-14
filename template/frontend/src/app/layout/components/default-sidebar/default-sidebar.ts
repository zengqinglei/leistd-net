import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
// prettier-ignore
import {
  //#if (TenancyEnabled)
  lucideBuilding2,
  //#endif
  lucideGauge,
  lucideIdCard,
  lucideShieldCheck,
  lucideUsers,
} from '@ng-icons/lucide';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';

//#if (IncludeLocalization)
import { translationReady } from '../../../core/i18n/translation-ready';
//#endif
//#if (IncludeRoles)
import { AuthorizationService } from '../../../core/services/authorization-service';
//#endif
import { Logo } from '../../../shared/components/logo/logo';
//#if (IncludeRoles)
import { PERMISSIONS } from '../../../shared/models/permission';
//#endif
import { LayoutService } from '../../services/layout-service';
//#if (IncludeIdentity)
import { UserMenu } from '../user-menu/user-menu';
//#endif

interface MenuItem {
  label: string;
  icon: string;
  route: string;
  /** 所需权限；拥有任意一个即可见。省略表示只要能进入本区就可见。 */
  permissions?: string[];
}

interface MenuGroup {
  label?: string;
  items: MenuItem[];
}

@Component({
  selector: 'app-default-sidebar',
  standalone: true,
  // prettier-ignore
  imports: [
    RouterLink,
    NgIcon,
    Logo,
    ...HlmSidebarImports,
    //#if (IncludeIdentity)
    UserMenu,
    //#endif
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
  ],
  // prettier-ignore
  providers: [provideIcons({
    //#if (TenancyEnabled)
    lucideBuilding2,
    //#endif
    lucideGauge, lucideUsers, lucideIdCard, lucideShieldCheck,
  })],
  templateUrl: './default-sidebar.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DefaultSidebar {
  readonly layoutService = inject(LayoutService);
  //#if (IncludeRoles)
  private readonly authorizationService = inject(AuthorizationService);
  //#endif
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif
  //#if (IncludeLocalization)
  // 存词条键，展示时按 translationReady 响应式翻译；资源就绪 / 语言切换时 menuGroups computed 重算，标签随之更新。
  private readonly platformMenuGroups: MenuGroup[] = [
    // 首区不设标题：Dashboard 是全局入口而非某一类的成员，给单项加组标题只增加解析成本。
    { items: [{ label: 'layout.sidebar.dashboard', icon: 'lucideGauge', route: '/platform' }] },
    //#if (IncludeIdentity)
    // 分组按关注点命名，不用「系统」这类兜底词——兜底词会把不相干的入口越塞越多。
    {
      label: 'layout.sidebar.groupAccess',
      items: [
        {
          label: 'layout.sidebar.users',
          icon: 'lucideUsers',
          route: '/platform/users',
          //#if (IncludeRoles)
          permissions: [PERMISSIONS.users.default],
          //#endif
        },
        //#if (IncludeRoles)
        {
          label: 'layout.sidebar.roles',
          icon: 'lucideShieldCheck',
          route: '/platform/roles',
          permissions: [PERMISSIONS.roles.default],
        },
        //#endif
        //#if (TenancyEnabled)
        // 宿主侧专属：租户用户的 current 权限里不会出现 App.Tenants，按权限自动裁剪。
        {
          label: 'layout.sidebar.tenants',
          icon: 'lucideBuilding2',
          route: '/platform/tenants',
          permissions: [PERMISSIONS.tenants.default],
        },
        //#endif
      ],
    },
    //#endif
    //#if (IncludeOpenIddict)
    {
      label: 'layout.sidebar.groupDeveloper',
      items: [
        {
          label: 'layout.sidebar.openApplications',
          icon: 'lucideIdCard',
          route: '/platform/open-applications',
          //#if (IncludeRoles)
          permissions: [PERMISSIONS.openApplications.default],
          //#endif
        },
      ],
    },
    //#endif
  ];

  private readonly workspaceMenuGroups: MenuGroup[] = [
    {
      items: [
        { label: 'layout.sidebar.workbench', icon: 'lucideGauge', route: '/workspace/dashboard' },
      ],
    },
  ];

  // 追踪「翻译就绪」：资源加载完成与语言切换时重算，含首帧避免裸键。
  private readonly translationReady = translationReady(this.transloco);
  //#else
  private readonly platformMenuGroups: MenuGroup[] = [
    // 首区不设标题：Dashboard 是全局入口而非某一类的成员，给单项加组标题只增加解析成本。
    { items: [{ label: 'Dashboard', icon: 'lucideGauge', route: '/platform' }] },
    //#if (IncludeIdentity)
    // 分组按关注点命名，不用「系统」这类兜底词——兜底词会把不相干的入口越塞越多。
    {
      label: 'Access control',
      items: [
        {
          label: 'User Management',
          icon: 'lucideUsers',
          route: '/platform/users',
          //#if (IncludeRoles)
          permissions: [PERMISSIONS.users.default],
          //#endif
        },
        //#if (IncludeRoles)
        {
          label: 'Role Management',
          icon: 'lucideShieldCheck',
          route: '/platform/roles',
          permissions: [PERMISSIONS.roles.default],
        },
        //#endif
        //#if (TenancyEnabled)
        // 宿主侧专属：租户用户的 current 权限里不会出现 App.Tenants，按权限自动裁剪。
        {
          label: 'Tenant Management',
          icon: 'lucideBuilding2',
          route: '/platform/tenants',
          permissions: [PERMISSIONS.tenants.default],
        },
        //#endif
      ],
    },
    //#endif
    //#if (IncludeOpenIddict)
    {
      label: 'Developer',
      items: [
        {
          label: 'Developer Apps',
          icon: 'lucideIdCard',
          route: '/platform/open-applications',
          //#if (IncludeRoles)
          permissions: [PERMISSIONS.openApplications.default],
          //#endif
        },
      ],
    },
    //#endif
  ];

  private readonly workspaceMenuGroups: MenuGroup[] = [
    { items: [{ label: 'Workbench', icon: 'lucideGauge', route: '/workspace/dashboard' }] },
  ];
  //#endif

  readonly menuGroups = computed(() => {
    const groups = this.layoutService.isPlatform()
      ? this.platformMenuGroups
      : this.workspaceMenuGroups;
    //#if (IncludeLocalization)
    // 读取 translationReady 建立依赖：资源就绪 / 语言切换时本 computed 重算，标签重新翻译。
    this.translationReady();
    //#endif

    return groups
      .map((group) => ({
        ...group,
        //#if (IncludeLocalization)
        label: group.label ? this.transloco.translate(group.label) : group.label,
        items: group.items
          .filter((item) => this.isItemVisible(item))
          .map((item) => ({ ...item, label: this.transloco.translate(item.label) })),
        //#else
        items: group.items.filter((item) => this.isItemVisible(item)),
        //#endif
      }))
      .filter((group) => group.items.length > 0);
  });
  //#if (IncludeLocalization)
  // 移动端侧栏 Sheet 的可访问名（视觉隐藏），随语言切换重算。
  readonly navLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('layout.sidebar.navigation');
  });
  //#else
  readonly navLabel = computed(() => 'Navigation');
  //#endif

  /**
   * 菜单可见性只按权限判断。
   *
   * 这里刻意不再使用"是否超级管理员"作为判据：超管的旁路已经体现在下发的权限集合里，
   * 前端再判断一次会让菜单与后端的权限语义分叉。
   */
  //#if (IncludeRoles)
  private isItemVisible(item: MenuItem): boolean {
    return !item.permissions?.length || this.authorizationService.hasAny(...item.permissions);
  }
  //#else
  private isItemVisible(_item: MenuItem): boolean {
    return true;
  }
  //#endif

  isItemActive(item: MenuItem): boolean {
    const currentUrl = this.layoutService.currentUrl();
    if (item.route === '/platform') {
      return currentUrl === item.route;
    }

    return currentUrl === item.route || currentUrl.startsWith(`${item.route}/`);
  }
}
