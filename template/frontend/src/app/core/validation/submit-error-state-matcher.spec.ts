import { Component, ElementRef, signal, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormField, form, required, validate } from '@angular/forms/signals';
import { provideErrorStateMatcher } from '@spartan-ng/brain/forms';
import { HlmCheckbox } from '@spartan-ng/helm/checkbox';
import { HlmComboboxImports } from '@spartan-ng/helm/combobox';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmInputOtpImports } from '@spartan-ng/helm/input-otp';
import { HlmRadioGroupImports } from '@spartan-ng/helm/radio-group';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmSwitch } from '@spartan-ng/helm/switch';
import { HlmTextarea } from '@spartan-ng/helm/textarea';

//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../i18n/transloco.testing';
//#endif
import { injectFormSubmission } from './form-submission';
import { renderSubmission, rejectedField } from './form-submission.testing';
import { SubmitErrorStateMatcher } from './submit-error-state-matcher';

@Component({
  selector: 'app-submission-test',
  imports: [
    FormField,
    HlmInput,
    HlmTextarea,
    HlmCheckbox,
    HlmSwitch,
    ...HlmFieldImports,
    ...HlmRadioGroupImports,
    ...HlmSelectImports,
    ...HlmComboboxImports,
    ...HlmInputOtpImports,
  ],
  template: `
    <form #root novalidate (submit)="$event.preventDefault(); save()">
      <div hlmField>
        <input
          hlmInput
          aria-label="Name"
          [formField]="fields.name"
          [forceInvalid]="forceInvalid()"
        />
        @for (error of fields.name().errors(); track error.kind) {
          <hlm-field-error [validator]="error.kind">{{
            error.message ?? error.kind
          }}</hlm-field-error>
        }
      </div>
      <div hlmField data-testid="select">
        <hlm-select [formField]="fields.choice">
          <hlm-select-trigger [forceInvalid]="forceInvalid()" [aria-invalid]="manualInvalid()"
            ><hlm-select-value
          /></hlm-select-trigger>
          <hlm-select-content *hlmSelectPortal
            ><hlm-select-item value="Valid">Valid</hlm-select-item></hlm-select-content
          >
        </hlm-select>
        <hlm-field-error validator="required">Required choice</hlm-field-error>
      </div>
      <div hlmField data-testid="combobox">
        <hlm-combobox [formField]="fields.combo">
          <hlm-combobox-input [forceInvalid]="forceInvalid()" [aria-invalid]="manualInvalid()" />
          <hlm-combobox-content *hlmComboboxPortal
            ><div hlmComboboxList>
              <hlm-combobox-item value="Valid">Valid</hlm-combobox-item>
            </div></hlm-combobox-content
          >
        </hlm-combobox>
        <hlm-field-error validator="required">Required combo</hlm-field-error>
      </div>
      <div hlmField data-testid="checkbox">
        <hlm-checkbox [formField]="fields.checked" [forceInvalid]="forceInvalid()" />
        <hlm-field-error validator="required">Required checkbox</hlm-field-error>
      </div>
      <div hlmField data-testid="switch">
        <hlm-switch [formField]="fields.enabled" [forceInvalid]="forceInvalid()" />
        <hlm-field-error validator="required">Required switch</hlm-field-error>
      </div>
      <div hlmField data-testid="otp">
        <hlm-input-otp [formField]="fields.code" length="1"
          ><hlm-input-otp-slot index="0"
        /></hlm-input-otp>
        <hlm-field-error validator="required">Required code</hlm-field-error>
      </div>
      <div hlmField data-testid="radio">
        <hlm-radio-group [formField]="fields.radio"
          ><hlm-radio value="Valid">Valid</hlm-radio></hlm-radio-group
        >
        <hlm-field-error validator="required">Required radio</hlm-field-error>
      </div>
      <div hlmField data-testid="textarea">
        <textarea hlmTextarea [formField]="fields.notes" [forceInvalid]="forceInvalid()"></textarea>
        <hlm-field-error validator="required">Required notes</hlm-field-error>
      </div>
      <div hlmField>
        <input hlmInput aria-label="Other" [formField]="other.name" />
        <hlm-field-error validator="required">Required</hlm-field-error>
      </div>
      <button type="submit">Save</button>
    </form>
  `,
})
class SubmissionTest {
  readonly forceInvalid = signal(false);
  readonly manualInvalid = signal<boolean | undefined>(undefined);
  readonly model = signal({
    name: '',
    choice: 'Valid',
    combo: 'Valid',
    checked: true,
    enabled: true,
    code: '1',
    notes: 'Valid',
    radio: 'Valid',
  });
  readonly fields = form(this.model, (path) => {
    required(path.name);
    required(path.choice);
    required(path.combo);
    required(path.code);
    required(path.notes);
    required(path.radio);
    validate(path.checked, ({ value }) => (value() ? null : { kind: 'required' }));
    validate(path.enabled, ({ value }) => (value() ? null : { kind: 'required' }));
  });
  readonly other = form(signal({ name: '' }), (path) => required(path.name));
  readonly action = vi.fn(async () => undefined);
  private readonly submitForm = injectFormSubmission();
  private readonly root = viewChild.required<ElementRef<HTMLElement>>('root');
  save() {
    return this.submitForm(this.fields, this.root().nativeElement, this.action);
  }
}

describe('SubmitErrorStateMatcher', () => {
  async function setup() {
    TestBed.configureTestingModule({
      //#if (IncludeLocalization)
      providers: [provideErrorStateMatcher(SubmitErrorStateMatcher), ...provideTranslocoTesting()],
      //#else
      providers: [provideErrorStateMatcher(SubmitErrorStateMatcher)],
      //#endif
    });
    const fixture = TestBed.createComponent(SubmissionTest);
    fixture.autoDetectChanges();
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    const input = host.querySelector('input')!;
    const errors = () =>
      Array.from(host.querySelectorAll<HTMLElement>('hlm-field-error')).filter(
        (error) => !error.hidden,
      );
    return { fixture, host, input, errors, component: fixture.componentInstance };
  }

  it('waits for submission, isolates forms, revalidates immediately and clears on reset', async () => {
    const { fixture, input, errors, component } = await setup();
    input.focus();
    input.blur();
    await fixture.whenStable();
    expect(errors()).toHaveLength(0);
    expect(input.getAttribute('aria-invalid')).toBeNull();
    expect(input.getAttribute('data-matches-spartan-invalid')).not.toBe('true');
    await renderSubmission(fixture, () => component.save());
    expect(errors()).toHaveLength(1);
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.getAttribute('aria-describedby')?.split(' ')).toContain(errors()[0].id);
    expect(document.activeElement).toBe(input);
    expect(component.action).not.toHaveBeenCalled();
    input.value = 'Valid';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await fixture.whenStable();
    expect(errors()).toHaveLength(0);
    expect(input.getAttribute('aria-invalid')).toBeNull();
    input.value = '';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await fixture.whenStable();
    expect(errors()).toHaveLength(1);
    component.fields().reset();
    await fixture.whenStable();
    input.focus();
    input.blur();
    await fixture.whenStable();
    expect(errors()).toHaveLength(0);
    expect(input.getAttribute('aria-invalid')).toBeNull();
    await renderSubmission(fixture, () => component.save());
    expect(errors()).toHaveLength(1);
  });

  for (const [kind, field, selector, empty, valid] of [
    ['select', 'choice', 'button[role="combobox"]', '', 'Valid'],
    ['combobox', 'combo', 'input', '', 'Valid'],
    ['checkbox', 'checked', 'button[role="checkbox"]', false, true],
    ['switch', 'enabled', 'button[role="switch"]', false, true],
    ['otp', 'code', 'input', '', '1'],
    ['textarea', 'notes', 'textarea', '', 'Valid'],
  ] as const) {
    it(`synchronizes ${kind} errors, descriptions, internal focus and reset`, async () => {
      const { fixture, host, component } = await setup();
      component.model.update((value) => ({ ...value, name: 'Valid', [field]: empty }));
      await fixture.whenStable();
      const wrapper = host.querySelector<HTMLElement>(`[data-testid="${kind}"]`)!;
      const control = wrapper.querySelector<HTMLElement>(selector)!;
      const error = wrapper.querySelector<HTMLElement>('hlm-field-error')!;
      control.focus();
      control.blur();
      await fixture.whenStable();
      expect(control.getAttribute('aria-invalid')).toBeNull();
      expect(error.hidden).toBe(true);
      await renderSubmission(fixture, () => component.save());
      expect(control.getAttribute('aria-invalid')).toBe('true');
      expect(error.hidden).toBe(false);
      expect(control.getAttribute('aria-describedby')?.split(' ')).toContain(error.id);
      expect(document.activeElement).toBe(control);
      component.model.update((value) => ({ ...value, [field]: valid }));
      await fixture.whenStable();
      expect(control.getAttribute('aria-invalid')).toBeNull();
      expect(error.hidden).toBe(true);
      expect(control.getAttribute('aria-describedby')?.split(' ') ?? []).not.toContain(error.id);
      component.model.update((value) => ({ ...value, [field]: empty }));
      await fixture.whenStable();
      expect(control.getAttribute('aria-invalid')).toBe('true');
      component.fields().reset();
      await fixture.whenStable();
      expect(control.getAttribute('aria-invalid')).toBeNull();
      expect(error.hidden).toBe(true);
    });
  }

  it('synchronizes radio group and its native input, including later invalidation', async () => {
    const { fixture, host, component } = await setup();
    component.model.update((value) => ({ ...value, name: 'Valid', radio: '' }));
    await fixture.whenStable();
    const group = host.querySelector<HTMLElement>('hlm-radio-group')!;
    const radio = group.querySelector<HTMLInputElement>('input[type="radio"]')!;
    radio.focus();
    radio.blur();
    await fixture.whenStable();
    expect(group.getAttribute('aria-invalid')).toBeNull();
    expect(radio.getAttribute('aria-invalid')).toBeNull();
    await renderSubmission(fixture, () => component.save());
    expect(group.getAttribute('aria-invalid')).toBe('true');
    expect(radio.getAttribute('aria-invalid')).toBe('true');
    expect(document.activeElement).toBe(radio);
    radio.click();
    await fixture.whenStable();
    expect(group.getAttribute('aria-invalid')).toBeNull();
    expect(radio.getAttribute('aria-invalid')).toBeNull();
    component.model.update((value) => ({ ...value, radio: '' }));
    await fixture.whenStable();
    expect(radio.getAttribute('aria-invalid')).toBe('true');
    component.fields().reset();
    await fixture.whenStable();
    expect(group.getAttribute('aria-invalid')).toBeNull();
    expect(radio.getAttribute('aria-invalid')).toBeNull();
  });

  it('includes forceInvalid on the real Helm controls', async () => {
    const { fixture, host, component } = await setup();
    component.model.update((value) => ({ ...value, name: 'Valid' }));
    component.forceInvalid.set(true);
    await fixture.whenStable();
    const controls = host.querySelectorAll<HTMLElement>(
      '[data-testid="select"] button[role="combobox"], [data-testid="combobox"] input, [role="checkbox"], [role="switch"], textarea',
    );
    expect(controls.length).toBe(5);
    for (const control of controls) expect(control.getAttribute('aria-invalid')).toBe('true');
    component.forceInvalid.set(false);
    await fixture.whenStable();
    for (const control of controls) expect(control.getAttribute('aria-invalid')).toBeNull();
  });

  it('keeps official explicit aria-invalid overrides ahead of automatic state', async () => {
    const { fixture, host, component } = await setup();
    const controls = host.querySelectorAll<HTMLElement>(
      '[data-testid="select"] button[role="combobox"], [data-testid="combobox"] input',
    );
    component.manualInvalid.set(true);
    await fixture.whenStable();
    for (const control of controls) expect(control.getAttribute('aria-invalid')).toBe('true');
    component.forceInvalid.set(true);
    component.manualInvalid.set(false);
    await fixture.whenStable();
    for (const control of controls) expect(control.getAttribute('aria-invalid')).toBeNull();
    component.manualInvalid.set(undefined);
    await fixture.whenStable();
    for (const control of controls) expect(control.getAttribute('aria-invalid')).toBe('true');
  });

  it('shows server field errors immediately and clears them when corrected', async () => {
    const { fixture, input, errors, component } = await setup();
    component.model.update((value) => ({ ...value, name: 'Valid' }));
    component.action.mockRejectedValueOnce(rejectedField('name'));
    await renderSubmission(fixture, () => component.save());
    expect(errors()).toHaveLength(1);
    expect(errors()[0].textContent).toContain('Server rejected this field');
    input.value = 'Corrected';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await fixture.whenStable();
    expect(errors()).toHaveLength(0);
  });
});
