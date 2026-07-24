import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { TranslocoModule } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideGlobe } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';

import { Lang, LanguageService } from '../../../core/services/language-service';

interface LangOption {
  id: Lang;
  label: string;
  active: boolean;
}

/**
 * 语言切换器：圆形图标按钮，点击弹出下拉菜单（Spartan dropdown-menu，模板驱动），
 * 当前语言项高亮并显示勾选。
 *
 * 复用于各布局右上角操作区（default-header、landing、login、register）。
 */
@Component({
  selector: 'app-language-switcher',
  standalone: true,
  imports: [HlmButton, NgIcon, TranslocoModule, ...HlmDropdownMenuImports],
  providers: [provideIcons({ lucideGlobe, lucideCheck })],
  host: { class: 'inline-flex items-center' },
  template: `
    <button
      hlmBtn
      variant="ghost"
      size="icon"
      [attr.aria-label]="'language.label' | transloco"
      [hlmDropdownMenuTrigger]="langMenu"
      align="end"
    >
      <ng-icon name="lucideGlobe" />
    </button>

    <ng-template #langMenu>
      <hlm-dropdown-menu class="min-w-36">
        @for (option of items(); track option.id) {
          <button hlmDropdownMenuItem (click)="select(option.id)">
            <ng-icon
              name="lucideCheck"
              data-icon="inline-start"
              [class.invisible]="!option.active"
            />
            <span>{{ option.label }}</span>
          </button>
        }
      </hlm-dropdown-menu>
    </ng-template>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LanguageSwitcher {
  private readonly languageService = inject(LanguageService);

  readonly items = computed<LangOption[]>(() => {
    const active = this.languageService.activeLang();
    return this.languageService.options.map((option) => ({
      id: option.id,
      label: option.label,
      active: option.id === active,
    }));
  });

  select(lang: Lang): void {
    this.languageService.setActiveLang(lang);
  }
}
