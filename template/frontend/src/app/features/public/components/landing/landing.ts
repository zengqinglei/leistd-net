import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterModule } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideShield, lucideSunMoon, lucideUsers } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';

//#if (IncludeLocalization)
import { LanguageSwitcher } from '../../../../shared/components/language-switcher/language-switcher';
//#endif
import { ThemeModeToggle } from '../../../../shared/components/theme-mode-toggle/theme-mode-toggle';
//#if (!IncludeLocalization)
import { englishText } from '../../../../shared/utils/english-text';
//#endif

@Component({
  selector: 'app-landing',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  //#if (IncludeLocalization)
  imports: [RouterModule, NgIcon, HlmButton, ThemeModeToggle, LanguageSwitcher, TranslocoDirective],
  //#else
  imports: [RouterModule, NgIcon, HlmButton, ThemeModeToggle],
  //#endif
  providers: [provideIcons({ lucideShield, lucideSunMoon, lucideUsers })],
  templateUrl: './landing.html',
})
//#if (IncludeLocalization)
export class Landing {}
//#else
export class Landing {
  protected readonly t = englishText(ENGLISH);
}
//#endif
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'hero.title': 'Template Project',
  'hero.subtitle':
    'A full-stack application development template based on Angular + Spartan UI + .NET 10 DDD architecture',
  'hero.login': 'Sign In',
  'hero.register': 'Sign Up',
  'feature.authTitle': 'Authentication',
  'feature.authDesc': 'Cookie-based authentication with role and permission management',
  'feature.usersTitle': 'User Management',
  'feature.usersDesc': 'Full CRUD operations with permission control',
  'feature.themeTitle': 'Theme System',
  'feature.themeDesc': 'Dark mode + Tailwind CSS + Spartan UI',
};
//#endif
