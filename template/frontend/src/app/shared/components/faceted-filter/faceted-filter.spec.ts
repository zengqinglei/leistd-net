import { ComponentFixture, TestBed } from '@angular/core/testing';

import { FacetedFilter, FacetedFilterOption } from './faceted-filter';

describe('FacetedFilter', () => {
  const OPTIONS: FacetedFilterOption[] = [
    { value: 'admin', label: 'Administrator' },
    { value: 'member', label: 'Member' },
    { value: 'guest', label: 'Guest' },
  ];

  beforeEach(() => TestBed.configureTestingModule({ imports: [FacetedFilter] }));

  function createFilter(inputs: Record<string, unknown>): ComponentFixture<FacetedFilter> {
    const fixture = TestBed.createComponent(FacetedFilter);
    fixture.componentRef.setInput('label', 'Role');
    fixture.componentRef.setInput('options', OPTIONS);
    for (const [key, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(key, value);
    }
    fixture.detectChanges();
    fixture.componentInstance.state.set('open');
    fixture.detectChanges();
    return fixture;
  }

  function commandItems(): HTMLButtonElement[] {
    return Array.from(document.querySelectorAll<HTMLButtonElement>('[hlm-command-item]')).filter(
      (item) => item.textContent !== null,
    );
  }

  afterEach(() => {
    document.querySelectorAll('.cdk-overlay-container').forEach((el) => (el.innerHTML = ''));
  });

  it('renders multi-select options without nested interactive elements', () => {
    createFilter({ multiple: true, values: ['admin'] });

    const items = commandItems();
    expect(items.length).toBeGreaterThanOrEqual(OPTIONS.length);
    for (const item of items) {
      expect(item.querySelector('button, [role="checkbox"], input, [tabindex]'))
        .withContext(
          `command item "${item.textContent?.trim()}" must be the only focusable element`,
        )
        .toBeNull();
    }
  });

  it('toggles a multi-select value and keeps the panel open', () => {
    const fixture = createFilter({ multiple: true, values: ['admin'] });
    let emitted: string[] | null = null;
    fixture.componentInstance.valuesChange.subscribe((values) => (emitted = values));

    const member = commandItems().find((item) => item.textContent?.includes('Member'));
    expect(member).toBeDefined();
    member?.click();
    fixture.detectChanges();

    expect(emitted!).toEqual(['admin', 'member']);
    expect(fixture.componentInstance.state()).toBe('open');
  });

  it('emits the single-select value and closes the panel', () => {
    const fixture = createFilter({ value: null });
    let emitted: unknown = 'untouched';
    fixture.componentInstance.valueChange.subscribe((value) => (emitted = value));

    const guest = commandItems().find((item) => item.textContent?.includes('Guest'));
    guest?.click();
    fixture.detectChanges();

    expect(emitted).toBe('guest');
    expect(fixture.componentInstance.state()).toBe('closed');
  });
});
