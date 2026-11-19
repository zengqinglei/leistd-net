import { PlatformLocation } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { MockPlatformLocation } from '@angular/common/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { toast } from '@spartan-ng/brain/sonner';
import { of, Subject, throwError } from 'rxjs';

import { DistributedEvents } from './distributed-events';
import { DistributedEventRecordSheet } from './widgets/distributed-event-record-sheet/distributed-event-record-sheet';
import {
  DistributedEventRecoveryConfirmService,
  DistributedEventRecoveryOptions,
} from './widgets/distributed-event-recovery-confirm/distributed-event-recovery-confirm';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../core/i18n/transloco.testing';
//#endif
import { AccountPreferencesService } from '../../../../core/services/account-preferences-service';
import { AuthService } from '../../../../core/services/auth-service';
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { PERMISSIONS } from '../../../../shared/constants/permission.constants';
import { formatAppDate } from '../../../../shared/pipes/app-date-pipe';
import * as Dto from '../../dtos/distributed-event.dto';
import { DistributedEventService } from '../../services/distributed-event-service';

const OUTBOX: Dto.DistributedEventOutboxOutputDto = {
  id: '9007199254740993',
  eventId: 'event-1',
  eventName: 'order.paid',
  status: 'deadLettered',
  attemptCount: 8,
  redeliverCount: 0,
  occurredAt: '2026-01-01T00:00:00Z',
  creationTime: '2026-01-01T00:00:01Z',
  lastModificationTime: '2026-01-02T00:00:00Z',
};
const STATUS: Dto.DistributedEventStatusOutputDto = {
  pendingOutbox: 0,
  pendingInbox: 0,
  deadLettered: 1,
  failedInbox: 0,
  pendingRejections: 0,
  sentLast24Hours: 0,
  processedLast24Hours: 0,
  transportStatus: 'healthy',
  payloadEnabled: true,
  transportName: 'RabbitMQ',
  testEventEnabled: true,
};

/** 权限裁剪、URL 状态、按需载荷与筛选恢复；事务审计由后端验证。 */
describe('DistributedEvents page', () => {
  let fixture: ComponentFixture<DistributedEvents>;
  let component: DistributedEvents;
  let router: Router;
  let service: ReturnType<typeof mockService>;
  let confirm: { open: ReturnType<typeof vi.fn> };

  function mockService() {
    return {
      getStatus: vi.fn().mockReturnValue(of(STATUS)),
      getCatalog: vi.fn().mockReturnValue(of([])),
      getOutboxList: vi.fn().mockReturnValue(of({ items: [OUTBOX], totalCount: 1200 })),
      getInboxList: vi.fn().mockReturnValue(of({ items: [], totalCount: 0 })),
      getRejectionList: vi.fn().mockReturnValue(of({ items: [], totalCount: 0 })),
      getOutbox: vi.fn().mockReturnValue(of({ record: OUTBOX, headers: {} })),
      getInbox: vi.fn().mockReturnValue(of(null)),
      getRejection: vi.fn().mockReturnValue(of(null)),
      getOutboxPayload: vi.fn().mockReturnValue(of({ payload: '{}' })),
      getInboxPayload: vi.fn().mockReturnValue(of({ payload: '{}' })),
      getRejectionPayload: vi.fn().mockReturnValue(of({ rawBody: '' })),
      redeliver: vi
        .fn()
        .mockReturnValue(of({ affectedCount: 1200, deferredCount: 0, skipped: [] })),
      replay: vi.fn().mockReturnValue(of({ affectedCount: 1, deferredCount: 0, skipped: [] })),
      redecode: vi.fn(),
      discard: vi.fn(),
      sendTestEvent: vi.fn().mockReturnValue(of({ eventId: 'test-event' })),
    };
  }

  async function setup(
    manage = false,
    tenantId: string | null = null,
    query: Record<string, string> = {},
    stub = mockService(),
  ) {
    service = stub;
    confirm = { open: vi.fn().mockResolvedValue(false) };
    await TestBed.configureTestingModule({
      imports: [DistributedEvents],
      providers: [
        provideRouter([{ path: 'platform/distributed-events', children: [] }]),
        { provide: PlatformLocation, useClass: MockPlatformLocation },
        provideHttpClient(),
        provideHttpClientTesting(),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en'], {
          getTranslation: () =>
            of({
              distributedEvents: {
                confirm: '{{scope}} | {{count}} | {{filter}} | {{before}}',
                confirmation: {
                  result: {
                    redecode:
                      'Re-decoded {{count}} · Handed off {{deferred}} · Skipped {{skipped}}',
                  },
                },
              },
            }),
        }),
        //#endif
        { provide: DistributedEventService, useValue: service },
        { provide: DistributedEventRecoveryConfirmService, useValue: confirm },
        { provide: AuthService, useValue: { currentUser: signal({ tenantId }) } },
        {
          provide: AccountPreferencesService,
          useValue: { timeZone: signal('UTC'), displayLocale: signal('en') },
        },
      ],
    }).compileComponents();
    router = TestBed.inject(Router);
    await router.navigate(['/platform/distributed-events'], { queryParams: query });
    TestBed.inject(AuthorizationService).setPermissions({
      permissions: [
        PERMISSIONS.distributedEvents.default,
        ...(manage ? [PERMISSIONS.distributedEvents.manage] : []),
      ],
      isSuperAdmin: false,
      versionToken: 'r1',
    });
    fixture = TestBed.createComponent(DistributedEvents);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  }
  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }
  afterEach(() => fixture?.destroy());

  it('shows read-only metadata and hides every management and payload entry point', async () => {
    await setup(false, null, { record: OUTBOX.id });
    await settle();
    expect(fixture.nativeElement.querySelector('[data-testid="event-send-test"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="event-selection-bar"]')).toBeNull();
    expect(document.querySelector('[data-testid="event-payload"]')).toBeNull();
    const sheet = fixture.debugElement.query(By.directive(DistributedEventRecordSheet))
      .componentInstance as DistributedEventRecordSheet;
    sheet.loadPayload();
    expect(service.getOutboxPayload).not.toHaveBeenCalled();
    expect(service.getOutbox).toHaveBeenCalledWith(OUTBOX.id);
  });

  it('hides host-only views and never sends a database parameter from a tenant session', async () => {
    await setup(false, 'tenant-1', { view: 'rejections', database: 'host' });
    await settle();
    expect(component.view()).toBe('outbox');
    expect(service.getRejectionList).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[data-testid="event-database"]')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toMatch(/Current tenant|Physical database/);
    expect(fixture.nativeElement.querySelector('[hlmTabsTrigger="rejections"]')).toBeNull();
    expect(service.getOutboxList.mock.calls.at(-1)?.[0]).not.toHaveProperty('database');
  });

  it('shows no tenant or database selector in host scope and sends no scope parameters', async () => {
    await setup(true);
    await settle();
    expect(fixture.nativeElement.querySelector('[data-testid="event-database"]')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toMatch(/Current tenant|Physical database/);
    expect(service.getOutboxList.mock.calls.at(-1)?.[0]).not.toHaveProperty('database');
    expect(service.getStatus).toHaveBeenCalledWith();
  });

  it('restores URL filters, pagination and an exact long record ID', async () => {
    await setup(true, null, {
      status: 'deadLettered',
      keyword: 'event-1',
      page: '2',
      pageSize: '20',
      record: OUTBOX.id,
    });
    expect(component.recordId()).toBe(OUTBOX.id);
    expect(component.page()).toBe(2);
    expect(service.getOutboxList.mock.calls.at(-1)?.[0]).toMatchObject({
      status: 'deadLettered',
      keyword: 'event-1',
      offset: 40,
      limit: 20,
    });
    component.jump('inbox', 'failed', 'order.paid');
    await settle();
    expect(router.url).toContain('view=inbox');
    expect(router.url).toContain('eventName=order.paid');
    expect(component.recordId()).toBeNull();
    expect(component.page()).toBe(0);
  });

  it('uses an unpaged filter and the original loading cutoff for batch recovery', async () => {
    await setup(true, null, { status: 'deadLettered', eventName: 'order.paid' });
    const before = component.cutoff();
    await component.recover('filter');
    const options = confirm.open.mock.calls[0][0] as DistributedEventRecoveryOptions;
    //#if (IncludeLocalization)
    expect(options.criteria).toContain('distributedEvents.state.deadLettered');
    //#else
    expect(options.criteria).toContain('Dead letter');
    //#endif
    options.action!().subscribe();
    expect(service.redeliver).toHaveBeenCalledWith({
      filter: {
        before,
        eventName: 'order.paid',
        keyword: undefined,
        errorCode: undefined,
        consumer: undefined,
        reason: undefined,
      },
    });
    expect(service.redeliver.mock.calls[0][0].filter).not.toHaveProperty('offset');
    expect(service.redeliver.mock.calls[0][0].filter).not.toHaveProperty('limit');
  });

  it('requires explicit confirmation before re-executing an already processed handler', async () => {
    await setup(true, null, { view: 'inbox', status: 'processed' });
    await component.recover('filter');
    const options = confirm.open.mock.calls[0][0] as DistributedEventRecoveryOptions;
    //#if (IncludeLocalization)
    expect(options.message).toContain('distributedEvents.replayWarning');
    //#else
    expect(options.message).toContain('execute the handler again');
    //#endif
    expect(service.replay).not.toHaveBeenCalled();
  });

  it('reports handoff separately from completed re-decodes', async () => {
    await setup(true, null, { view: 'rejections', status: 'pending' });
    const notice = vi.spyOn(toast, 'success').mockReturnValue('handoff');
    try {
      const row: Dto.DistributedEventRejectionOutputDto = {
        id: '7',
        transport: 'RabbitMQ',
        coordinates: {},
        reason: 'invalid-envelope',
        error: 'Safe reason.',
        status: 'pending',
        redecodeCount: 0,
        receivedAt: '2026-11-09T00:00:00Z',
        lastModificationTime: '2026-11-09T00:00:00Z',
      };
      service.redecode.mockReturnValue(of({ affectedCount: 3, deferredCount: 2, skipped: [] }));
      await component.recover('single', row);
      const options = confirm.open.mock.calls[0][0] as DistributedEventRecoveryOptions;
      options.action!().subscribe();
      expect(notice).toHaveBeenCalledWith('Re-decoded 1 · Handed off 2 · Skipped 0');
    } finally {
      notice.mockRestore();
    }
  });

  it('can discard queued re-decodes by the exact current status without allowing re-decode again', async () => {
    await setup(true, null, { view: 'rejections', status: 'redecodePending' });
    expect(component.bulkAllowed()).toBe(false);
    expect(component.bulkDiscardAllowed()).toBe(true);
    service.discard.mockReturnValue(of({ affectedCount: 1, deferredCount: 0, skipped: [] }));
    await component.recover('filter', undefined, true);
    const options = confirm.open.mock.calls[0][0] as DistributedEventRecoveryOptions;
    options.action!().subscribe();
    expect(service.discard).toHaveBeenCalledWith({
      filter: expect.objectContaining({ status: 'redecodePending' }),
    });
    expect(service.redecode).not.toHaveBeenCalled();
  });

  it('cancels a previous payload request when the opened record changes', async () => {
    await setup(true, null, { record: OUTBOX.id });
    const delayed = new Subject<Dto.DistributedEventPayloadOutputDto>();
    service.getOutboxPayload.mockReturnValueOnce(delayed);
    const sheet = fixture.debugElement.query(By.directive(DistributedEventRecordSheet))
      .componentInstance as DistributedEventRecordSheet;
    sheet.loadPayload();
    component.openRecord('2');
    await settle();
    delayed.next({ payload: 'stale secret' });
    delayed.complete();
    expect(sheet.payload()).toBeNull();
    expect(sheet.payloadLoading()).toBe(false);
  });

  it('hides test publication in tenant scope', async () => {
    await setup(true, 'tenant-1');
    expect(component.canTest()).toBe(false);
    component.sendTest();
    expect(service.sendTestEvent).not.toHaveBeenCalled();
  });

  const START = '2026-01-01T00:00:00.000Z';
  const END = '2026-01-07T23:59:59.999Z';
  const host = () => fixture.nativeElement as HTMLElement;

  it('sends a valid URL time range to the list and the recovery filter and lists it in the confirmation', async () => {
    await setup(true, null, { status: 'deadLettered', startTime: START, endTime: END });
    expect(service.getOutboxList.mock.calls.at(-1)?.[0]).toMatchObject({
      startTime: START,
      endTime: END,
      before: component.cutoff(),
    });
    expect(component.selectedRange()).toBeDefined();
    await component.recover('filter');
    const options = confirm.open.mock.calls[0][0] as DistributedEventRecoveryOptions;
    //#if (IncludeLocalization)
    expect(options.criteria).toContain('distributedEvents.field.timeRange');
    //#else
    expect(options.criteria).toContain('Time range');
    //#endif
    expect(options.criteria).toContain(
      `${formatAppDate(START, 'full', 'UTC', 'en')} ~ ${formatAppDate(END, 'full', 'UTC', 'en')}`,
    );
    expect(options.cutoff).not.toBe('');
    options.action!().subscribe();
    expect(service.redeliver.mock.calls[0][0].filter).toMatchObject({
      before: component.cutoff(),
      startTime: START,
      endTime: END,
    });
  });

  it.each([
    { startTime: START },
    { startTime: 'yesterday', endTime: END },
    { startTime: END, endTime: START },
    // 浏览器能解析、接口会拒绝的日期写法（缺时区）
    { startTime: '2026-11-01', endTime: '2026-11-07' },
    { startTime: '2026-11-01T00:00:00', endTime: '2026-11-07T23:59:59' },
  ])(
    'treats an incomplete, malformed, zoneless or reversed URL range as unfiltered (%o)',
    async (range) => {
      await setup(true, null, { status: 'deadLettered', ...(range as Record<string, string>) });
      const input = service.getOutboxList.mock.calls.at(-1)?.[0];
      expect(input).not.toHaveProperty('startTime');
      expect(input).not.toHaveProperty('endTime');
      expect(component.selectedRange()).toBeUndefined();
      await component.recover('filter');
      const options = confirm.open.mock.calls[0][0] as DistributedEventRecoveryOptions;
      options.action!().subscribe();
      expect(service.redeliver.mock.calls[0][0].filter).not.toHaveProperty('startTime');
      expect(service.redeliver.mock.calls[0][0].filter).not.toHaveProperty('endTime');
    },
  );

  it('writes the selected days in the display time zone as UTC bounds and returns to the first page', async () => {
    await setup(true, null, { page: '3' });
    component.onRangeChange([new Date(2026, 0, 1), new Date(2026, 0, 7)]);
    await settle();
    expect(component.query()['startTime']).toBe(START);
    expect(component.query()['endTime']).toBe(END);
    expect(component.page()).toBe(0);
    component.onRangeChange(null);
    await settle();
    expect(component.query()['startTime']).toBeUndefined();
  });

  it('clears the time range when switching tabs or jumping to another view', async () => {
    await setup(true, null, { startTime: START, endTime: END });
    component.setView('inbox');
    await settle();
    expect(component.timeRange()).toBeNull();
    await router.navigate(['/platform/distributed-events'], {
      queryParams: { view: 'inbox', startTime: START, endTime: END },
    });
    await settle();
    component.jump('outbox', 'deadLettered');
    await settle();
    expect(router.url).not.toContain('startTime');
    expect(router.url).not.toContain('endTime');
  });

  it('sends an explicit-offset URL range to the list and the recovery filter as UTC', async () => {
    await setup(true, null, {
      status: 'deadLettered',
      startTime: '2026-01-01T08:00:00+08:00',
      endTime: '2026-01-08T07:59:59.999+08:00',
    });
    expect(service.getOutboxList.mock.calls.at(-1)?.[0]).toMatchObject({
      startTime: START,
      endTime: END,
    });
    expect(component.selectedRange()).toBeDefined();
    await component.recover('filter');
    (confirm.open.mock.calls[0][0] as DistributedEventRecoveryOptions).action!().subscribe();
    expect(service.redeliver.mock.calls[0][0].filter).toMatchObject({
      startTime: START,
      endTime: END,
    });
  });

  it('shows a test event sent while the outbox is filtered to historical dates', async () => {
    await setup(true, null, { view: 'outbox', startTime: START, endTime: END });
    const sent: Dto.DistributedEventOutboxOutputDto = {
      ...OUTBOX,
      id: '42',
      eventId: 'test-event',
      eventName: 'event-bus.test',
      status: 'sent',
    };
    // 只有不带时间范围、按新事件 ID 查询时才返回新事件；还带着历史日期时照旧只有旧记录
    service.getOutboxList.mockImplementation((input: Dto.GetDistributedEventOutboxInputDto) =>
      of(
        !input.startTime && !input.endTime && input.keyword === 'test-event'
          ? { items: [sent], totalCount: 1 }
          : { items: [OUTBOX], totalCount: 1 },
      ),
    );
    component.sendTest();
    await settle();
    expect(component.query()['startTime']).toBeUndefined();
    expect(component.query()['endTime']).toBeUndefined();
    expect(host().querySelector('[data-record-id="42"]')).not.toBeNull();
    expect(host().querySelector(`[data-record-id="${OUTBOX.id}"]`)).toBeNull();
  });

  it('drops rows from the previous filter when loading a changed filter fails', async () => {
    await setup(true, null, { status: 'deadLettered' });
    expect(host().querySelector(`[data-record-id="${OUTBOX.id}"]`)).not.toBeNull();
    expect(component.totalCount()).toBe(1200);
    const notice = vi.spyOn(toast, 'error').mockReturnValue('load-failed');
    try {
      service.getOutboxList.mockReturnValueOnce(throwError(() => new Error('offline')));
      component.onRangeChange([new Date(2026, 0, 1), new Date(2026, 0, 7)]);
      await settle();
      expect(component.timeRange()).toEqual({ startTime: START, endTime: END });
      // 旧条件下的行与总数不留下，进入可重试的空态
      expect(host().querySelector(`[data-record-id="${OUTBOX.id}"]`)).toBeNull();
      expect(component.totalCount()).toBe(0);
      expect(host().querySelector('[data-testid=event-load-error]')).not.toBeNull();
      expect(notice).not.toHaveBeenCalled();
      // 选择条里"按当前筛选重投"不可用，直接调用也不会拿旧总数开确认框
      const filterButton = Array.from(
        host().querySelectorAll('[data-testid=event-selection-bar] button'),
      ).at(-1) as HTMLButtonElement;
      expect(filterButton.disabled).toBe(true);
      await component.recover('filter');
      expect(confirm.open).not.toHaveBeenCalled();
    } finally {
      notice.mockRestore();
    }
  });

  it('keeps the loaded rows and shows a toast when a refresh fails, then recovers on retry', async () => {
    await setup(true);
    expect(host().querySelector(`[data-record-id="${OUTBOX.id}"]`)).not.toBeNull();
    const notice = vi.spyOn(toast, 'error').mockReturnValue('refresh-failed');
    try {
      service.getOutboxList.mockReturnValueOnce(throwError(() => new Error('offline')));
      component.reload();
      await settle();
      expect(notice).toHaveBeenCalledOnce();
      expect(host().querySelector(`[data-record-id="${OUTBOX.id}"]`)).not.toBeNull();
      expect(component.totalCount()).toBe(1200);
      expect(host().querySelector('[data-testid=event-load-error]')).toBeNull();
      component.reload();
      await settle();
      expect(host().querySelector(`[data-record-id="${OUTBOX.id}"]`)).not.toBeNull();
      expect(notice).toHaveBeenCalledOnce();
    } finally {
      notice.mockRestore();
    }
  });

  it('shows a retryable empty state only when the first load fails', async () => {
    const failing = mockService();
    failing.getOutboxList.mockReturnValueOnce(throwError(() => new Error('offline')));
    await setup(true, null, {}, failing);
    const error = host().querySelector('[data-testid=event-load-error]')!;
    expect(error.getAttribute('data-slot')).toBe('empty');
    expect(host().querySelector('[data-testid=event-table]')).toBeNull();
    (error.querySelector('button') as HTMLButtonElement).click();
    await settle();
    expect(host().querySelector('[data-testid=event-load-error]')).toBeNull();
    expect(host().querySelector(`[data-record-id="${OUTBOX.id}"]`)).not.toBeNull();
  });

  it('hides the time range on the event catalog', async () => {
    await setup(true, null, { view: 'catalog' });
    expect(host().querySelector('[data-testid=event-time-range]')).toBeNull();
    expect(host().querySelector('[data-testid=event-status-filter]')).toBeNull();
  });

  it('orders the toolbar right group as time range then a secondary refresh that never disables', async () => {
    await setup(true);
    const refresh = host().querySelector('[data-testid=event-refresh]') as HTMLButtonElement;
    const range = host().querySelector('[data-testid=event-time-range]')!;
    expect(range.parentElement).toBe(refresh.parentElement);
    expect(range.compareDocumentPosition(refresh)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    expect(refresh.classList).toContain('bg-secondary');
    const pending = new Subject<{
      items: Dto.DistributedEventOutboxOutputDto[];
      totalCount: number;
    }>();
    service.getOutboxList.mockReturnValueOnce(pending);
    refresh.click();
    fixture.detectChanges();
    expect(refresh.disabled).toBe(false);
    expect(refresh.querySelector('hlm-spinner')).not.toBeNull();
    pending.next({ items: [OUTBOX], totalCount: 1 });
    pending.complete();
    await settle();
    expect(refresh.querySelector('hlm-spinner')).toBeNull();
  });

  it('filters status with a single-choice toggle group that clears on a second click', async () => {
    await setup(true);
    // 发件箱状态依次是待发送、已发送、死信
    const item = () =>
      host().querySelectorAll(
        '[data-testid=event-status-filter] [hlmToggleGroupItem]',
      )[2] as HTMLButtonElement;
    expect(
      host().querySelectorAll('[data-testid=event-status-filter] [hlmToggleGroupItem]'),
    ).toHaveLength(3);
    expect(host().querySelector('app-faceted-filter')?.textContent).not.toMatch(/status/i);
    item().click();
    await settle();
    expect(component.query()['status']).toBe('deadLettered');
    expect(item().getAttribute('data-state')).toBe('on');
    item().click();
    await settle();
    expect(component.query()['status']).toBeUndefined();
  });

  it('shows the transport name with one health badge and relative report times', async () => {
    await setup(true);
    component.status.set({
      ...STATUS,
      transportStatusChangedAt: '2026-01-01T10:20:30Z',
      transportReportedAt: '2026-01-01T11:22:33Z',
    });
    await settle();
    const row = host().querySelector('[data-testid=event-transport-row]')!;
    expect(row.querySelector('[data-testid=event-transport-name]')?.textContent).toContain(
      'RabbitMQ',
    );
    expect(row.querySelectorAll('[hlmBadge]')).toHaveLength(1);
    expect(row.querySelector('[hlmBadge]')?.className).toContain("before:content-['']");
    expect(
      row.querySelector('[data-testid=event-transport-reported] time')?.getAttribute('datetime'),
    ).toBe('2026-01-01T11:22:33Z');
    expect(row.querySelector('p')?.classList).toContain('text-xs');
    const send = row.querySelector('[data-testid=event-send-test]')!;
    expect(send.classList).toContain('bg-background');
  });

  describe('selection bar', () => {
    const rejection = (
      id: string,
      status: Dto.DistributedEventRejectionStatus,
    ): Dto.DistributedEventRejectionOutputDto => ({
      id,
      transport: 'RabbitMQ',
      coordinates: {},
      reason: 'invalid-payload',
      error: 'Invalid payload.',
      status,
      redecodeCount: 0,
      receivedAt: '2026-11-09T00:00:00Z',
      lastModificationTime: '2026-11-09T00:00:00Z',
    });
    const bar = () => host().querySelector('[data-testid=event-selection-bar]');
    const buttons = () =>
      Array.from(bar()?.querySelectorAll('button') ?? []) as HTMLButtonElement[];

    it('stays hidden until records are selected or the status allows recovery by condition', async () => {
      await setup(true);
      expect(bar()).toBeNull();
      component.selected.set([OUTBOX.id]);
      await settle();
      expect(bar()).not.toBeNull();
      expect(buttons().find((b) => /redeliver/i.test(b.textContent!))?.disabled).toBe(false);
      buttons()[0].click();
      await settle();
      expect(component.selected()).toEqual([]);
      expect(bar()).toBeNull();
    });

    it('offers recovery by the current filter for dead letters with the selected action disabled at zero', async () => {
      await setup(true, null, { status: 'deadLettered' });
      expect(bar()).not.toBeNull();
      // 取消选择、重投已选（0）、按当前筛选重投
      expect(buttons()).toHaveLength(3);
      expect(buttons().map((button) => button.disabled)).toEqual([true, true, false]);
    });

    it('counts and submits only the rejections each action applies to', async () => {
      await setup(true, null, { view: 'rejections' });
      service.getRejectionList.mockReturnValue(
        of({
          items: [rejection('1', 'pending'), rejection('2', 'redecodePending')],
          totalCount: 2,
        }),
      );
      component.reload();
      await settle();
      component.selected.set(['1', '2']);
      await settle();
      expect(component.selectedForRecovery()).toEqual(['1']);
      expect(component.selectedForDiscard()).toEqual(['1', '2']);
      // 取消选择、重新解码已选、丢弃已选；状态未筛选时没有按条件的入口
      expect(buttons().map((button) => button.disabled)).toEqual([false, false, false]);
      service.redecode.mockReturnValue(of({ affectedCount: 1, deferredCount: 0, skipped: [] }));
      await component.recover('selected');
      (confirm.open.mock.calls.at(-1)![0] as DistributedEventRecoveryOptions).action!().subscribe();
      expect(service.redecode).toHaveBeenCalledWith({ ids: ['1'] });
      // 恢复成功后列表重新加载并清空勾选，重新勾选后再丢弃
      await settle();
      component.selected.set(['1', '2']);
      service.discard.mockReturnValue(of({ affectedCount: 2, deferredCount: 0, skipped: [] }));
      await component.recover('selected', undefined, true);
      (confirm.open.mock.calls.at(-1)![0] as DistributedEventRecoveryOptions).action!().subscribe();
      expect(service.discard).toHaveBeenCalledWith({ ids: ['1', '2'] });
    });

    it('offers discard by the current filter for pending and queued rejections', async () => {
      await setup(true, null, { view: 'rejections', status: 'redecodePending' });
      expect(component.bulkDiscardAllowed()).toBe(true);
      expect(buttons().some((button) => /filter/i.test(button.textContent!))).toBe(true);
    });
  });
});
