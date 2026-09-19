import { ChangeDetectionStrategy, Component, input, model, output } from '@angular/core';
import { BrnInputOtpImports } from '@spartan-ng/brain/input-otp';
import { HlmInputOtpImports } from '@spartan-ng/helm/input-otp';

const CODE_LENGTH = 6;

/**
 * 6 位验证码输入：官方 Input OTP 的固定组合（3 + 3 格）。
 *
 * 内部输入框自带 `autocomplete="one-time-code"` 与数字键盘，短信与密码管理器的自动填充照常可用。
 * 粘贴与键入都只保留数字，"123 456""123-456"这类带分隔的写法也能落成 6 位。
 *
 * 标签必须以 `for` 关联到内部输入框（`inputId`）：挂在宿主元素上的 aria-label 读屏器读不到。
 * 页面上已有可见 `<label for>` 时不传 `label`，否则这里补一个仅供读屏器的标签。
 */
@Component({
  selector: 'app-otp-code-input',
  imports: [...BrnInputOtpImports, ...HlmInputOtpImports],
  template: `
    @if (label(); as text) {
      <label class="sr-only" [for]="inputId()">{{ text }}</label>
    }
    <brn-input-otp
      hlmInputOtp
      [length]="length"
      [inputId]="inputId()"
      [value]="value()"
      [autofocus]="autofocus()"
      [disabled]="disabled()"
      [transformPaste]="digitsOnly"
      (valueChange)="onValueChange($event)"
      (completed)="completed.emit(digitsOnly($event))"
    >
      <div hlmInputOtpGroup>
        <hlm-input-otp-slot index="0" />
        <hlm-input-otp-slot index="1" />
        <hlm-input-otp-slot index="2" />
      </div>
      <hlm-input-otp-separator />
      <div hlmInputOtpGroup>
        <hlm-input-otp-slot index="3" />
        <hlm-input-otp-slot index="4" />
        <hlm-input-otp-slot index="5" />
      </div>
    </brn-input-otp>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OtpCodeInput {
  /** 内部输入框的 id，供 `<label for>` 关联。 */
  readonly inputId = input.required<string>();
  /** 仅供读屏器的标签；页面上已有可见标签时留空。 */
  readonly label = input<string>('');
  readonly autofocus = input(false);
  readonly disabled = input(false);
  /** 当前值，只含数字。 */
  readonly value = model('');
  /** 输满 6 位时触发，适合用来自动提交。 */
  readonly completed = output<string>();

  protected readonly length = CODE_LENGTH;

  protected readonly digitsOnly = (text: string): string => text.replace(/\D/g, '');

  protected onValueChange(raw: string): void {
    this.value.set(this.digitsOnly(raw));
  }
}
