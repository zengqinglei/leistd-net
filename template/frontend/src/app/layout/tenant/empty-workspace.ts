//#if (TenantWorkspace)
import {
  ChangeDetectionStrategy,
  Component,
  DOCUMENT,
  Injector,
  afterNextRender,
  inject,
} from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideLock } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmSidebarService } from '@spartan-ng/helm/sidebar';

import { WorkspaceContextService } from '../../core/services/workspace-context-service';
//#if (!IncludeLocalization)
import { englishText } from '../../shared/utils/english-text';
//#endif

@Component({
  selector: 'app-empty-workspace',
  // prettier-ignore
  imports: [
    NgIcon,
    HlmButton,
    ...HlmEmptyImports,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [provideIcons({ lucideLock })],
  templateUrl: './empty-workspace.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmptyWorkspace {
  readonly workspace = inject(WorkspaceContextService);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly sidebar = inject(HlmSidebarService);
  //#if (!IncludeLocalization)
  readonly t = englishText({
    'tenantWorkspace.emptyTitle': 'No features are available in {{name}} yet',
    'tenantWorkspace.emptyDescription':
      'Ask the workspace administrator to assign roles in this system.',
    'tenantWorkspace.switch': 'Switch workspace',
    'tenantWorkspace.backToConsole': 'Back to platform management',
  });
  //#endif
  openSwitcher(): void {
    const trigger = () =>
      [
        ...this.document.querySelectorAll<HTMLElement>(
          '[data-testid="tenant-selector"] input[role="combobox"]',
        ),
      ].find((button) => button.getClientRects().length > 0);
    const visible = trigger();
    if (visible) {
      visible.click();
      return;
    }
    this.sidebar.setOpenMobile(true);
    afterNextRender(() => trigger()?.click(), { injector: this.injector });
  }
}
//#endif
