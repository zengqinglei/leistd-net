import { signal, Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DistributedEventTable } from './distributed-event-table';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../core/i18n/transloco.testing';
//#endif
import { AccountPreferencesService } from '../../../../../../core/services/account-preferences-service';
import { EventRow } from '../../distributed-event-display';
import { DistributedCatalogTable } from '../distributed-catalog-table/distributed-catalog-table';
import { DistributedInboxTable } from '../distributed-inbox-table/distributed-inbox-table';
import { DistributedOutboxTable } from '../distributed-outbox-table/distributed-outbox-table';
import { DistributedRejectionTable } from '../distributed-rejection-table/distributed-rejection-table';

const TABLES: { name: string; component: Type<DistributedEventTable> }[] = [
  { name: 'Outbox', component: DistributedOutboxTable },
  { name: 'Inbox', component: DistributedInboxTable },
  { name: 'Rejections', component: DistributedRejectionTable },
  { name: 'Catalog', component: DistributedCatalogTable },
];
const ROW: EventRow = {
  id: '1',
  eventName: 'order.paid',
  eventId: '00000000-0000-4000-8000-000000000101',
  status: 'sent',
  tenant: 'host',
  orderingKey: 'order-1',
  consumer: 'Orders.Projection',
  attempts: 1,
  error: '—',
  next: null,
  time: '2026-01-01T00:00:00Z',
  catalog: {
    eventName: 'order.paid',
    outgoing: true,
    incoming: true,
    consumers: ['Orders.Projection'],
    pendingOutbox: 1,
    pendingInbox: 1,
    deadLettered: 0,
    failedInbox: 0,
  },
};

describe.each(TABLES)('$name loading and active records', ({ component: table }) => {
  let fixture: ComponentFixture<DistributedEventTable>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [table],
      providers: [
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(),
        //#endif
        {
          provide: AccountPreferencesService,
          useValue: { timeZone: signal('UTC'), displayLocale: signal('en') },
        },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(table);
  });
  afterEach(() => fixture?.destroy());

  it('keeps the headers and renders five loading rows before data arrives', async () => {
    fixture.componentRef.setInput('loading', true);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('thead th')).not.toBeNull();
    expect(host.querySelectorAll('tbody tr[aria-hidden=true]')).toHaveLength(5);
    expect(host.querySelector('[aria-busy=true]')).not.toBeNull();

    fixture.componentRef.setInput('rows', [ROW]);
    fixture.componentRef.setInput('loading', false);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(host.querySelectorAll('tbody tr[aria-hidden=true]')).toHaveLength(0);
    expect(host.querySelector('[data-record-id="1"]')?.textContent).toContain(ROW.eventName);
  });

  it('preserves rows during refresh and exposes the selected or opened record', async () => {
    fixture.componentRef.setInput('rows', [ROW]);
    fixture.detectChanges();
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    const row = host.querySelector('[data-record-id="1"]');
    expect(row?.getAttribute('aria-selected')).toBe('false');

    fixture.componentRef.setInput('loading', true);
    fixture.componentRef.setInput('selected', ['1']);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(host.querySelector('[data-record-id="1"]')).toBe(row);
    expect(host.querySelector('tbody')?.classList.contains('opacity-50')).toBe(true);
    expect(row?.getAttribute('aria-selected')).toBe('true');

    fixture.componentRef.setInput('selected', []);
    fixture.componentRef.setInput('recordId', '1');
    fixture.detectChanges();
    expect(row?.getAttribute('aria-selected')).toBe('true');
  });
});
