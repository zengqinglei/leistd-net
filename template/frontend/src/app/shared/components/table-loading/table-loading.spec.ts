import { NgTemplateOutlet } from '@angular/common';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
//#if (IncludeLocalization)
import { TranslocoTestingModule } from '@jsverse/transloco';
//#endif

import { TableLoading, TableLoadingBody } from './table-loading';

@Component({
  imports: [NgTemplateOutlet, TableLoading, TableLoadingBody],
  template: `
    <app-table-loading
      #state="appTableLoading"
      [loading]="loading()"
      [hasRows]="hasRows()"
      [columns]="columns()"
      [rowHeight]="53"
    >
      <div data-testid="region" [attr.aria-busy]="state.loading()">
        <table>
          <tbody appTableLoadingBody>
            @if (state.initialLoading()) {
              <ng-container [ngTemplateOutlet]="state.skeleton() ?? null" />
            } @else if (hasRows()) {
              <tr>
                <td><button>Existing row</button></td>
              </tr>
            } @else {
              <tr>
                <td>No results</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </app-table-loading>
    <button data-testid="outside">Outside</button>
  `,
})
class Host {
  readonly loading = signal(true);
  readonly hasRows = signal(false);
  readonly columns = signal(3);
}

describe('TableLoading', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      // prettier-ignore
      imports: [
        //#if (IncludeLocalization)
        TranslocoTestingModule.forRoot({
          langs: { en: {} },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en', missingHandler: { logMissingKey: false } },
        }),
        //#endif
      ],
    }),
  );

  it('renders five skeleton rows with one cell per visible column only on the initial load', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelectorAll('tbody tr')).toHaveLength(5);
    expect(host.querySelectorAll('tbody td')).toHaveLength(15);
    expect(host.querySelector('tbody tr')?.getBoundingClientRect().height).toBe(53);
    fixture.componentInstance.columns.set(2);
    await fixture.whenStable();
    expect(host.querySelectorAll('tbody td')).toHaveLength(10);
    fixture.componentInstance.loading.set(false);
    await fixture.whenStable();
    expect(host.querySelector('tbody')?.textContent).toContain('No results');
    expect(host.querySelector('[data-testid=region]')?.getAttribute('aria-busy')).toBe('false');
  });

  it('preserves row identity and dimensions, preserves focused actions and announces loading outside the busy region', async () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.loading.set(false);
    fixture.componentInstance.hasRows.set(true);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    const row = host.querySelector('tbody tr')!;
    const before = row.getBoundingClientRect().toJSON();
    const action = row.querySelector<HTMLButtonElement>('button')!;
    action.focus();
    expect(document.activeElement).toBe(action);
    fixture.componentInstance.loading.set(true);
    await fixture.whenStable();
    expect(host.querySelector('tbody tr')).toBe(row);
    expect(document.activeElement).toBe(action);
    expect(row.getBoundingClientRect().toJSON()).toEqual(before);
    expect(host.querySelector('tbody')?.hasAttribute('inert')).toBe(false);
    expect(host.querySelector('tbody')?.classList.contains('opacity-50')).toBe(true);
    expect(host.querySelector('[data-testid=region]')?.getAttribute('aria-busy')).toBe('true');
    const status = host.querySelector('[role="status"]')!;
    expect(status.closest('[aria-busy]')).toBeNull();
    const outside = host.querySelector<HTMLButtonElement>('[data-testid="outside"]')!;
    outside.focus();
    action.focus();
    expect(document.activeElement).toBe(action);
    //#if (IncludeLocalization)
    expect(status.textContent?.trim()).toBe('app.startup.loading');
    //#else
    expect(status.textContent?.trim()).toBe('Loading...');
    //#endif
    fixture.componentInstance.loading.set(false);
    await fixture.whenStable();
    expect(host.querySelector('tbody tr')).toBe(row);
    expect(host.querySelector('tbody')?.hasAttribute('inert')).toBe(false);
    expect(status.textContent?.trim()).toBe('');
    action.focus();
    expect(document.activeElement).toBe(action);
  });
});
