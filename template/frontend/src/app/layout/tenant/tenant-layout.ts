//#if (TenantWorkspace)
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { WorkspaceContextService } from '../../core/services/workspace-context-service';

@Component({
  selector: 'app-tenant-layout',
  imports: [RouterOutlet],
  template: `@for (name of [workspace.target()]; track name) {
    <router-outlet />
  }`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TenantLayout {
  readonly workspace = inject(WorkspaceContextService);
}
//#endif
