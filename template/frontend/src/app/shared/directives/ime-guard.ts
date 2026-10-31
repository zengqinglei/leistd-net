import { DestroyRef, Directive, ElementRef, inject } from '@angular/core';

/** 组字键只交给输入法；不 preventDefault，以保留选择与取消候选的原生行为。 */
@Directive({ selector: '[appImeGuard]' })
export class ImeGuard {
  constructor() {
    const element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    const guard = (event: KeyboardEvent) => {
      if (
        (event.key === 'Enter' || event.key === 'Escape') &&
        (event.isComposing || event.keyCode === 229)
      ) {
        event.stopImmediatePropagation();
      }
    };
    element.addEventListener('keydown', guard, true);
    inject(DestroyRef).onDestroy(() => element.removeEventListener('keydown', guard, true));
  }
}
