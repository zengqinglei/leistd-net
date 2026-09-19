import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { DefaultHeader } from '../components/default-header/default-header';

/**
 * 工作空间布局：顶栏导航，没有侧栏。
 *
 * 菜单与管理平台的侧栏读同一份（见 NavigationService）。工作空间的入口多到顶栏放不下、
 * 或需要分组标题与按权限整组裁剪时，把路由换回 DefaultLayout 即可，判据见
 * docs/standards/coding-frontend.md §8。
 */
@Component({
  selector: 'app-workspace-layout',
  imports: [RouterOutlet, DefaultHeader],
  templateUrl: './workspace-layout.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkspaceLayout {}
