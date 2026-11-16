import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { NavigationProgress } from '../../core/components/navigation-progress/navigation-progress';
//#if (TenantWorkspace)
import { AuthorizationService } from '../../core/services/authorization-service';
//#endif
import { LayoutService } from '../../core/services/layout-service';
//#if (TenantWorkspace)
import { WorkspaceContextService } from '../../core/services/workspace-context-service';
//#endif
import { CommandPalette } from '../components/command-palette/command-palette';
import { DefaultHeader } from '../components/default-header/default-header';
import { PageBreadcrumb } from '../components/page-breadcrumb/page-breadcrumb';
//#if (IncludeRealTime)
import { RealtimeStatus } from '../components/realtime-status/realtime-status';
//#endif
//#if (TenantWorkspace)
import { EmptyWorkspace } from '../tenant/empty-workspace';
//#endif

/**
 * 工作空间布局：顶栏导航，没有侧栏；菜单与管理平台侧栏读同一份（见 NavigationService）。需要分组标题
 * 或按权限整组裁剪时换回 DefaultLayout，判据见 docs/standards/frontend-navigation.md §2。
 */
@Component({
  selector: 'app-workspace-layout',
  // prettier-ignore
  imports: [
    RouterOutlet,
    //#if (TenantWorkspace)
    EmptyWorkspace,
    //#endif
    CommandPalette,
    DefaultHeader,
    NavigationProgress,
    PageBreadcrumb,
    //#if (IncludeRealTime)
    RealtimeStatus,
    //#endif
  ],
  templateUrl: './workspace-layout.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkspaceLayout {
  //#if (TenantWorkspace)
  readonly workspace = inject(WorkspaceContextService);
  readonly authorization = inject(AuthorizationService);
  //#endif
  protected readonly layoutService = inject(LayoutService);
}
