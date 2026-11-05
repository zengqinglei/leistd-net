import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { NavigationProgress } from '../../core/components/navigation-progress/navigation-progress';
import { CommandPalette } from '../components/command-palette/command-palette';
import { DefaultHeader } from '../components/default-header/default-header';
//#if (IncludeRealTime)
import { RealtimeStatus } from '../components/realtime-status/realtime-status';
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
    CommandPalette,
    DefaultHeader,
    NavigationProgress,
    //#if (IncludeRealTime)
    RealtimeStatus,
    //#endif
  ],
  templateUrl: './workspace-layout.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkspaceLayout {}
