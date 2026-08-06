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

  const listbox = () => document.querySelector<HTMLElement>('[role="listbox"]');
  const search = () => document.querySelector<HTMLInputElement>('input[role="combobox"]');
  const optionEls = () => Array.from(document.querySelectorAll<HTMLElement>('[role="option"]'));
  const optionBy = (label: string) => optionEls().find((o) => o.textContent?.includes(label));

  function press(fixture: ComponentFixture<FacetedFilter>, key: string): void {
    search()!.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
    fixture.detectChanges();
  }

  afterEach(() => {
    document.querySelectorAll('.cdk-overlay-container').forEach((el) => (el.innerHTML = ''));
  });

  it('declares a multi-selectable listbox owned by the search combobox', () => {
    const fixture = createFilter({ multiple: true, values: [] });

    expect(listbox()?.getAttribute('aria-multiselectable')).toBe('true');
    expect(search()?.getAttribute('aria-controls')).toBe(listbox()?.id);
    expect(listbox()?.id).toBeTruthy();

    fixture.componentRef.setInput('multiple', false);
    fixture.detectChanges();
    expect(listbox()?.getAttribute('aria-multiselectable')).toBeNull();
  });

  it('reports business selection through aria-selected in both modes', () => {
    const fixture = createFilter({ multiple: true, values: ['admin'] });
    expect(optionBy('Administrator')?.getAttribute('aria-selected')).toBe('true');
    expect(optionBy('Member')?.getAttribute('aria-selected')).toBe('false');

    fixture.componentRef.setInput('multiple', false);
    fixture.componentRef.setInput('value', 'guest');
    fixture.detectChanges();
    expect(optionBy('Guest')?.getAttribute('aria-selected')).toBe('true');
    expect(optionBy('Administrator')?.getAttribute('aria-selected')).toBe('false');
  });

  it('moves the keyboard active option without changing the selection', () => {
    const fixture = createFilter({ multiple: true, values: ['admin'] });
    const selectedBefore = optionEls().map((o) => o.getAttribute('aria-selected'));

    expect(search()?.getAttribute('aria-activedescendant')).toBe(optionEls()[0].id);
    press(fixture, 'ArrowDown');
    expect(search()?.getAttribute('aria-activedescendant')).toBe(optionEls()[1].id);
    press(fixture, 'End');
    expect(search()?.getAttribute('aria-activedescendant')).toBe(optionEls()[2].id);
    press(fixture, 'ArrowDown'); // 环绕回首项
    expect(search()?.getAttribute('aria-activedescendant')).toBe(optionEls()[0].id);
    press(fixture, 'ArrowUp'); // 环绕到末项
    expect(search()?.getAttribute('aria-activedescendant')).toBe(optionEls()[2].id);

    expect(optionEls().map((o) => o.getAttribute('aria-selected'))).toEqual(selectedBefore);
  });

  it('toggles the active option with Enter and keeps the multi-select panel open', () => {
    const fixture = createFilter({ multiple: true, values: ['admin'] });
    let emitted: string[] | null = null;
    fixture.componentInstance.valuesChange.subscribe((values) => (emitted = values));

    press(fixture, 'ArrowDown');
    press(fixture, 'Enter');

    expect(emitted!).toEqual(['admin', 'member']);
    expect(fixture.componentInstance.state()).toBe('open');
  });

  it('emits the single-select value on click and closes the panel', () => {
    const fixture = createFilter({ value: null });
    let emitted: unknown = 'untouched';
    fixture.componentInstance.valueChange.subscribe((value) => (emitted = value));

    optionBy('Guest')?.click();
    fixture.detectChanges();

    expect(emitted).toBe('guest');
    expect(fixture.componentInstance.state()).toBe('closed');
  });

  it('filters options by the search query and resets the active option', () => {
    const fixture = createFilter({ multiple: true, values: [] });

    press(fixture, 'End');
    expect(search()?.getAttribute('aria-activedescendant')).toBe(optionEls()[2].id);

    const input = search()!;
    input.value = 'mem';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(optionEls().length).toBe(1);
    expect(optionEls()[0].textContent).toContain('Member');
    expect(search()?.getAttribute('aria-activedescendant')).toBe(optionEls()[0].id);
  });

  it('keeps the clear command outside the listbox and free of option semantics', () => {
    createFilter({ multiple: true, values: ['admin'] });

    const clear = Array.from(document.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Clear filter'),
    );
    expect(clear).withContext('clear button should render when a selection exists').toBeDefined();
    expect(clear?.getAttribute('role')).toBeNull();
    expect(listbox()?.contains(clear!)).toBeFalse();
  });

  it('renders options without nested interactive elements', () => {
    createFilter({ multiple: true, values: ['admin'] });

    for (const option of optionEls()) {
      expect(option.querySelector('button, input, [tabindex], [role="checkbox"]'))
        .withContext(`option "${option.textContent?.trim()}" must not nest interactive elements`)
        .toBeNull();
    }
  });

  it('uses a distinct checked background in dark mode', () => {
    document.documentElement.classList.add('dark');
    try {
      createFilter({ multiple: true, values: ['admin'] });
      const graphic = (label: string) =>
        optionBy(label)?.querySelector<HTMLElement>('span[aria-hidden]');
      const checked = getComputedStyle(graphic('Administrator')!).backgroundColor;
      const unchecked = getComputedStyle(graphic('Member')!).backgroundColor;
      expect(checked).not.toBe(unchecked);
    } finally {
      document.documentElement.classList.remove('dark');
    }
  });
});
