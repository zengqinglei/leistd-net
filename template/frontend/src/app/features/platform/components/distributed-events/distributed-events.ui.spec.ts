import { PlatformLocation } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { MockPlatformLocation } from '@angular/common/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter, Router } from '@angular/router';
import { of, Subject } from 'rxjs';

import { DistributedEvents } from './distributed-events';
import { DistributedCatalogTable } from './widgets/distributed-catalog-table/distributed-catalog-table';
import { DistributedEventRecoveryConfirmService } from './widgets/distributed-event-recovery-confirm/distributed-event-recovery-confirm';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../core/i18n/transloco.testing';
//#endif
import { AccountPreferencesService } from '../../../../core/services/account-preferences-service';
import { AuthService } from '../../../../core/services/auth-service';
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { PERMISSIONS } from '../../../../shared/constants/permission.constants';
import * as Dto from '../../dtos/distributed-event.dto';
import { DistributedEventService } from '../../services/distributed-event-service';
//#if (LocalIdentity && IncludeMultiTenancy)
import { TenantService } from '../../services/tenant-service';
//#endif

const TENANT_ID = '00000000-0000-4000-8000-000000000011';
const OUTBOX: Dto.DistributedEventOutboxOutputDto = {
  id: '1',
  eventId: '00000000-0000-4000-8000-000000000101',
  eventName: 'order.paid',
  tenantId: TENANT_ID,
  status: 'deadLettered',
  attemptCount: 1,
  redeliverCount: 0,
  creationTime: '2026-01-01T00:00:01Z',
  occurredAt: '2026-01-01T00:00:00Z',
  lastModificationTime: '2026-01-01T00:00:01Z',
};
const INBOX: Dto.DistributedEventInboxOutputDto = {
  id: '2',
  eventId: OUTBOX.eventId,
  eventName: OUTBOX.eventName,
  tenantId: TENANT_ID,
  status: 'processed',
  consumer: 'Orders.Projection',
  attemptCount: 1,
  duplicateCount: 0,
  replayCount: 0,
  occurredAt: OUTBOX.occurredAt,
  receivedAt: OUTBOX.creationTime,
  lastModificationTime: OUTBOX.lastModificationTime,
};
const REJECTION: Dto.DistributedEventRejectionOutputDto = {
  id: '3',
  eventName: OUTBOX.eventName,
  eventId: OUTBOX.eventId,
  status: 'pending',
  transport: 'RabbitMQ',
  coordinates: {},
  reason: 'invalid-payload',
  error: 'Invalid payload.',
  redecodeCount: 0,
  receivedAt: OUTBOX.creationTime,
  lastModificationTime: OUTBOX.lastModificationTime,
};
const STATUS: Dto.DistributedEventStatusOutputDto = {
  pendingOutbox: 1,
  oldestPendingOutboxAgeSeconds: 90116,
  deadLettered: 2,
  pendingInbox: 0,
  failedInbox: 1,
  pendingRejections: 1,
  sentLast24Hours: 3,
  processedLast24Hours: 2,
  transportStatus: 'healthy',
  testEventEnabled: true,
  payloadEnabled: true,
};
const catalogEntry = (
  eventName: string,
  incoming: boolean,
): Dto.DistributedEventCatalogOutputDto => ({
  eventName,
  incoming,
  outgoing: true,
  consumers: incoming ? ['Orders.Projection'] : [],
  pendingOutbox: 0,
  pendingInbox: 0,
  deadLettered: 0,
  failedInbox: 0,
});

/** UI R1 的行为回归：同一用例也在修复前模板上实际生成并运行。 */
describe('DistributedEvents UI R1', () => {
  let fixture: ComponentFixture<DistributedEvents>;
  let component: DistributedEvents;
  //#if (IncludeLocalization)
  const translations = {
    distributedEvents: {
      hostDatabase: 'Host / default database',
      oldest: 'Oldest {{age}} · Pending {{count}}',
      view: { outbox: 'Outbox', inbox: 'Inbox', rejections: 'Rejections', catalog: 'Catalog' },
      state: { deadLettered: 'Dead letter', processed: 'Processed', pending: 'Pending' },
      action: {
        redeliver: 'Redeliver',
        replay: 'Replay',
        redecode: 'Re-decode',
        discard: 'Discard',
      },
      reason: { invalidPayload: 'Invalid payload' },
      field: { direction: 'Direction', status: 'Status' },
      column: {
        event: 'Event',
        tenant: 'Tenant',
        orderingKey: 'Key',
        status: 'Status',
        attempts: 'Try',
        error: 'Error',
        next: 'Next',
        time: 'Time',
      },
      confirmation: {
        filterQuestion: {
          redeliver: 'Redeliver {{count}} dead letters matching the current filter?',
        },
        button: { redeliver: 'Redeliver {{count}} records' },
        scope: 'Scope',
        cutoff: 'Loaded through',
        criteria: 'Filters',
      },
      confirm: 'Recover {{count}} records?',
    },
  };
  //#endif

  async function setup(query: Record<string, string> = {}, _tenantPermission = false) {
    await TestBed.configureTestingModule({
      imports: [DistributedEvents],
      providers: [
        provideRouter([{ path: 'platform/distributed-events', children: [] }]),
        { provide: PlatformLocation, useClass: MockPlatformLocation },
        provideHttpClient(),
        provideHttpClientTesting(),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en'], { getTranslation: () => of(translations) }),
        //#endif
        { provide: AuthService, useValue: { currentUser: signal({ tenantId: null }) } },
        {
          provide: AccountPreferencesService,
          useValue: { timeZone: signal('UTC'), displayLocale: signal('en') },
        },
        {
          provide: DistributedEventService,
          useValue: {
            getStatus: () => of(STATUS),
            getDatabases: () => of([{ key: 'host', isHost: true, tenantIds: [] }]),
            getCatalog: () =>
              of([catalogEntry('order.paid', true), catalogEntry('invoice.issued', false)]),
            getOutboxList: () => of({ items: [OUTBOX], totalCount: 2 }),
            getInboxList: () => of({ items: [INBOX], totalCount: 1 }),
            getRejectionList: () => of({ items: [REJECTION], totalCount: 1 }),
            getOutbox: () => of({ record: OUTBOX, headers: {} }),
            getInbox: () => of({ record: INBOX, headers: {}, coordinates: {} }),
            getRejection: () => of({ record: REJECTION, headers: {}, coordinates: {} }),
            redeliver: () => of({ affectedCount: 2, deferredCount: 0, skipped: [] }),
          },
        },
        //#if (LocalIdentity && IncludeMultiTenancy)
        {
          provide: TenantService,
          useValue: {
            getTenant: () => of({ id: TENANT_ID, name: 'portal-east', displayName: 'Portal East' }),
          },
        },
        //#endif
      ],
    }).compileComponents();
    await TestBed.inject(Router).navigate(['/platform/distributed-events'], { queryParams: query });
    const permissions: string[] = [
      PERMISSIONS.distributedEvents.default,
      PERMISSIONS.distributedEvents.manage,
    ];
    //#if (LocalIdentity && IncludeMultiTenancy)
    if (_tenantPermission) permissions.push(PERMISSIONS.tenants.default);
    //#endif
    TestBed.inject(AuthorizationService).setPermissions({
      permissions,
      isSuperAdmin: false,
      versionToken: 'ui-r1',
    });
    fixture = TestBed.createComponent(DistributedEvents);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }
  afterEach(() => fixture?.destroy());

  it('formats a day-long pending age without displaying raw seconds', async () => {
    await setup();
    expect(fixture.nativeElement.textContent).toContain('1 day');
    expect(fixture.nativeElement.textContent).not.toContain('90,116');
  });
  it('renders reported status timestamps and clears them for unknown transport', async () => {
    await setup();
    component.status.set({
      ...STATUS,
      transportStatusChangedAt: '2026-01-01T10:20:30Z',
      transportReportedAt: '2026-01-01T11:22:33Z',
    });
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelector('[data-testid=event-transport-changed]').textContent,
    ).toContain('10:20:30');
    expect(
      fixture.nativeElement.querySelector('[data-testid=event-transport-reported]').textContent,
    ).toContain('11:22:33');
    component.status.set({
      ...STATUS,
      transportStatus: 'unknown',
      transportStatusChangedAt: null,
      transportReportedAt: null,
    });
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelector('[data-testid=event-transport-changed]').textContent,
    ).toContain('—');
    expect(
      fixture.nativeElement.querySelector('[data-testid=event-transport-reported]').textContent,
    ).toContain('—');
  });
  it('hides the payload section when the endpoint reports it disabled while keeping recovery available', async () => {
    await setup({ view: 'outbox', record: '1' });
    expect(document.querySelector('[data-testid=event-payload]')).not.toBeNull();
    component.status.set({ ...STATUS, payloadEnabled: false });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(document.querySelector('[data-testid=event-payload]')).toBeNull();
    expect(document.querySelector('[hlmSheetHeader]')?.textContent).toContain('Redeliver');
  });
  it('shows attention counts on the three record tabs', async () => {
    await setup();
    const tabs = Array.from(fixture.nativeElement.querySelectorAll('[role=tab]')) as HTMLElement[];
    expect(tabs[0].textContent).toMatch(/2/);
    expect(tabs[1].textContent).toMatch(/1/);
    expect(tabs[2].textContent).toMatch(/1/);
  });
  it.each([0, 2])('highlights abnormal snapshot counts only when positive (%i)', async (count) => {
    await setup();
    component.status.set({
      ...STATUS,
      deadLettered: count,
      failedInbox: count,
      pendingRejections: count,
    });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const values = Array.from(
      fixture.nativeElement.querySelectorAll('app-distributed-event-statistics .tabular-nums'),
    ) as HTMLElement[];
    expect(values).toHaveLength(4);
    expect(values[0].classList.contains('text-destructive')).toBe(false);
    for (const value of values.slice(1)) {
      expect(value.classList.contains('text-destructive')).toBe(count > 0);
    }
  });
  it('localizes the detail state as a badge', async () => {
    await setup({ view: 'inbox', record: '2' });
    const badge = document.querySelector('[data-testid=event-detail-status]');
    expect(badge).not.toBeNull();
    expect(badge?.textContent?.trim()).toBe('Processed');
  });
  it('places the record-specific primary action in the sheet header', async () => {
    await setup({ view: 'inbox', record: '2' });
    expect(document.querySelector('[hlmSheetHeader]')?.textContent).toContain('Replay');
  });
  //#if (LocalIdentity && IncludeMultiTenancy)
  it('resolves a permitted tenant display name instead of showing its ID in the table', async () => {
    await setup({}, true);
    expect(fixture.nativeElement.querySelector('tbody')?.textContent).toContain('Portal East');
  });
  //#endif
  it('renders zero catalog counts as plain text and hides the inapplicable direction', async () => {
    await setup({ view: 'catalog' });
    const table = fixture.debugElement.query(By.directive(DistributedCatalogTable))
      .componentInstance as DistributedCatalogTable;
    table.table.setOptions((options) => ({
      ...options,
      state: { ...options.state, columnVisibility: {} },
    }));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const rows = Array.from(fixture.nativeElement.querySelectorAll('tbody tr')) as HTMLElement[];
    const invoice = rows.find((row) => row.textContent?.includes('invoice.issued'))!;
    expect(invoice.textContent).toContain('Outbox 0');
    expect(invoice.textContent).not.toContain('Inbox 0');
    expect(invoice.querySelectorAll('button[variant=link]')).toHaveLength(0);
  });
  it('uses source coordinates to locate an undecodable rejection', async () => {
    await setup({ view: 'rejections' });
    const row = component.rows()[0];
    const { eventRow } = await import('./distributed-event-display');
    const fallback = eventRow({
      ...REJECTION,
      eventName: null,
      eventId: null,
      coordinates: { queue: 'customer-events', messageId: 'external-104' },
    });
    expect(row).toBeDefined();
    expect(fallback.eventName).toBe('customer-events');
    expect(fallback.eventId).toBe('external-104');
  });
  it('applies the catalog direction filter restored from the URL', async () => {
    await setup({ view: 'catalog', direction: 'incoming' });
    expect(component.totalCount()).toBe(1);
  });
  it('localizes known rejection reasons in the list', async () => {
    await setup({ view: 'rejections' });
    expect(fixture.nativeElement.querySelector('tbody')?.textContent).toContain('Invalid payload');
  });

  it('keeps rejection confirmation scoped to the host database when another database is selected', async () => {
    await setup({ view: 'rejections', database: TENANT_ID });
    const confirm = TestBed.inject(DistributedEventRecoveryConfirmService);
    const open = vi.spyOn(confirm, 'open').mockResolvedValue(false);
    await component.recover('single', REJECTION);
    expect(open.mock.calls[0][0].scope).toBe('Host / default database');
  });

  it('asks a count-specific filter question and confirms the same count', async () => {
    await setup({ status: 'deadLettered' });
    void component.recover('filter');
    fixture.detectChanges();
    await fixture.whenStable();
    const dialog = document.querySelector('[role=alertdialog]');
    expect(dialog?.textContent).toContain('Redeliver 2 dead letters matching the current filter?');
    expect(dialog?.textContent).toContain('Redeliver 2 records');
  });

  it('keeps the existing table visible while refreshing the same view', async () => {
    await setup();
    const result = new Subject<{
      items: Dto.DistributedEventOutboxOutputDto[];
      totalCount: number;
    }>();
    vi.spyOn(TestBed.inject(DistributedEventService), 'getOutboxList').mockReturnValueOnce(result);
    const row = fixture.nativeElement.querySelector('[data-record-id="1"]');
    component.reload();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-record-id="1"]')).toBe(row);
    expect(fixture.nativeElement.querySelector('[aria-busy=true]')).not.toBeNull();
    result.next({ items: [OUTBOX], totalCount: 1 });
    result.complete();
    await fixture.whenStable();
  });

  it('uses loading rows instead of records from the previous view when switching tabs', async () => {
    await setup();
    const result = new Subject<{
      items: Dto.DistributedEventInboxOutputDto[];
      totalCount: number;
    }>();
    vi.spyOn(TestBed.inject(DistributedEventService), 'getInboxList').mockReturnValueOnce(result);
    await TestBed.inject(Router).navigate(['/platform/distributed-events'], {
      queryParams: { view: 'inbox' },
    });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-record-id="1"]')).toBeNull();
    expect(fixture.nativeElement.querySelectorAll('tbody tr[aria-hidden=true]')).toHaveLength(5);
    result.next({ items: [INBOX], totalCount: 1 });
    result.complete();
    await fixture.whenStable();
  });
});
