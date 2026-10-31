import { DestroyRef, Directive, ElementRef, inject } from '@angular/core';
import { BrnCommand } from '@spartan-ng/brain/command';
import { classes } from '@spartan-ng/helm/utils';

@Directive({
  selector: '[hlmCommand],hlm-command',
  exportAs: 'hlmCommand',
  hostDirectives: [
    {
      directive: BrnCommand,
      inputs: ['id', 'filter', 'search', 'disabled'],
      outputs: ['valueChange', 'searchChange'],
    },
  ],
  host: {
    'data-slot': 'command',
  },
})
export class HlmCommand {
  constructor() {
    const element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    const guard = (event: KeyboardEvent) => {
      if (event.key === 'Enter' && (event.isComposing || event.keyCode === 229))
        event.stopImmediatePropagation();
    };
    element.addEventListener('keydown', guard, true);
    inject(DestroyRef).onDestroy(() => element.removeEventListener('keydown', guard, true));
    classes(
      () =>
        'bg-popover text-popover-foreground rounded-xl! p-1 flex size-full flex-col overflow-hidden',
    );
  }
}
