import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideGauge, lucideIdCard, lucideUsers } from '@ng-icons/lucide';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';

//#if (IncludeLocalization)
import { translationReady } from '../../../core/i18n/translation-ready';
//#endif
import { AuthService } from '../../../core/services/auth-service';
import { Logo } from '../../../shared/components/logo/logo';
import { LayoutService } from '../../services/layout-service';
//#if (IncludeIdentity)
import { UserMenu } from '../user-menu/user-menu';
//#endif

interface MenuItem {
  label: string;
  icon: string;
  route: string;
  superAdminOnly?: boolean;
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
  providers: [provideIcons({ lucideGauge, lucideUsers, lucideIdCard })],
  templateUrl: './default-sidebar.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DefaultSidebar {
  readonly layoutService = inject(LayoutService);
  private readonly authService = inject(AuthService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif

  //#if (IncludeLocalization)
  // 存词条键，展示时按 translationReady 响应式翻译；资源就绪 / 语言切换时 menuGroups computed 重算，标签随之更新。
  private readonly platformMenuGroups: MenuGroup[] = [
    { items: [{ label: 'layout.sidebar.dashboard', icon: 'lucideGauge', route: '/platform' }] },
    //#if (IncludeIdentity)
    {
      label: 'layout.sidebar.groupSystem',
      items: [
        // Identity management entries; the developer-app entry is optional.
        { label: 'layout.sidebar.users', icon: 'lucideUsers', route: '/platform/users' },
        //#if (IncludeOpenIddict)
        {
          label: 'layout.sidebar.openApplications',
          icon: 'lucideIdCard',
          route: '/platform/open-applications',
        },
        //#endif
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
    { items: [{ label: 'Dashboard', icon: 'lucideGauge', route: '/platform' }] },
    //#if (IncludeIdentity)
    {
      label: 'System',
      items: [
        // Identity management entries; the developer-app entry is optional.
        { label: 'User Management', icon: 'lucideUsers', route: '/platform/users' },
        //#if (IncludeOpenIddict)
        { label: 'Developer Apps', icon: 'lucideIdCard', route: '/platform/open-applications' },
        //#endif
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
    const isSuperAdmin = this.authService.currentUser()?.isSuperAdmin === true;
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
          .filter((item) => !item.superAdminOnly || isSuperAdmin)
          .map((item) => ({ ...item, label: this.transloco.translate(item.label) })),
        //#else
        items: group.items.filter((item) => !item.superAdminOnly || isSuperAdmin),
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

  isItemActive(item: MenuItem): boolean {
    const currentUrl = this.layoutService.currentUrl();
    if (item.route === '/platform') {
      return currentUrl === item.route;
    }

    return currentUrl === item.route || currentUrl.startsWith(`${item.route}/`);
  }
}
