import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideGauge, lucideLayers, lucideMenu, lucideSettings } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmNavigationMenuImports } from '@spartan-ng/helm/navigation-menu';
import { HlmSheetImports } from '@spartan-ng/helm/sheet';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

import { LayoutService } from '../../../core/services/layout-service';
import { Logo } from '../../../shared/components/logo/logo';
//#if (!IncludeLocalization)
import { englishText } from '../../../shared/utils/english-text';
//#endif
import { MenuItem, NavigationService } from '../../services/navigation-service';

/**
 * 工作空间顶栏的导航：按菜单分组的 `placement` 放在左侧（文字链接）或右侧（图标按钮），不显示分组标题；
 * 窄屏只剩左侧的抽屉按钮。新增菜单项用了新图标时在这里的 `provideIcons` 登记。
 */
@Component({
  selector: 'app-workspace-nav',
  // prettier-ignore
  imports: [
    RouterLink,
    NgIcon,
    HlmButton,
    ...HlmNavigationMenuImports,
    ...HlmSheetImports,
    ...HlmTooltipImports,
    Logo,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [provideIcons({ lucideGauge, lucideLayers, lucideMenu, lucideSettings })],
  templateUrl: './workspace-nav.html',
  host: { class: 'flex min-w-0 items-center gap-3' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkspaceNav {
  readonly placement = input<'start' | 'end'>('start');

  readonly layoutService = inject(LayoutService);
  private readonly navigation = inject(NavigationService);
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif

  readonly groups = this.navigation.menuGroups;

  private readonly ownGroups = computed(() =>
    this.groups().filter((group) => (group.placement ?? 'start') === this.placement()),
  );

  readonly items = computed(() => this.ownGroups().flatMap((group) => group.items));

  /** 右侧导航区的可访问名。左右两个导航区不能同名，否则读屏器听到两个"导航"；右侧用它自己的分组名。 */
  readonly endNavLabel = computed(() =>
    this.ownGroups()
      .map((group) => group.label)
      .join(' / '),
  );

  isItemActive(item: MenuItem): boolean {
    return this.navigation.isItemActive(item);
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'layout.topbar.openNavigation': 'Open navigation',
  'layout.sidebar.appTitle': 'Template Project',
  'layout.topbar.navigationDescription': 'Choose a page to open',
  'layout.sidebar.navigation': 'Navigation',
};
//#endif
