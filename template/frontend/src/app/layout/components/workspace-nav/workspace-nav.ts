import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoModule } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideGauge, lucideLayers, lucideMenu, lucideSettings } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmNavigationMenuImports } from '@spartan-ng/helm/navigation-menu';
import { HlmSheetImports } from '@spartan-ng/helm/sheet';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

import { Logo } from '../../../shared/components/logo/logo';
import { LayoutService } from '../../services/layout-service';
import { MenuItem, NavigationService } from '../../services/navigation-service';

/**
 * 工作空间顶栏的导航。
 *
 * 顶栏放两份：`start` 在左侧（品牌、窄屏抽屉、这个区能做的事，文字链接），
 * `end` 在右侧（关于我自己的，与主题、通知、语言同款的图标按钮，名称在提示与可访问名里）。
 * 分到哪一侧看菜单分组的 `placement`；顶栏不显示分组标题，同侧各组首尾相接。
 * 窄屏只剩左侧的抽屉按钮，抽屉里按分组列出全部入口。
 * 新增菜单项用了新图标时，要在这里的 `provideIcons` 登记（抽屉里显示图标）。
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
    TranslocoModule,
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

  readonly appTitle = this.navigation.appTitle;
  readonly groups = this.navigation.menuGroups;

  private readonly ownGroups = computed(() =>
    this.groups().filter((group) => (group.placement ?? 'start') === this.placement()),
  );

  readonly items = computed(() => this.ownGroups().flatMap((group) => group.items));

  /** 左右两个导航区不能同名，否则读屏器听到两个"导航"；右侧用它自己的分组名。 */
  readonly ariaLabel = computed(() =>
    this.placement() === 'start'
      ? this.navigation.navLabel()
      : this.ownGroups()
          .map((group) => group.label)
          .join(' / '),
  );

  isItemActive(item: MenuItem): boolean {
    return this.navigation.isItemActive(item);
  }
}
