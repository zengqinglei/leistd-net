import { Directive } from '@angular/core';
import { classes } from '@spartan-ng/helm/utils';

@Directive({
  selector: '[hlmSheetHeader],hlm-sheet-header',
  host: { 'data-slot': 'sheet-header' },
})
export class HlmSheetHeader {
  constructor() {
    classes(() => 'gap-0.5 p-4 pe-12 pointer-coarse:pe-16 flex flex-col');
  }
}
