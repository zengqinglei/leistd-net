import { ChangeDetectionStrategy, Component, inject } from '@angular/core';

import { LocalizationService, SupportedLocale } from '../../../core/services/localization-service';

@Component({
  selector: 'app-language-switcher',
  standalone: true,
  template: `
    <label class="relative inline-flex items-center">
      <span class="sr-only" i18n="@@languageSwitcher.label">语言</span>
      <i class="pi pi-language absolute left-2.5 pointer-events-none text-muted-color" aria-hidden="true"></i>
      <select
        class="h-9 pl-8 pr-2 rounded-border border border-surface bg-surface-0 dark:bg-surface-900 text-sm text-color cursor-pointer"
        [value]="localization.currentLocale()"
        (change)="switchLocale($event)"
      >
        @for (locale of localization.locales; track locale.code) {
          <option [value]="locale.code">{{ locale.label }}</option>
        }
      </select>
    </label>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LanguageSwitcher {
  readonly localization = inject(LocalizationService);

  switchLocale(event: Event): void {
    this.localization.switchLocale((event.target as HTMLSelectElement).value as SupportedLocale);
  }
}
