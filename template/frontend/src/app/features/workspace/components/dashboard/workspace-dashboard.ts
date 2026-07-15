//#if (IncludeLocalization)
import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#else
import { ChangeDetectionStrategy, Component, inject, OnInit } from '@angular/core';
//#endif
import { CardModule } from 'primeng/card';
import { TagModule } from 'primeng/tag';

import { AuthService } from '../../../../core/services/auth-service';
import { LayoutService } from '../../../../layout/services/layout-service';
import { RoleLabelPipe } from '../../../../shared/pipes/role-label.pipe';

@Component({
  selector: 'app-workspace-dashboard-page',
  standalone: true,
  //#if (IncludeLocalization)
  imports: [CardModule, TagModule, RoleLabelPipe, TranslocoModule],
  //#else
  imports: [CardModule, TagModule, RoleLabelPipe],
  //#endif
  templateUrl: './workspace-dashboard.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
//#if (IncludeLocalization)
export class WorkspaceDashboardPage {
  private readonly layoutService = inject(LayoutService);
  readonly authService = inject(AuthService);
  private readonly transloco = inject(TranslocoService);

  // 追踪活动语言：切换时该 signal 变化 → effect 重跑 → 标题重新翻译。
  private readonly activeLang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });

  constructor() {
    // 读取 activeLang 建立依赖：语言切换时本 effect 重跑，标题随之更新。
    effect(() => {
      this.activeLang();
      this.layoutService.title.set(this.transloco.translate('workspace.dashboard.title'));
    });
  }
}
//#else
export class WorkspaceDashboardPage implements OnInit {
  private readonly layoutService = inject(LayoutService);
  readonly authService = inject(AuthService);

  ngOnInit() {
    this.layoutService.title.set('Workbench');
  }
}
//#endif
