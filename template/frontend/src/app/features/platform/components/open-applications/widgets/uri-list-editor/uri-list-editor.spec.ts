import { ComponentFixture, TestBed } from '@angular/core/testing';

import { UriListEditor } from './uri-list-editor';
import { renderSubmission } from '../../../../../../core/validation/form-submission.testing';

describe('UriListEditor', () => {
  function createEditor(uris: string[] = []): ComponentFixture<UriListEditor> {
    const fixture = TestBed.createComponent(UriListEditor);
    fixture.componentRef.setInput('inputId', 'redirect-uri');
    fixture.componentRef.setInput('label', 'Redirect URI');
    fixture.componentRef.setInput('addLabel', 'Add');
    fixture.componentRef.setInput('invalidText', 'Invalid URI');
    fixture.componentRef.setInput('emptyText', 'No URIs');
    fixture.componentRef.setInput('uris', uris);
    fixture.detectChanges();
    return fixture;
  }

  function enterValue(fixture: ComponentFixture<UriListEditor>, value: string): void {
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  beforeEach(() => TestBed.configureTestingModule({ imports: [UriListEditor] }));

  it('emits the exact URI when a valid value is added', () => {
    const fixture = createEditor();
    const emitted: string[] = [];
    fixture.componentInstance.add.subscribe((uri) => emitted.push(uri));

    enterValue(fixture, 'https://example.com/callback');
    (fixture.nativeElement.querySelector('button') as HTMLButtonElement).click();

    expect(emitted).toEqual(['https://example.com/callback']);
  });

  it('keeps invalid submission available and shows the field error', () => {
    const fixture = createEditor();
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;

    expect(button.disabled).toBe(false);

    enterValue(fixture, 'not-a-uri');
    expect(button.disabled).toBe(false);

    enterValue(fixture, 'https://example.com/callback#fragment');
    expect(button.disabled).toBe(false);

    enterValue(fixture, 'https://example.com/callback');
    expect(button.disabled).toBe(false);
  });

  it('emits the removed URI when its chip button is clicked', () => {
    const fixture = createEditor(['https://one.example/callback', 'https://two.example/callback']);
    let removed = '';
    fixture.componentInstance.remove.subscribe((uri) => (removed = uri));

    const removeButton = fixture.nativeElement.querySelector(
      'button[aria-label="Remove: https://one.example/callback"]',
    ) as HTMLButtonElement;
    removeButton.click();

    expect(removed).toBe('https://one.example/callback');
  });

  it('keeps the remove action focus ring inside every clipping ancestor', async () => {
    const fixture = createEditor(['https://one.example/callback']);
    await fixture.whenStable();
    const button = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      'button[aria-label="Remove: https://one.example/callback"]',
    )!;
    button.focus();
    const ring = button.getBoundingClientRect();
    for (let parent = button.parentElement; parent; parent = parent.parentElement) {
      const style = getComputedStyle(parent);
      const bounds = parent.getBoundingClientRect();
      if (/(auto|scroll|hidden|clip)/.test(style.overflowX)) {
        expect(ring.left - 2).toBeGreaterThanOrEqual(bounds.left + parent.clientLeft);
        expect(ring.right + 2).toBeLessThanOrEqual(
          bounds.left + parent.clientLeft + parent.clientWidth,
        );
      }
      if (/(auto|scroll|hidden|clip)/.test(style.overflowY)) {
        expect(ring.top - 2).toBeGreaterThanOrEqual(bounds.top + parent.clientTop);
        expect(ring.bottom + 2).toBeLessThanOrEqual(
          bounds.top + parent.clientTop + parent.clientHeight,
        );
      }
    }
  });

  it('review R1 focuses the URI input and shows invalid submissions', async () => {
    const fixture = createEditor();
    await renderSubmission(fixture, () => fixture.componentInstance['onAdd']());
    const input = (fixture.nativeElement as HTMLElement).querySelector('input')!;
    expect(document.activeElement).toBe(input);
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(
      (fixture.nativeElement as HTMLElement)
        .querySelector('hlm-field-error')!
        .hasAttribute('hidden'),
    ).toBe(false);
  });
});
