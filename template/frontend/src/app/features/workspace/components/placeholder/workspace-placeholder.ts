import { ChangeDetectionStrategy, Component } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoModule } from '@jsverse/transloco';
//#endif

@Component({
  selector: 'app-workspace-placeholder',
  standalone: true,
  //#if (IncludeLocalization)
  imports: [TranslocoModule],
  //#endif
  templateUrl: './workspace-placeholder.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkspacePlaceholder {}
