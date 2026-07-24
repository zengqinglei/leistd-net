//#if (IncludeLocalization)
import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#else
import { ChangeDetectionStrategy, Component, inject, OnInit } from '@angular/core';
//#endif
import { HlmCardImports } from '@spartan-ng/helm/card';

//#if (IncludeLocalization)
import { translationReady } from '../../../../core/i18n/translation-ready';
//#endif
import { AuthService } from '../../../../core/services/auth-service';
import { LayoutService } from '../../../../layout/services/layout-service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  //#if (IncludeLocalization)
  imports: [...HlmCardImports, TranslocoModule],
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
  private readonly transloco = inject(TranslocoService);

  // 追踪「翻译就绪」：资源加载完成与语言切换时重算，含首帧避免裸键。
  private readonly translationReady = translationReady(this.transloco);

  constructor() {
    effect(() => {
      this.translationReady();
      this.layoutService.title.set(this.transloco.translate('platform.dashboard.title'));
    });
  }
}
//#else
export class Dashboard implements OnInit {
  private readonly layoutService = inject(LayoutService);
  readonly authService = inject(AuthService);

  ngOnInit() {
    this.layoutService.title.set('Dashboard');
  }
}
//#endif
