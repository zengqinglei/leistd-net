import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoModule } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
// prettier-ignore
import {
  lucideBuilding2,
  lucideCheck,
  lucideChevronsUpDown,
  lucideCog,
  lucideDatabase,
  lucideGauge,
  lucideHouse,
  lucideIdCard,
  lucideLayers,
  lucideSettings,
  lucideShieldCheck,
  lucideUsers,
} from '@ng-icons/lucide';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmSidebarImports, HlmSidebarService } from '@spartan-ng/helm/sidebar';

import { Logo } from '../../../shared/components/logo/logo';
import { LayoutService } from '../../services/layout-service';
import { MenuItem, NavigationService } from '../../services/navigation-service';
import { UserMenu } from '../user-menu/user-menu';

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
    lucideDatabase,
  })],
  templateUrl: './default-sidebar.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DefaultSidebar {
  readonly layoutService = inject(LayoutService);
  private readonly navigation = inject(NavigationService);
  private readonly sidebarService = inject(HlmSidebarService);

  /** 区域切换菜单的弹出方向，同官方 nav-user：桌面向右，手机（侧栏是抽屉）向下。 */
  readonly areaMenuSide = computed(() => (this.sidebarService.isMobile() ? 'bottom' : 'right'));

  readonly areaOptions = this.navigation.areaOptions;
  readonly currentAreaLabel = this.navigation.currentAreaLabel;
  readonly appTitle = this.navigation.appTitle;
  readonly switchAreaLabel = this.navigation.switchAreaLabel;
  readonly menuGroups = this.navigation.menuGroups;
  readonly navLabel = this.navigation.navLabel;
  readonly navDescription = this.navigation.navDescription;

  goToArea(route: string): void {
    this.navigation.goToArea(route);
  }

  isItemActive(item: MenuItem): boolean {
    return this.navigation.isItemActive(item);
  }
}
