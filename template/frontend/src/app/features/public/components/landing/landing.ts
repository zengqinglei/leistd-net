import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterModule } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoModule } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideShield, lucideSunMoon, lucideUsers } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';

//#if (IncludeLocalization)
import { LanguageSwitcher } from '../../../../shared/components/language-switcher/language-switcher';
//#endif
import { ThemeModeToggle } from '../../../../shared/components/theme-mode-toggle/theme-mode-toggle';

@Component({
  selector: 'app-landing',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  //#if (IncludeLocalization)
  imports: [RouterModule, NgIcon, HlmButton, ThemeModeToggle, LanguageSwitcher, TranslocoModule],
  //#else
  imports: [RouterModule, NgIcon, HlmButton, ThemeModeToggle],
  //#endif
  providers: [provideIcons({ lucideShield, lucideSunMoon, lucideUsers })],
  templateUrl: './landing.html',
})
export class Landing {}
