import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
// prettier-ignore
import {
  lucideBuilding2,
  lucideCheck,
  lucideChevronsUpDown,
  lucideCog,
  lucideGauge,
  lucideHouse,
  lucideIdCard,
  lucideLayers,
  lucideSettings,
  lucideShieldCheck,
  lucideUsers,
} from '@ng-icons/lucide';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';

//#if (IncludeLocalization)
import { translationReady } from '../../../core/i18n/translation-ready';
//#endif
import { AuthorizationService } from '../../../core/services/authorization-service';
import { Logo } from '../../../shared/components/logo/logo';
import { PERMISSIONS } from '../../../shared/models/permission';
import { LayoutService } from '../../services/layout-service';
import { UserMenu } from '../user-menu/user-menu';

interface MenuItem {
  label: string;
  icon: string;
  route: string;
  /** 所需权限；拥有任意一个即可见。省略表示只要能进入本区就可见。 */
  permissions?: string[];
}

interface MenuGroup {
  /** 分组标题，必填：没有标题的组就是变相的兜底组（见 docs/standards/coding-frontend.md §8）。 */
  label: string;
  items: MenuItem[];
}

/*
 * 下面两套菜单是这个系统的**信息架构**，不是控件清单。
 *
 * 分组判据（工作 / 业务 / 系统 / 开发者 / 运维 各放什么，以及"不设兜底组"等规则）
 * 只写在 docs/standards/coding-frontend.md §8 一处——新增入口先去那张表里找落位，
 * 落不进就在那里新开一类。判据在这里再抄一份，改的时候必然只改一边。
 * 分组骨架由 default-sidebar.spec.ts 钉住。
 */

@Component({
  selector: 'app-default-sidebar',
  standalone: true,
  // prettier-ignore
  imports: [
    RouterLink,
    NgIcon,
    Logo,
    ...HlmDropdownMenuImports,
    ...HlmSidebarImports,
    UserMenu,
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
  ],
  // prettier-ignore
  providers: [provideIcons({
    lucideBuilding2, lucideCheck, lucideChevronsUpDown, lucideCog, lucideHouse,
    lucideGauge, lucideUsers, lucideIdCard, lucideLayers, lucideShieldCheck, lucideSettings,
  })],
  templateUrl: './default-sidebar.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DefaultSidebar {
  readonly layoutService = inject(LayoutService);
  private readonly authorizationService = inject(AuthorizationService);
  private readonly router = inject(Router);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif
  //#if (IncludeLocalization)
  // 存词条键，展示时按 translationReady 响应式翻译；资源就绪 / 语言切换时 menuGroups computed 重算，标签随之更新。
  private readonly platformMenuGroups: MenuGroup[] = [
    {
      label: 'layout.sidebar.groupWork',
      items: [{ label: 'layout.sidebar.dashboard', icon: 'lucideGauge', route: '/platform' }],
    },
    // 管理侧的业务菜单加在这里（工作之后、系统之前）：管理员先看业务，再管系统与运维。
    {
      label: 'layout.sidebar.groupSystem',
      items: [
        {
          label: 'layout.sidebar.users',
          icon: 'lucideUsers',
          route: '/platform/users',
          permissions: [PERMISSIONS.users.default],
        },
        {
          label: 'layout.sidebar.roles',
          icon: 'lucideShieldCheck',
          route: '/platform/roles',
          permissions: [PERMISSIONS.roles.default],
        },
        //#if (LocalIdentity)
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
    //#if (OpenIddictServer)
    {
      label: 'layout.sidebar.groupDeveloper',
      items: [
        {
          label: 'layout.sidebar.openApplications',
          icon: 'lucideIdCard',
          route: '/platform/open-applications',
          permissions: [PERMISSIONS.openApplications.default],
        },
      ],
    },
    //#endif
    {
      label: 'layout.sidebar.groupOperations',
      items: [
        // 这里是**系统配置**（租户级默认值），按 App.Settings 裁剪。个人偏好不在主导航，
        // 在头像菜单里——两者作用域不同，混在一个入口容易把私人偏好当成全租户默认值改。
        {
          label: 'layout.sidebar.systemSettings',
          icon: 'lucideSettings',
          route: '/platform/settings',
          permissions: [PERMISSIONS.settings.default],
        },
      ],
    },
  ];

  private readonly workspaceMenuGroups: MenuGroup[] = [
    {
      label: 'layout.sidebar.groupWork',
      items: [
        { label: 'layout.sidebar.workbench', icon: 'lucideGauge', route: '/workspace/dashboard' },
      ],
    },
    {
      // 模板在这里只放一个示例模块：下游项目把它换成自己的业务入口，
      // 分组本身与判据留着，新入口就不必再重新想一遍该摆哪。
      label: 'layout.sidebar.groupBusiness',
      items: [
        {
          label: 'layout.sidebar.exampleModule',
          icon: 'lucideLayers',
          route: '/workspace/placeholder',
        },
      ],
    },
  ];

  // 追踪「翻译就绪」：资源加载完成与语言切换时重算，含首帧避免裸键。
  private readonly translationReady = translationReady(this.transloco);
  //#else
  private readonly platformMenuGroups: MenuGroup[] = [
    {
      label: 'Work',
      items: [{ label: 'Dashboard', icon: 'lucideGauge', route: '/platform' }],
    },
    // 管理侧的业务菜单加在这里（工作之后、系统之前）：管理员先看业务，再管系统与运维。
    {
      label: 'System',
      items: [
        {
          label: 'User Management',
          icon: 'lucideUsers',
          route: '/platform/users',
          permissions: [PERMISSIONS.users.default],
        },
        {
          label: 'Role Management',
          icon: 'lucideShieldCheck',
          route: '/platform/roles',
          permissions: [PERMISSIONS.roles.default],
        },
        //#if (LocalIdentity)
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
    //#if (OpenIddictServer)
    {
      label: 'Developer',
      items: [
        {
          label: 'Developer Apps',
          icon: 'lucideIdCard',
          route: '/platform/open-applications',
          permissions: [PERMISSIONS.openApplications.default],
        },
      ],
    },
    //#endif
    {
      label: 'Operations',
      items: [
        // 这里是**系统配置**（租户级默认值），按 App.Settings 裁剪。个人偏好不在主导航，
        // 在头像菜单里——两者作用域不同，混在一个入口容易把私人偏好当成全租户默认值改。
        {
          label: 'System settings',
          icon: 'lucideSettings',
          route: '/platform/settings',
          permissions: [PERMISSIONS.settings.default],
        },
      ],
    },
  ];

  private readonly workspaceMenuGroups: MenuGroup[] = [
    {
      label: 'Work',
      items: [{ label: 'Workbench', icon: 'lucideGauge', route: '/workspace/dashboard' }],
    },
    {
      // 模板在这里只放一个示例模块：下游项目把它换成自己的业务入口，
      // 分组本身与判据留着，新入口就不必再重新想一遍该摆哪。
      label: 'Business',
      items: [{ label: 'Example module', icon: 'lucideLayers', route: '/workspace/placeholder' }],
    },
  ];
  //#endif

  /**
   * 可去的区域。
   *
   * 只有一个可去区域时（无平台权限的普通用户），品牌块退回普通链接——
   * 给一个只有当前项的下拉，点开只会让人以为坏了。
   */
  readonly areaOptions = computed(() => {
    //#if (IncludeLocalization)
    this.translationReady();
    //#endif
    const isPlatform = this.layoutService.isPlatform();
    const areas = [
      {
        route: '/workspace/dashboard',
        icon: 'lucideHouse',
        //#if (IncludeLocalization)
        label: this.transloco.translate('menu.workspace'),
        //#else
        label: 'Workspace',
        //#endif
        current: !isPlatform,
      },
    ];

    // 进平台要权限；回工作空间无条件——与头像菜单同一口径。
    if (this.authorizationService.canAccessPlatform()) {
      areas.push({
        route: '/platform',
        icon: 'lucideCog',
        //#if (IncludeLocalization)
        label: this.transloco.translate('menu.platform'),
        //#else
        label: 'Admin platform',
        //#endif
        current: isPlatform,
      });
    }

    return areas;
  });

  readonly currentAreaLabel = computed(
    () => this.areaOptions().find((area) => area.current)?.label ?? '',
  );

  //#if (IncludeLocalization)
  readonly appTitle = computed(() => {
    this.translationReady();
    return this.transloco.translate('layout.sidebar.appTitle');
  });

  readonly switchAreaLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('layout.sidebar.switchArea');
  });
  //#else
  readonly appTitle = () => 'Template Project';
  readonly switchAreaLabel = () => 'Switch area';
  //#endif

  goToArea(route: string): void {
    void this.router.navigate([route]);
  }

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
        label: this.transloco.translate(group.label),
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
   * 超级管理员旁路已体现在下发的权限集合中，前端不另建身份判据。
   */
  private isItemVisible(item: MenuItem): boolean {
    return !item.permissions?.length || this.authorizationService.hasAny(...item.permissions);
  }

  isItemActive(item: MenuItem): boolean {
    const currentUrl = this.layoutService.currentUrl();
    if (item.route === '/platform') {
      return currentUrl === item.route;
    }

    return currentUrl === item.route || currentUrl.startsWith(`${item.route}/`);
  }
}
