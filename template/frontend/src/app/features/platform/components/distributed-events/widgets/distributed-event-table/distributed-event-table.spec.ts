import { signal, Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../core/i18n/transloco.testing';
//#endif
import { AccountPreferencesService } from '../../../../../../core/services/account-preferences-service';
import * as Dto from '../../../../dtos/distributed-event.dto';
import { EventRow, eventRow } from '../../distributed-event-display';
import { DistributedCatalogTable } from '../distributed-catalog-table/distributed-catalog-table';
import { DistributedInboxTable } from '../distributed-inbox-table/distributed-inbox-table';
import { DistributedOutboxTable } from '../distributed-outbox-table/distributed-outbox-table';
import { DistributedRejectionTable } from '../distributed-rejection-table/distributed-rejection-table';
import { DistributedEventTable } from './distributed-event-table';

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

const outbox = (
  id: string,
  status: Dto.DistributedEventOutboxStatus,
  attemptCount = 0,
): Dto.DistributedEventOutboxOutputDto => ({
  id,
  eventId: `event-${id}`,
  eventName: 'order.paid',
  status,
  attemptCount,
  redeliverCount: 0,
  occurredAt: '2026-01-01T00:00:00Z',
  creationTime: '2026-01-01T00:00:01Z',
  lastModificationTime: '2026-01-01T00:00:01Z',
});
const rejection = (
  id: string,
  status: Dto.DistributedEventRejectionStatus,
): Dto.DistributedEventRejectionOutputDto => ({
  id,
  transport: 'RabbitMQ',
  coordinates: {},
  reason: 'invalid-payload',
  error: 'Invalid payload.',
  eventName: 'order.paid',
  eventId: `event-${id}`,
  status,
  redecodeCount: 0,
  receivedAt: '2026-01-01T00:00:01Z',
  lastModificationTime: '2026-01-01T00:00:01Z',
});

describe('distributed event selection and status badges', () => {
  let fixture: ComponentFixture<DistributedEventTable>;

  async function render(table: Type<DistributedEventTable>, rows: EventRow[]) {
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
    fixture.componentRef.setInput('rows', rows);
    fixture.componentRef.setInput('canManage', true);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }
  const checkbox = (host: HTMLElement, id: string) =>
    host.querySelector(`[data-record-id="${id}"] [role=checkbox]`) as HTMLButtonElement | null;
  afterEach(() => fixture?.destroy());

  it('renders a checkbox on every outbox row and explains why only dead letters are enabled', async () => {
    const host = await render(DistributedOutboxTable, [
      eventRow(outbox('1', 'pending')),
      eventRow(outbox('2', 'sent')),
      eventRow(outbox('3', 'deadLettered')),
    ]);
    for (const id of ['1', '2', '3']) expect(checkbox(host, id)).not.toBeNull();
    expect(checkbox(host, '3')!.disabled).toBe(false);
    for (const id of ['1', '2']) {
      const box = checkbox(host, id)!;
      expect(box.disabled).toBe(true);
      const reason = host.querySelector(`#${box.getAttribute('aria-describedby')}`);
      //#if (IncludeLocalization)
      expect(reason?.textContent?.trim()).toBe('distributedEvents.selectDisabled.outbox');
      //#else
      expect(reason?.textContent?.trim()).toBe('Only dead letters can be redelivered');
      //#endif
      expect(box.closest('[data-select-cell]')?.getAttribute('tabindex')).toBe('0');
    }
  });

  it('selects only the eligible records on this page from the header checkbox', async () => {
    const host = await render(DistributedOutboxTable, [
      eventRow(outbox('1', 'sent')),
      eventRow(outbox('2', 'deadLettered')),
      eventRow(outbox('3', 'deadLettered')),
    ]);
    const selection = vi.fn();
    fixture.componentInstance.selectionChange.subscribe(selection);
    (host.querySelector('thead [role=checkbox]') as HTMLButtonElement).click();
    expect(selection).toHaveBeenCalledWith(['2', '3']);
  });

  it('lets unresolved rejections be selected when either re-decode or discard applies', async () => {
    const host = await render(DistributedRejectionTable, [
      eventRow(rejection('1', 'pending')),
      eventRow(rejection('2', 'redecodePending')),
      eventRow(rejection('3', 'discarded')),
      eventRow(rejection('4', 'redecoded')),
    ]);
    expect(checkbox(host, '1')!.disabled).toBe(false);
    expect(checkbox(host, '2')!.disabled).toBe(false);
    expect(checkbox(host, '3')!.disabled).toBe(true);
    expect(checkbox(host, '4')!.disabled).toBe(true);
  });

  it('renders no selection column without manage permission', async () => {
    const host = await render(DistributedOutboxTable, [eventRow(outbox('1', 'deadLettered'))]);
    fixture.componentRef.setInput('canManage', false);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(host.querySelector('[role=checkbox]')).toBeNull();
  });

  it('uses view-specific status labels and variants with a status dot', async () => {
    const host = await render(DistributedOutboxTable, [
      eventRow(outbox('1', 'pending')),
      eventRow(outbox('2', 'pending', 3)),
      eventRow(outbox('3', 'deadLettered')),
      eventRow(outbox('4', 'sent')),
    ]);
    const badge = (id: string) =>
      host.querySelector(`[data-record-id="${id}"] [hlmBadge]`) as HTMLElement;
    const variants = ['1', '2', '3', '4'].map((id) => badge(id).getAttribute('data-variant'));
    expect(variants).toEqual(['secondary', 'warning', 'destructive', 'success']);
    expect(badge('1').className).toContain("before:content-['']");
    //#if (IncludeLocalization)
    expect(badge('1').textContent?.trim()).toBe('distributedEvents.state.pendingSend');
    //#else
    expect(badge('1').textContent?.trim()).toBe('Pending send');
    //#endif
  });

  it('collects row actions into a menu below sm that opens details and runs the allowed recovery', async () => {
    const host = await render(DistributedOutboxTable, [
      eventRow(outbox('1', 'deadLettered')),
      eventRow(outbox('2', 'sent')),
    ]);
    const open = vi.fn();
    const recover = vi.fn();
    fixture.componentInstance.open.subscribe(open);
    fixture.componentInstance.recover.subscribe(recover);
    const row = (id: string) => host.querySelector(`[data-record-id="${id}"]`)!;
    // 平铺按钮只在 ≥sm 显示，触发器只在 <sm 显示
    for (const button of Array.from(row('1').querySelectorAll('td:last-child button[hlmBtn]')))
      expect(
        button.getAttribute('data-testid') === 'event-row-menu'
          ? button.classList.contains('sm:hidden')
          : button.classList.contains('hidden') && button.classList.contains('sm:inline-flex'),
      ).toBe(true);
    const menuItems = async (id: string) => {
      (row(id).querySelector('[data-testid=event-row-menu]') as HTMLButtonElement).click();
      fixture.detectChanges();
      await fixture.whenStable();
      return Array.from(
        document.querySelectorAll('hlm-dropdown-menu [hlmDropdownMenuItem]'),
      ) as HTMLButtonElement[];
    };
    const items = await menuItems('1');
    expect(items).toHaveLength(2);
    items[1].click();
    expect(recover).toHaveBeenCalledWith(expect.objectContaining({ id: '1' }));
    fixture.detectChanges();
    await fixture.whenStable();
    const sentItems = await menuItems('2');
    // 已发送的记录不能重投，菜单里只有详情
    expect(sentItems).toHaveLength(1);
    sentItems[0].click();
    expect(open).toHaveBeenCalledWith('2');
  });

  it('marks pending and queued rejections as needing attention', async () => {
    const host = await render(DistributedRejectionTable, [
      eventRow(rejection('1', 'pending')),
      eventRow(rejection('2', 'redecodePending')),
      eventRow(rejection('3', 'discarded')),
      eventRow(rejection('4', 'redecoded')),
    ]);
    const variants = ['1', '2', '3', '4'].map((id) =>
      host.querySelector(`[data-record-id="${id}"] [hlmBadge]`)?.getAttribute('data-variant'),
    );
    expect(variants).toEqual(['warning', 'warning', 'secondary', 'success']);
  });
});
