import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { HlmProgressImports } from '@spartan-ng/helm/progress';

//#if (!IncludeLocalization)
import { englishText } from '../../../shared/utils/english-text';
//#endif
import { NavigationProgressService } from '../../services/navigation-progress-service';

/** 挂在根组件以跨布局保留等待提示；页面自己的首屏骨架仍由页面负责。 */
@Component({
  selector: 'app-navigation-progress',
  // prettier-ignore
  imports: [
    ...HlmProgressImports,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  templateUrl: './navigation-progress.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NavigationProgress {
  protected readonly progress = inject(NavigationProgressService);
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif
}
//#if (!IncludeLocalization)

const ENGLISH = {
  'app.navigation.loading': 'Loading...',
} as const;
//#endif
