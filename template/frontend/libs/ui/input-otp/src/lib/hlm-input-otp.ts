import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { BrnInputOtp, BrnInputOtpInput, type InputMode } from '@spartan-ng/brain/input-otp';
import { BrnFieldControlDescribedBy } from '@spartan-ng/brain/field';
import { classes, hlm } from '@spartan-ng/helm/utils';
import type { ClassValue } from 'cn';

@Component({
  selector: 'hlm-input-otp',
  exportAs: 'hlmInputOtp',
  imports: [BrnInputOtpInput, BrnFieldControlDescribedBy],
  changeDetection: ChangeDetectionStrategy.OnPush,
  hostDirectives: [
    {
      directive: BrnInputOtp,
      inputs: [
        'length',
        'value',
        'disabled',
        'inputId',
        'hostStyles',
        'autofocus',
        'transformPaste',
        'mask',
        'maskChar',
        'slotPattern',
      ],
      outputs: ['valueChange', 'completed'],
    },
  ],
  host: { 'data-slot': 'input-otp' },
  template: `
    <ng-content />
    <input
      brnInputOtpInput
      brnFieldControlDescribedBy
      [attr.aria-invalid]="_ariaInvalid()"
      [autocomplete]="inputAutocomplete()"
      [inputMode]="inputMode()"
      [class]="_inputClass()"
      [inputStyles]="inputStyles()"
    />
  `,
})
export class HlmInputOtp {
  private readonly _otp = inject(BrnInputOtp);
  protected readonly _ariaInvalid = computed(() => (this._otp.spartanInvalid?.() ? 'true' : null));

  /** Custom autocomplete attribute applied to the input element */
  public readonly inputAutocomplete = input<'one-time-code' | 'off'>('one-time-code');

  /** Virtual keyboard appearance on mobile */
  public readonly inputMode = input<InputMode>('numeric');

  /** Classes applied to the input element. */
  public readonly inputClass = input<ClassValue>('');

  /** Overrides the default styles applied to the input element. */
  public readonly inputStyles = input<string | undefined>(undefined);

  protected readonly _inputClass = computed(() =>
    hlm('disabled:cursor-not-allowed', this.inputClass()),
  );

  constructor() {
    classes(() => 'gap-2 flex items-center has-disabled:opacity-50');
  }
}
