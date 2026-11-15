// prettier-ignore
import {
  ChangeDetectionStrategy, Component,
  //#if (TenantWorkspace)
  inject,
  //#endif
} from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';

import { NavigationProgress } from '../../core/components/navigation-progress/navigation-progress';
//#if (TenantWorkspace)
import { AuthorizationService } from '../../core/services/authorization-service';
//#endif
//#if (TenantWorkspace)
import { WorkspaceContextService } from '../../core/services/workspace-context-service';
//#endif
import { CommandPalette } from '../components/command-palette/command-palette';
import { DefaultHeader } from '../components/default-header/default-header';
import { DefaultSidebar } from '../components/default-sidebar/default-sidebar';
//#if (IncludeRealTime)
import { RealtimeStatus } from '../components/realtime-status/realtime-status';
//#endif
//#if (TenantWorkspace)
import { EmptyWorkspace } from '../tenant/empty-workspace';
//#endif

@Component({
  selector: 'app-default-layout',
  standalone: true,
  // prettier-ignore
  imports: [
    RouterOutlet,
    //#if (TenantWorkspace)
    EmptyWorkspace,
    //#endif
    CommandPalette,
    DefaultHeader,
    DefaultSidebar,
    NavigationProgress,
    //#if (IncludeRealTime)
    RealtimeStatus,
    //#endif
    ...HlmSidebarImports,
  ],
  templateUrl: './default-layout.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DefaultLayout {
  //#if (TenantWorkspace)
  readonly workspace = inject(WorkspaceContextService);
  readonly authorization = inject(AuthorizationService);
  //#endif
}
