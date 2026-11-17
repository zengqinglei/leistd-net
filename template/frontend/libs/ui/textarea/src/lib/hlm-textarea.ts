import { computed, Directive, inject } from '@angular/core';
import { BrnFieldControl, BrnFieldControlDescribedBy } from '@spartan-ng/brain/field';
import { BrnTextarea } from '@spartan-ng/brain/textarea';
import { classes, hlmAriaInvalid, syncHlmAriaInvalid } from '@spartan-ng/helm/utils';

@Directive({
  selector: '[hlmTextarea]',
  exportAs: 'hlmTextarea',
  hostDirectives: [
    { directive: BrnTextarea, inputs: ['id', 'forceInvalid'] },
    BrnFieldControlDescribedBy,
  ],
  host: { 'data-slot': 'textarea', '[attr.aria-invalid]': '_ariaInvalid()' },
})
export class HlmTextarea {
  private readonly _brain = inject(BrnTextarea);
  private readonly _fieldControl = inject(BrnFieldControl);
  protected readonly _ariaInvalid = computed(() =>
    hlmAriaInvalid(this._brain.forceInvalid() || this._fieldControl.spartanInvalid()),
  );

  constructor() {
    // 无自身字段状态的内部输入由所属组合控件处理。
    syncHlmAriaInvalid(null, () =>
      this._fieldControl.controlState() ? this._ariaInvalid() === 'true' : undefined,
    );
    classes(
      () =>
        'border-input dark:bg-input/30 focus-visible:border-ring focus-visible:ring-ring/50 data-[matches-spartan-invalid=true]:ring-destructive/20 dark:data-[matches-spartan-invalid=true]:ring-destructive/40 data-[matches-spartan-invalid=true]:border-destructive dark:data-[matches-spartan-invalid=true]:border-destructive/50 rounded-md border bg-transparent px-2.5 py-2 text-base shadow-xs transition-[color,box-shadow] focus-visible:ring-3 data-[matches-spartan-invalid=true]:ring-3 md:text-sm placeholder:text-muted-foreground flex field-sizing-content min-h-16 w-full outline-none disabled:cursor-not-allowed disabled:opacity-50',
    );
  }
}
