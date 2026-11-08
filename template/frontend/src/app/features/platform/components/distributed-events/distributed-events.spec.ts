import { PlatformLocation } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { MockPlatformLocation } from '@angular/common/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';

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
      getDatabases: vi.fn().mockReturnValue(of([{ key: 'host', isHost: true, tenantIds: [] }])),
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
      redeliver: vi.fn().mockReturnValue(of({ affectedCount: 1200, skipped: [] })),
      replay: vi.fn().mockReturnValue(of({ affectedCount: 1, skipped: [] })),
      redecode: vi.fn(),
      discard: vi.fn(),
      sendTestEvent: vi.fn().mockReturnValue(of({ eventId: 'test-event' })),
    };
  }

  async function setup(
    manage = false,
    tenantId: string | null = null,
    query: Record<string, string> = {},
  ) {
    service = mockService();
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
              distributedEvents: { confirm: '{{scope}} | {{count}} | {{filter}} | {{before}}' },
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
    expect(fixture.nativeElement.querySelector('[data-testid="event-bulk-actions"]')).toBeNull();
    expect(document.querySelector('[data-testid="event-payload"]')).toBeNull();
    const sheet = fixture.debugElement.query(By.directive(DistributedEventRecordSheet))
      .componentInstance as DistributedEventRecordSheet;
    sheet.loadPayload();
    expect(service.getOutboxPayload).not.toHaveBeenCalled();
    expect(service.getOutbox).toHaveBeenCalledWith(OUTBOX.id, 'host');
  });

  it('hides host-only views and never sends a database parameter from a tenant session', async () => {
    await setup(false, 'tenant-1', { view: 'rejections', database: 'host' });
    await settle();
    expect(component.view()).toBe('outbox');
    expect(component.database()).toBeNull();
    expect(service.getDatabases).not.toHaveBeenCalled();
    expect(service.getRejectionList).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[data-testid="event-database"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[hlmTabsTrigger="rejections"]')).toBeNull();
    expect(service.getOutboxList.mock.calls.at(-1)?.[0].database).toBeNull();
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
    expect(service.redeliver).toHaveBeenCalledWith(
      {
        filter: {
          before,
          eventName: 'order.paid',
          keyword: undefined,
          errorCode: undefined,
          consumer: undefined,
          reason: undefined,
        },
      },
      'host',
    );
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

  it('hides test publication when inspecting a tenant database from host scope', async () => {
    await setup(true, null, { database: 'tenant-db' });
    expect(component.canTest()).toBe(false);
    component.sendTest();
    expect(service.sendTestEvent).not.toHaveBeenCalled();
  });
});
