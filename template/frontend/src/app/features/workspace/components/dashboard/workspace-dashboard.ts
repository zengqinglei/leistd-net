//#if (IncludeLocalization)
import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#else
import { ChangeDetectionStrategy, Component, inject, OnInit } from '@angular/core';
//#endif
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmCardImports } from '@spartan-ng/helm/card';

//#if (IncludeLocalization)
import { translationReady } from '../../../../core/i18n/translation-ready';
//#endif
import { AuthService } from '../../../../core/services/auth-service';
import { LayoutService } from '../../../../layout/services/layout-service';
import { RoleLabelPipe } from '../../../../shared/pipes/role-label-pipe';

@Component({
  selector: 'app-workspace-dashboard',
  standalone: true,
  //#if (IncludeLocalization)
  imports: [...HlmCardImports, HlmBadge, RoleLabelPipe, TranslocoModule],
  //#else
  imports: [...HlmCardImports, HlmBadge, RoleLabelPipe],
  //#endif
  templateUrl: './workspace-dashboard.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
//#if (IncludeLocalization)
export class WorkspaceDashboard {
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
export class WorkspaceDashboard implements OnInit {
  private readonly layoutService = inject(LayoutService);
  readonly authService = inject(AuthService);

  ngOnInit() {
    this.layoutService.title.set('Workbench');
  }
}
//#endif
