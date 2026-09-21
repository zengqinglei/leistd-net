import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePanelLeft } from '@ng-icons/lucide';
import { HlmButton, provideBrnButtonConfig } from '@spartan-ng/helm/button';
import { injectHlmA11yLabels } from '@spartan-ng/helm/utils';
import { HlmSidebarService } from './hlm-sidebar.service';

@Component({
  // eslint-disable-next-line @angular-eslint/component-selector
  selector: 'button[hlmSidebarTrigger]',
  imports: [NgIcon],
  providers: [
    provideIcons({ lucidePanelLeft }),
    provideBrnButtonConfig({ variant: 'ghost', size: 'icon-sm' }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  hostDirectives: [{ directive: HlmButton, inputs: ['variant', 'size'] }],
  host: {
    'data-slot': 'sidebar-trigger',
    'data-sidebar': 'trigger',
    '(click)': '_onClick()',
  },
  template: `
    <ng-icon name="lucidePanelLeft" />
    <span class="sr-only">{{ label() }}</span>
  `,
})
export class HlmSidebarTrigger {
  private readonly _sidebarService = inject(HlmSidebarService);

  private readonly _a11y = injectHlmA11yLabels();

  /** 逐处覆盖用；不传时取应用级无障碍文案，随语言切换。 */
  public readonly srOnlyText = input<string>();

  protected readonly label = computed(() => this.srOnlyText() ?? this._a11y.toggleSidebar());

  protected _onClick(): void {
    this._sidebarService.toggleSidebar();
  }
}
