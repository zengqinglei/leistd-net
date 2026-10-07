// prettier-ignore
import {
  ChangeDetectionStrategy,
  Component,
  //#if (IncludeLocalization)
  effect,
  //#endif
  inject,
  //#if (!IncludeLocalization)
  OnInit,
  //#endif
} from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoDirective, translateSignal } from '@jsverse/transloco';
//#endif
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmCardImports } from '@spartan-ng/helm/card';

import { AuthService } from '../../../../core/services/auth-service';
import { LayoutService } from '../../../../core/services/layout-service';
//#if (!IncludeLocalization)
import { englishText } from '../../../../shared/utils/english-text';
//#endif

@Component({
  selector: 'app-workspace-dashboard',
  standalone: true,
  //#if (IncludeLocalization)
  imports: [...HlmCardImports, HlmBadge, TranslocoDirective],
  //#else
  imports: [...HlmCardImports, HlmBadge],
  //#endif
  templateUrl: './workspace-dashboard.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
//#if (IncludeLocalization)
export class WorkspaceDashboard {
  private readonly layoutService = inject(LayoutService);
  readonly authService = inject(AuthService);

  constructor() {
    const title = translateSignal('workspace.dashboard.title', {}, { scope: 'workspace' });
    effect(() => this.layoutService.title.set(title()));
  }
}
//#else
export class WorkspaceDashboard implements OnInit {
  private readonly layoutService = inject(LayoutService);
  readonly authService = inject(AuthService);
  protected readonly t = englishText(ENGLISH);

  ngOnInit() {
    this.layoutService.title.set('Workbench');
  }
}
//#endif
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'workspace.dashboard.title': 'Workbench',
  'workspace.dashboard.username': 'Username',
  'workspace.dashboard.displayName': 'Display name',
  'workspace.dashboard.email': 'Email',
  'workspace.dashboard.roles': 'Roles',
  'workspace.dashboard.noUserInfo': 'No user information available',
};
//#endif
