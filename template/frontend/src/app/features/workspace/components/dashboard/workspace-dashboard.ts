//#if (IncludeLocalization)
import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#else
import { ChangeDetectionStrategy, Component, inject, OnInit } from '@angular/core';
//#endif
import { CardModule } from 'primeng/card';
import { TagModule } from 'primeng/tag';

//#if (IncludeLocalization)
import { translationReady } from '../../../../core/i18n/translation-ready';
//#endif
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

  // 追踪「翻译就绪」：资源加载完成与语言切换时重算，含首帧避免裸键。
  private readonly translationReady = translationReady(this.transloco);

  constructor() {
    // 读取 translationReady 建立依赖：资源就绪 / 语言切换时本 effect 重跑，标题随之更新。
    effect(() => {
      this.translationReady();
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
