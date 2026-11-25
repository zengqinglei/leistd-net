//#if (TenantWorkspace)
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideLock } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';

//#if (!IncludeLocalization)
import { englishText } from '../../../shared/utils/english-text';
//#endif
import { WorkspaceContextService } from '../../services/workspace-context-service';

@Component({
  selector: 'app-workspace-denied',
  // prettier-ignore
  imports: [
    NgIcon, HlmButton, ...HlmEmptyImports,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [provideIcons({ lucideLock })],
  templateUrl: './workspace-denied.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkspaceDenied {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly params = toSignal(this.route.queryParamMap, { requireSync: true });
  readonly description = computed(() =>
    this.params().get('reason') === 'forbidden'
      ? 'tenantWorkspace.forbiddenDescription'
      : 'tenantWorkspace.unavailableDescription',
  );
  //#if (!IncludeLocalization)
  readonly t = englishText(ENGLISH);
  //#endif

  async goToWorkspace(): Promise<void> {
    await this.workspace.refresh('reload');
    await this.router.navigateByUrl(this.workspace.landingUrl());
  }
}
//#if (!IncludeLocalization)

const ENGLISH: Record<string, string> = {
  'tenantWorkspace.deniedTitle': 'You cannot access this workspace',
  'tenantWorkspace.forbiddenDescription':
    'Your account belongs to another workspace. Go to your own workspace to continue.',
  'tenantWorkspace.unavailableDescription':
    'This workspace is unavailable or you no longer have access. Go to an available workspace to continue.',
  'tenantWorkspace.goToMine': 'Go to my workspace',
};
//#endif
//#endif
