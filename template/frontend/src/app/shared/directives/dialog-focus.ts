import { afterRenderEffect, Directive, ElementRef, inject, input } from '@angular/core';

/** 异步内容就绪只接续标题上的初始焦点，不覆盖本人已经做出的焦点选择。 */
@Directive({ selector: '[appDialogFocus]' })
export class DialogFocus {
  readonly loading = input.required<boolean>({ alias: 'appDialogFocus' });
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);

  constructor() {
    afterRenderEffect(() => {
      if (this.loading()) return;
      const content = this.element.nativeElement;
      if (document.activeElement !== content.querySelector('[hlmDialogTitle]')) return;
      const controls = content.querySelectorAll<HTMLElement>(
        'input:not([type="file"]), textarea, button[role="combobox"]',
      );
      for (const control of controls) {
        if (
          !control.matches(':disabled,[aria-disabled="true"]') &&
          control.getClientRects().length
        ) {
          control.focus();
          return;
        }
      }
    });
  }
}
