import { ChangeDetectionStrategy, Component, inject, OnInit } from '@angular/core';
import { CardModule } from 'primeng/card';
import { TagModule } from 'primeng/tag';

import { AuthService } from '../../../../core/services/auth-service';
import { LayoutService } from '../../../../layout/services/layout-service';
import { RoleLabelPipe } from '../../../../shared/pipes/role-label.pipe';

@Component({
  selector: 'app-workspace-dashboard-page',
  standalone: true,
  imports: [CardModule, TagModule, RoleLabelPipe],
  templateUrl: './workspace-dashboard.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class WorkspaceDashboardPage implements OnInit {
  private readonly layoutService = inject(LayoutService);
  readonly authService = inject(AuthService);

  ngOnInit() {
    this.layoutService.title.set('工作台');
  }
}
