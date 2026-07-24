import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterModule } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoModule } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePalette, lucideShield, lucideUsers } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';

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
    NgIcon,
    HlmButton,
    ThemeConfigurator,
    LanguageSwitcher,
    TranslocoModule,
    ...HlmPopoverImports,
  ],
  //#else
  imports: [RouterModule, NgIcon, HlmButton, ThemeConfigurator, ...HlmPopoverImports],
  //#endif
  providers: [provideIcons({ lucidePalette, lucideShield, lucideUsers })],
  templateUrl: './landing.html',
})
export class Landing {
  public themeService = inject(ThemeService);
}
