import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterModule } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoModule } from '@jsverse/transloco';
//#endif
import { ButtonModule } from 'primeng/button';
import { StyleClassModule } from 'primeng/styleclass';

import { ThemeService } from '../../../../core/services/theme-service';
//#if (IncludeLocalization)
import { LanguageSwitcher } from '../../../../shared/components/language-switcher/language-switcher';
//#endif
import { ThemeConfigurator } from '../../../../shared/components/theme-configurator/theme-configurator';

@Component({
  selector: 'app-landing',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  //#if (IncludeLocalization)
  imports: [
    RouterModule,
    ButtonModule,
    StyleClassModule,
    ThemeConfigurator,
    LanguageSwitcher,
    TranslocoModule,
  ],
  //#else
  imports: [RouterModule, ButtonModule, StyleClassModule, ThemeConfigurator],
  //#endif
  templateUrl: './landing.html',
})
export class Landing {
  public themeService = inject(ThemeService);
}
