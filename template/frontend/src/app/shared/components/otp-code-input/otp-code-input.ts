import {
  afterRenderEffect,
  computed,
  ChangeDetectionStrategy,
  Component,
  inject,
  input,
  model,
  output,
  untracked,
  viewChild,
} from '@angular/core';
import { FormValueControl } from '@angular/forms/signals';
import { BrnFieldA11yService, BrnFieldControl, provideBrnLabelable } from '@spartan-ng/brain/field';
import { BrnInputOtp, BrnInputOtpInput } from '@spartan-ng/brain/input-otp';
import {
  HlmInputOtpGroup,
  HlmInputOtpSeparator,
  HlmInputOtpSlot,
} from '@spartan-ng/helm/input-otp';

const CODE_LENGTH = 6;

/**
 * 6 位验证码输入：官方 Input OTP 的 3 + 3 格组合。内部输入框带 `autocomplete="one-time-code"`，
 * 粘贴与键入只保留数字。标签须以 `for` 关联内部输入框（`inputId`），宿主上的 aria-label 读不到；
 * 页面已有可见 `<label for>` 时不传 `label`。
 *
 * 原生输入框通过官方指令接入，在模板上关联错误与说明；拒绝验证码时，读屏能读到无效状态和错误。
 */
@Component({
  selector: 'app-otp-code-input',
  hostDirectives: [BrnFieldControl],
  providers: [provideBrnLabelable(OtpCodeInput)],
  host: { '(focusout)': 'touch.emit()' },
  imports: [BrnInputOtp, BrnInputOtpInput, HlmInputOtpGroup, HlmInputOtpSeparator, HlmInputOtpSlot],
  template: `
    @if (label(); as text) {
      <label class="sr-only" [for]="inputId()">{{ text }}</label>
    }
    <brn-input-otp
      class="flex items-center gap-2 has-disabled:opacity-50"
      [length]="length"
      [inputId]="inputId()"
      [value]="value()"
      [autofocus]="autofocus()"
      [disabled]="disabled()"
      slotPattern="[0-9]"
      (valueChange)="onValueChange($event)"
      (completed)="completed.emit($event)"
    >
      <input
        brnInputOtpInput
        class="disabled:cursor-not-allowed"
        autocomplete="one-time-code"
        inputMode="numeric"
        [spellcheck]="false"
        autocapitalize="off"
        [attr.aria-invalid]="displayInvalid() ? 'true' : null"
        [attr.aria-describedby]="descriptionIds()"
      />
      <div hlmInputOtpGroup>
        <hlm-input-otp-slot index="0" [forceInvalid]="displayInvalid()" />
        <hlm-input-otp-slot index="1" [forceInvalid]="displayInvalid()" />
        <hlm-input-otp-slot index="2" [forceInvalid]="displayInvalid()" />
      </div>
      <hlm-input-otp-separator />
      <div hlmInputOtpGroup>
        <hlm-input-otp-slot index="3" [forceInvalid]="displayInvalid()" />
        <hlm-input-otp-slot index="4" [forceInvalid]="displayInvalid()" />
        <hlm-input-otp-slot index="5" [forceInvalid]="displayInvalid()" />
      </div>
    </brn-input-otp>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OtpCodeInput implements FormValueControl<string> {
  private readonly fieldControl = inject(BrnFieldControl);
  protected readonly displayInvalid = computed(
    () => this.fieldControl.spartanInvalid() ?? this.invalid(),
  );
  private readonly fieldA11y = inject(BrnFieldA11yService, { optional: true });
  readonly touch = output<void>();

  /** 内部输入框的 id，供 `<label for>` 关联。 */
  readonly inputId = input.required<string>();
  readonly labelableId = this.inputId;
  /** 仅供读屏器的标签；页面上已有可见标签时留空。 */
  readonly label = input<string>('');
  readonly autofocus = input(false);
  readonly disabled = input(false);
  /** 验证码被拒：内部输入框带 `aria-invalid`，各格显示错误样式。 */
  readonly invalid = input(false);
  /** 说明与错误元素的 id（空格分隔），写到内部输入框的 `aria-describedby`。 */
  readonly describedBy = input<string | null>(null);
  /** 当前值，只含数字。 */
  readonly value = model('');
  /** 输满 6 位时触发，适合用来自动提交。 */
  readonly completed = output<string>();

  protected readonly length = CODE_LENGTH;
  private readonly otp = viewChild(BrnInputOtp);

  constructor() {
    // 同轮输满并被拒时，父值清回空串，模板绑定可能仍记着空串，Brain 会保留旧码。
    // 渲染后再同步公开 value：新版 length 为必填输入，普通 effect 会在它初始化前读取。
    afterRenderEffect(() => {
      const value = this.value();
      untracked(() => {
        const otp = this.otp();
        if (otp && (otp.value() ?? '') !== value) otp.value.set(value);
      });
    });
  }

  protected readonly descriptionIds = computed(
    () =>
      [
        ...new Set(
          [this.describedBy(), this.fieldA11y?.describedBy()]
            .flatMap((ids) => (ids ?? '').split(/\s+/))
            .filter(Boolean),
        ),
      ].join(' ') || null,
  );

  /** 把焦点放进内部输入框（切换方式或输错清空后请本人重输时用）。 */
  focus(): void {
    this.otp()?.focus();
  }

  protected onValueChange(value: string): void {
    this.value.set(value);
  }
}
