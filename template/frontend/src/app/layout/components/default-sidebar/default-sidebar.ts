//#if (TenantWorkspace)
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
//#else
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
//#endif
import { RouterLink } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
// prettier-ignore
import {
  lucideBuilding2,
  lucideCog,
  lucideDatabase,
  lucideGauge,
  lucideHouse,
  lucideIdCard,
  lucideLayers,
  lucideSettings,
  lucideShieldCheck,
  lucideUsers,
  //#if (IncludeDistributedEvents)
  lucideRadioTower,
  //#endif
  //#if (IncludeWebhooks)
  lucideWebhook,
  //#endif
} from '@ng-icons/lucide';
//#if (TenantWorkspace)
import { HlmComboboxImports } from '@spartan-ng/helm/combobox';
//#endif
import { HlmSidebarImports, HlmSidebarService } from '@spartan-ng/helm/sidebar';

import { LayoutService } from '../../../core/services/layout-service';
//#if (TenantWorkspace)
import { WorkspaceContextService } from '../../../core/services/workspace-context-service';
//#endif
//#if (!IncludeLocalization)
import { englishText } from '../../../shared/utils/english-text';
//#endif
import { MenuItem, NavigationService } from '../../services/navigation-service';
import { SystemSwitcher } from '../system-switcher/system-switcher';

@Component({
  selector: 'app-default-sidebar',
  standalone: true,
  // prettier-ignore
  imports: [
    RouterLink,
    //#if (TenantWorkspace)
    ...HlmComboboxImports,
    //#endif
    NgIcon,
    ...HlmSidebarImports,
    SystemSwitcher,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  // prettier-ignore
  providers: [provideIcons({
    lucideBuilding2, lucideCog, lucideHouse,
    lucideGauge, lucideUsers, lucideIdCard, lucideLayers, lucideShieldCheck, lucideSettings,
    lucideDatabase,
    //#if (IncludeDistributedEvents)
    lucideRadioTower,
    //#endif
    //#if (IncludeWebhooks)
    lucideWebhook,
    //#endif
  })],
  // 侧栏用 inset 变体：主内容区（hlmSidebarInset）按兄弟元素的 peer 状态浮成圆角面板，而 hlm-sidebar
  // 包在本组件里、不是它的兄弟，所以把 peer 与桌面端的状态属性转到组件宿主上。手机端侧栏是抽屉，不转。
  host: {
    class: 'peer',
    '[attr.data-variant]': "sidebarService.isMobile() ? null : 'inset'",
    '[attr.data-state]': 'sidebarService.isMobile() ? null : sidebarService.state()',
  },
  templateUrl: './default-sidebar.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DefaultSidebar {
  //#if (TenantWorkspace)
  readonly workspace = inject(WorkspaceContextService);
  readonly showTenantSelector = computed(
    () =>
      !!this.workspace.current() &&
      !this.workspace.auth.currentUser()?.tenantId &&
      this.workspace.workspaces().length > 1,
  );
  readonly workspaceLabel = (value: string) =>
    this.workspace.workspaces().find((item) => item.name === value)?.displayName ?? value;
  //#endif
  readonly layoutService = inject(LayoutService);
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif
  private readonly navigation = inject(NavigationService);
  protected readonly sidebarService = inject(HlmSidebarService);

  readonly menuGroups = this.navigation.menuGroups;

  isItemActive(item: MenuItem): boolean {
    return this.navigation.isItemActive(item);
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  //#if (TenantWorkspace)
  'tenantWorkspace.title': 'Workspaces',
  'tenantWorkspace.console': 'Platform management',
  'tenantWorkspace.return': 'Return to {{tenant}}',
  'tenantWorkspace.search': 'Search tenants',
  'tenantWorkspace.empty': 'No matching tenants',
  //#endif
  'layout.sidebar.navigation': 'Navigation',
  'layout.sidebar.navigationDescription': 'Browse the sections of this area.',
  'layout.sidebar.switchArea': 'Switch area',
  'layout.sidebar.appTitle': 'Template Project',
  'layout.commandPalette.search': 'Search',
};
//#endif
