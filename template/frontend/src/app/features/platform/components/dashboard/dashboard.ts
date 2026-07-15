//#if (IncludeLocalization)
import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#else
import { ChangeDetectionStrategy, Component, inject, OnInit } from '@angular/core';
//#endif
import { CardModule } from 'primeng/card';

import { AuthService } from '../../../../core/services/auth-service';
import { LayoutService } from '../../../../layout/services/layout-service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  //#if (IncludeLocalization)
  imports: [CardModule, TranslocoModule],
  //#else
  imports: [CardModule],
  //#endif
  templateUrl: './dashboard.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
//#if (IncludeLocalization)
export class Dashboard {
  private readonly layoutService = inject(LayoutService);
  readonly authService = inject(AuthService);
  private readonly transloco = inject(TranslocoService);

  // 追踪活动语言：切换时 effect 重跑，layout 标题随之更新（避免只在 ngOnInit 定死一次）。
  private readonly activeLang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });

  constructor() {
    effect(() => {
      this.activeLang();
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
