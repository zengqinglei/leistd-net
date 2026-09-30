import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
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
//#if (!IncludeLocalization)
import { englishText } from '../../../shared/utils/english-text';
//#endif
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
    TranslocoDirective,
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
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif
  private readonly navigation = inject(NavigationService);
  private readonly sidebarService = inject(HlmSidebarService);

  /** 区域切换菜单的弹出方向，同官方 nav-user：桌面向右，手机（侧栏是抽屉）向下。 */
  readonly areaMenuSide = computed(() => (this.sidebarService.isMobile() ? 'bottom' : 'right'));

  readonly areaOptions = this.navigation.areaOptions;
  readonly currentAreaLabel = this.navigation.currentAreaLabel;
  readonly menuGroups = this.navigation.menuGroups;

  goToArea(route: string): void {
    this.navigation.goToArea(route);
  }

  isItemActive(item: MenuItem): boolean {
    return this.navigation.isItemActive(item);
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'layout.sidebar.navigation': 'Navigation',
  'layout.sidebar.navigationDescription': 'Browse the sections of this area.',
  'layout.sidebar.switchArea': 'Switch area',
  'layout.sidebar.appTitle': 'Template Project',
};
//#endif
