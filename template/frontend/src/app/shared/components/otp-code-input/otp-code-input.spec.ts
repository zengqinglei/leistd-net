import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { disabled, form, FormField, pattern, required } from '@angular/forms/signals';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { userEvent } from 'vitest/browser';

import { OtpCodeInput } from './otp-code-input';

@Component({
  imports: [OtpCodeInput, FormField, ...HlmFieldImports],
  template: `<div hlmField>
      <label hlmFieldLabel for="otp-test">Verification code</label>
      <app-otp-code-input inputId="otp-test" [formField]="fields.code" />
      @for (error of fields.code().errors(); track error.kind) {
        <hlm-field-error [validator]="error.kind">Enter all six digits</hlm-field-error>
      }
    </div>
    <button type="button">Next</button>`,
})
class OtpHost {
  readonly locked = signal(false);
  readonly model = signal({ code: '' });
  readonly fields = form(this.model, (path) => {
    required(path.code);
    pattern(path.code, /^\d{6}$/);
    disabled(path.code, () => this.locked());
  });
}

describe('OtpCodeInput', () => {
  it('binds values, clears and accepts a second complete code', async () => {
    const fixture = TestBed.createComponent(OtpHost);
    document.body.append(fixture.nativeElement);
    await fixture.whenStable();
    const input = document.getElementById('otp-test') as HTMLInputElement;
    await userEvent.fill(input, '123456');
    expect(fixture.componentInstance.model().code).toBe('123456');
    fixture.componentInstance.model.set({ code: '' });
    await fixture.whenStable();
    expect(input.value).toBe('');
    await userEvent.fill(input, '654321');
    expect(fixture.componentInstance.model().code).toBe('654321');
    fixture.destroy();
  });

  it('propagates blur, disabled, label and visible error descriptions to the internal input', async () => {
    const fixture = TestBed.createComponent(OtpHost);
    document.body.append(fixture.nativeElement);
    await fixture.whenStable();
    const input = document.getElementById('otp-test') as HTMLInputElement;
    input.focus();
    await userEvent.tab();
    await fixture.whenStable();
    const field = fixture.componentInstance.fields.code();
    expect(field.touched()).toBe(true);
    expect(input.getAttribute('aria-invalid')).toBe('true');
    const ids = input.getAttribute('aria-describedby')?.split(' ') ?? [];
    expect(
      ids.some((id) => document.getElementById(id)?.textContent?.includes('Enter all six digits')),
    ).toBe(true);
    expect(input.spellcheck).toBe(false);
    fixture.componentInstance.locked.set(true);
    await fixture.whenStable();
    expect(input.disabled).toBe(true);
    fixture.destroy();
  });
});
