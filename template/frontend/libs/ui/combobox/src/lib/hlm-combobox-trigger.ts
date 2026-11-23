import { BooleanInput } from '@angular/cdk/coercion';
import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
} from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronDown } from '@ng-icons/lucide';
import {
  BrnComboboxAnchor,
  injectBrnComboboxBase,
  BrnComboboxPopoverTrigger,
  BrnComboboxTrigger,
} from '@spartan-ng/brain/combobox';
import { BrnFieldControlDescribedBy } from '@spartan-ng/brain/field';
import { ButtonVariants, HlmButton } from '@spartan-ng/helm/button';
import { hlm, hlmAriaInvalid, syncHlmAriaInvalid } from '@spartan-ng/helm/utils';
import type { ClassValue } from 'clsx';

@Component({
  selector: 'hlm-combobox-trigger',
  exportAs: 'hlmComboboxTrigger',
  imports: [
    NgIcon,
    HlmButton,
    BrnComboboxAnchor,
    BrnComboboxTrigger,
    BrnComboboxPopoverTrigger,
    BrnFieldControlDescribedBy,
  ],
  providers: [provideIcons({ lucideChevronDown })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'contents' },
  template: `
    <button
      brnComboboxTrigger
      brnComboboxAnchor
      brnComboboxPopoverTrigger
      brnFieldControlDescribedBy
      hlmBtn
      data-slot="combobox-trigger"
      [id]="buttonId()"
      [class]="_computedClass()"
      [variant]="variant()"
      [forceInvalid]="forceInvalid()"
      [attr.aria-invalid]="_ariaInvalid()"
    >
      <ng-content />
      <ng-icon name="lucideChevronDown" class="text-muted-foreground text-[length:--spacing(4)]" />
    </button>
  `,
})
export class HlmComboboxTrigger {
  constructor() {
    syncHlmAriaInvalid('button[brnComboboxTrigger]', () => this._ariaInvalid() === 'true');
  }

  private static _id = 0;
  private readonly _control = injectBrnComboboxBase();
  protected readonly _ariaInvalid = computed(() =>
    hlmAriaInvalid(this.forceInvalid() || this._control.controlState?.()?.spartanInvalid),
  );

  public readonly userClass = input<ClassValue>('', {
    alias: 'class',
  });
  protected readonly _computedClass = computed(() =>
    hlm('data-placeholder:text-muted-foreground', this.userClass()),
  );

  public readonly buttonId = input<string>(`hlm-combobox-trigger-${HlmComboboxTrigger._id++}`);

  public readonly variant = input<ButtonVariants['variant']>('outline');

  public readonly forceInvalid = input<boolean, BooleanInput>(false, {
    transform: booleanAttribute,
  });
}
