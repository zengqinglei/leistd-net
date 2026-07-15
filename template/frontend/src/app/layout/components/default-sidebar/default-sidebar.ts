import { NgClass } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterModule } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { ButtonModule } from 'primeng/button';
import { TooltipModule } from 'primeng/tooltip';
import { filter, map, startWith } from 'rxjs/operators';

import { AuthService } from '../../../core/services/auth-service';
import { LogoComponent } from '../../../shared/components/logo/logo';
import { LayoutService } from '../../services/layout-service';

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
  //#if (IncludeLocalization)
  imports: [NgClass, RouterModule, TranslocoModule, ButtonModule, TooltipModule, LogoComponent],
  //#else
  imports: [NgClass, RouterModule, ButtonModule, TooltipModule, LogoComponent],
  //#endif
  templateUrl: './default-sidebar.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DefaultSidebar {
  readonly layoutService = inject(LayoutService);
  private readonly router = inject(Router);
  private readonly authService = inject(AuthService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif

  isMobileMenuOpen = input<boolean>(false);
  readonly mobileMenuClosed = output<void>();

  //#if (IncludeLocalization)
  // 存词条键，展示时按 activeLang 响应式翻译；语言切换时 menuGroups computed 重算，标签随之更新。
  private readonly platformMenuGroups: MenuGroup[] = [
    { items: [{ label: 'layout.sidebar.dashboard', icon: 'pi-gauge', route: '/platform' }] },
    //#if (IncludeIdentity)
    {
      label: 'layout.sidebar.groupSystem',
      items: [
        { label: 'layout.sidebar.users', icon: 'pi-users', route: '/platform/users' },
        //#if (IncludeOpenIddict)
        { label: 'layout.sidebar.openApplications', icon: 'pi-id-card', route: '/platform/open-applications' }
        //#endif
      ]
    }
    //#endif
  ];

  private readonly workspaceMenuGroups: MenuGroup[] = [
    { items: [{ label: 'layout.sidebar.workbench', icon: 'pi-gauge', route: '/workspace/dashboard' }] }
  ];

  // 追踪活动语言：切换时该 signal 变化 → menuGroups 重算 → 标签重新翻译。
  private readonly activeLang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });
  //#else
  private readonly platformMenuGroups: MenuGroup[] = [
    { items: [{ label: 'Dashboard', icon: 'pi-gauge', route: '/platform' }] },
    //#if (IncludeIdentity)
    {
      label: 'System',
      items: [
        { label: 'User Management', icon: 'pi-users', route: '/platform/users' },
        //#if (IncludeOpenIddict)
        { label: 'Developer Apps', icon: 'pi-id-card', route: '/platform/open-applications' }
        //#endif
      ]
    }
    //#endif
  ];

  private readonly workspaceMenuGroups: MenuGroup[] = [
    { items: [{ label: 'Workbench', icon: 'pi-gauge', route: '/workspace/dashboard' }] }
  ];
  //#endif

  private readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map(event => event.urlAfterRedirects ?? event.url),
      startWith(this.router.url)
    ),
    { initialValue: this.router.url }
  );

  readonly menuGroups = computed(() => {
    const url = this.currentUrl();
    const groups = url.startsWith('/platform') ? this.platformMenuGroups : this.workspaceMenuGroups;
    const isSuperAdmin = this.authService.currentUser()?.isSuperAdmin === true;
    //#if (IncludeLocalization)
    // 读取 activeLang 建立依赖：语言切换时本 computed 重算，标签重新翻译。
    this.activeLang();
    //#endif

    return groups
      .map(group => ({
        ...group,
        //#if (IncludeLocalization)
        label: group.label ? this.transloco.translate(group.label) : group.label,
        items: group.items
          .filter(item => !item.superAdminOnly || isSuperAdmin)
          .map(item => ({ ...item, label: this.transloco.translate(item.label) }))
        //#else
        items: group.items.filter(item => !item.superAdminOnly || isSuperAdmin)
        //#endif
      }))
      .filter(group => group.items.length > 0);
  });

  isItemActive(item: MenuItem) {
    const currentUrl = this.currentUrl();
    if (item.route === '/platform') {
      return currentUrl === item.route;
    }

    return currentUrl === item.route || currentUrl.startsWith(`${item.route}/`);
  }
}
