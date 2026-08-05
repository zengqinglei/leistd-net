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

  it('exposes the business selection via aria-checked, independent of keyboard focus', () => {
    const fixture = createFilter({ multiple: true, values: ['admin'] });

    const items = commandItems();
    const byLabel = (label: string) => items.find((item) => item.textContent?.includes(label));
    expect(byLabel('Administrator')?.getAttribute('aria-checked')).toBe('true');
    expect(byLabel('Member')?.getAttribute('aria-checked')).toBe('false');

    byLabel('Member')?.click();
    fixture.componentRef.setInput('values', ['admin', 'member']);
    fixture.detectChanges();

    expect(byLabel('Member')?.getAttribute('aria-checked')).toBe('true');
  });

  it('uses a distinct checked background in dark mode', () => {
    document.documentElement.classList.add('dark');
    try {
      createFilter({ multiple: true, values: ['admin'] });
      const items = commandItems();
      const graphic = (label: string) =>
        items
          .find((item) => item.textContent?.includes(label))
          ?.querySelector<HTMLElement>('span[aria-hidden]');

      const checkedBg = getComputedStyle(graphic('Administrator')!).backgroundColor;
      const uncheckedBg = getComputedStyle(graphic('Member')!).backgroundColor;
      expect(checkedBg).not.toBe(uncheckedBg);
    } finally {
      document.documentElement.classList.remove('dark');
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
