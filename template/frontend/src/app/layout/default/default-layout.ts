import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';

import { NavigationProgress } from '../../core/components/navigation-progress/navigation-progress';
import { CommandPalette } from '../components/command-palette/command-palette';
import { DefaultHeader } from '../components/default-header/default-header';
import { DefaultSidebar } from '../components/default-sidebar/default-sidebar';
//#if (IncludeRealTime)
import { RealtimeStatus } from '../components/realtime-status/realtime-status';
//#endif

@Component({
  selector: 'app-default-layout',
  standalone: true,
  // prettier-ignore
  imports: [
    RouterOutlet,
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
export class DefaultLayout {}
