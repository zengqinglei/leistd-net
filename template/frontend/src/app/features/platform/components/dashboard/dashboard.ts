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
import { HlmCardImports } from '@spartan-ng/helm/card';

import { AuthService } from '../../../../core/services/auth-service';
import { LayoutService } from '../../../../core/services/layout-service';
//#if (!IncludeLocalization)
import { englishText } from '../../../../shared/utils/english-text';
//#endif

@Component({
  selector: 'app-dashboard',
  standalone: true,
  //#if (IncludeLocalization)
  imports: [...HlmCardImports, TranslocoDirective],
  //#else
  imports: [...HlmCardImports],
  //#endif
  templateUrl: './dashboard.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
//#if (IncludeLocalization)
export class Dashboard {
  private readonly layoutService = inject(LayoutService);
  readonly authService = inject(AuthService);

  constructor() {
    const title = translateSignal('platform.dashboard.title', {}, { scope: 'platform' });
    effect(() => this.layoutService.title.set(title()));
  }
}
//#else
export class Dashboard implements OnInit {
  private readonly layoutService = inject(LayoutService);
  readonly authService = inject(AuthService);
  protected readonly t = englishText(ENGLISH);

  ngOnInit() {
    this.layoutService.title.set('Dashboard');
  }
}
//#endif
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'platform.dashboard.welcome': 'Welcome to the Admin Console',
  'platform.dashboard.currentUser': 'Currently signed in as: {{name}}',
  'platform.dashboard.intro':
    'This is a starter template project. You can manage users under "User Management" and extend more feature modules from the left-hand menu.',
};
//#endif
