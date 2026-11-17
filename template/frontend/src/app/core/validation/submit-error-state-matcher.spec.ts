import { Component, ElementRef, signal, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormField, form, required } from '@angular/forms/signals';
import { provideErrorStateMatcher } from '@spartan-ng/brain/forms';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';

import { injectFormSubmission } from './form-submission';
import { renderSubmission, rejectedField } from './form-submission.testing';
import { SubmitErrorStateMatcher } from './submit-error-state-matcher';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../i18n/transloco.testing';
//#endif

@Component({
  selector: 'app-submission-test',
  imports: [FormField, HlmInput, ...HlmFieldImports],
  template: `
    <form #root novalidate (submit)="$event.preventDefault(); save()">
      <div hlmField>
        <input hlmInput aria-label="Name" [formField]="fields.name" />
        @for (error of fields.name().errors(); track error.kind) {
          <hlm-field-error [validator]="error.kind">{{
            error.message ?? error.kind
          }}</hlm-field-error>
        }
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
  readonly model = signal({ name: '' });
  readonly fields = form(this.model, (path) => required(path.name));
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
    expect(input.getAttribute('data-matches-spartan-invalid')).not.toBe('true');
    await renderSubmission(fixture, () => component.save());
    expect(errors()).toHaveLength(1);
    expect(document.activeElement).toBe(input);
    expect(component.action).not.toHaveBeenCalled();
    input.value = 'Valid';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await fixture.whenStable();
    expect(errors()).toHaveLength(0);
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
    await renderSubmission(fixture, () => component.save());
    expect(errors()).toHaveLength(1);
  });

  it('shows server field errors immediately and clears them when corrected', async () => {
    const { fixture, input, errors, component } = await setup();
    component.model.set({ name: 'Valid' });
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
