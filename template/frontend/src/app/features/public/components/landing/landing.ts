import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterModule } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { StyleClassModule } from 'primeng/styleclass';

import { LayoutService } from '../../../../layout/services/layout-service';
import { ThemeConfigurator } from '../../../../shared/components/theme-configurator/theme-configurator';

@Component({
  selector: 'app-landing',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterModule, ButtonModule, StyleClassModule, ThemeConfigurator],
  templateUrl: './landing.html'
})
export class Landing {
  public layoutService = inject(LayoutService);
}
