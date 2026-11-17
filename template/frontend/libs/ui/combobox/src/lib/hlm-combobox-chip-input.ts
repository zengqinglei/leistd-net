import { computed, Directive, inject } from '@angular/core';
import { BrnComboboxChipInput, injectBrnComboboxBase } from '@spartan-ng/brain/combobox';
import { classes, hlmAriaInvalid, syncHlmAriaInvalid } from '@spartan-ng/helm/utils';

@Directive({
  selector: 'input[hlmComboboxChipInput]',
  exportAs: 'hlmComboboxChipInput',
  hostDirectives: [{ directive: BrnComboboxChipInput, inputs: ['id', 'aria-invalid'] }],
  host: { 'data-slot': 'combobox-chip-input', '[attr.aria-invalid]': '_ariaInvalid()' },
})
export class HlmComboboxChipInput {
  private readonly _input = inject(BrnComboboxChipInput);
  private readonly _combobox = injectBrnComboboxBase();
  protected readonly _ariaInvalid = computed(() =>
    hlmAriaInvalid(
      this._input.ariaInvalidInput() ?? this._combobox.controlState?.()?.spartanInvalid,
    ),
  );

  constructor() {
    syncHlmAriaInvalid(null, () => this._ariaInvalid() === 'true');
    classes(() => 'placeholder:text-muted-foreground min-w-16 flex-1 outline-none');
  }
}
