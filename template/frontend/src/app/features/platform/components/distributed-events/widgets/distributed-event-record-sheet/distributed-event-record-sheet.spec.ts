import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, Subject } from 'rxjs';

import { DistributedEventRecordSheet } from './distributed-event-record-sheet';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../core/i18n/transloco.testing';
//#endif
import { AccountPreferencesService } from '../../../../../../core/services/account-preferences-service';
import {
  DistributedEventRejectionDetailOutputDto,
  DistributedEventOutboxDetailOutputDto,
} from '../../../../dtos/distributed-event.dto';
import { DistributedEventService } from '../../../../services/distributed-event-service';

const TENANT_ID = '00000000-0000-4000-8000-000000000011';
const DETAIL: DistributedEventOutboxDetailOutputDto = {
  record: {
    id: '1',
    eventId: '00000000-0000-4000-8000-000000000101',
    eventName: 'order.paid',
    tenantId: TENANT_ID,
    status: 'sent',
    attemptCount: 1,
    redeliverCount: 0,
    creationTime: '2026-01-01T00:00:01Z',
    occurredAt: '2026-01-01T00:00:00Z',
    lastModificationTime: '2026-01-01T00:00:01Z',
  },
  headers: {},
};

describe('DistributedEventRecordSheet tenant identity', () => {
  let fixture: ComponentFixture<DistributedEventRecordSheet>;

  afterEach(() => fixture?.destroy());

  it.each([undefined, '', 'Portal East'])(
    'shows the tenant ID once while preserving an available name (%s)',
    async (name) => {
      await TestBed.configureTestingModule({
        imports: [DistributedEventRecordSheet],
        providers: [
          //#if (IncludeLocalization)
          ...provideTranslocoTesting(),
          //#endif
          {
            provide: AccountPreferencesService,
            useValue: { timeZone: signal('UTC'), displayLocale: signal('en') },
          },
          { provide: DistributedEventService, useValue: { getOutbox: () => of(DETAIL) } },
        ],
      }).compileComponents();
      fixture = TestBed.createComponent(DistributedEventRecordSheet);
      fixture.componentRef.setInput('tenantNames', name === undefined ? {} : { [TENANT_ID]: name });
      fixture.componentRef.setInput('recordId', DETAIL.record.id);
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      const value = Array.from(document.querySelectorAll('[data-testid=event-detail] dd')).find(
        (element) => element.textContent.includes(TENANT_ID),
      );
      expect(value).toBeDefined();
      expect(value!.textContent.split(TENANT_ID)).toHaveLength(2);
      if (name) expect(value!.textContent).toContain(name);
      else expect(value!.textContent.trim()).toBe(TENANT_ID);
    },
  );

  it('does not request payload when closed and cancels an in-flight request when access closes', async () => {
    const payload = new Subject<{ payload: string }>();
    const getOutboxPayload = vi.fn(() => payload);
    await TestBed.configureTestingModule({
      imports: [DistributedEventRecordSheet],
      providers: [
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(),
        //#endif
        {
          provide: AccountPreferencesService,
          useValue: { timeZone: signal('UTC'), displayLocale: signal('en') },
        },
        {
          provide: DistributedEventService,
          useValue: { getOutbox: () => of(DETAIL), getOutboxPayload },
        },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(DistributedEventRecordSheet);
    fixture.componentRef.setInput('recordId', DETAIL.record.id);
    fixture.componentRef.setInput('canManage', true);
    fixture.componentRef.setInput('payloadEnabled', false);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.componentInstance.loadPayload();
    expect(getOutboxPayload).not.toHaveBeenCalled();
    fixture.componentRef.setInput('payloadEnabled', true);
    fixture.detectChanges();
    fixture.componentInstance.loadPayload();
    expect(getOutboxPayload).toHaveBeenCalledOnce();
    expect(payload.observed).toBe(true);
    fixture.componentRef.setInput('payloadEnabled', false);
    fixture.detectChanges();
    expect(payload.observed).toBe(false);
    payload.next({ payload: 'sensitive body' });
    expect(fixture.componentInstance.payload()).toBeNull();
    expect(fixture.componentInstance.payloadLoading()).toBe(false);
  });
  it('offers discard for a queued re-decode while hiding the duplicate re-decode action', async () => {
    const detail: DistributedEventRejectionDetailOutputDto = {
      record: {
        id: '3',
        transport: 'RabbitMQ',
        coordinates: {},
        reason: 'invalid-envelope',
        error: 'Safe reason.',
        status: 'redecodePending',
        redecodeCount: 0,
        receivedAt: '2026-11-09T00:00:00Z',
        lastModificationTime: '2026-11-09T00:00:00Z',
      },
      headers: {},
      coordinates: {},
    };
    await TestBed.configureTestingModule({
      imports: [DistributedEventRecordSheet],
      providers: [
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(),
        //#endif
        {
          provide: AccountPreferencesService,
          useValue: { timeZone: signal('UTC'), displayLocale: signal('en') },
        },
        { provide: DistributedEventService, useValue: { getRejection: () => of(detail) } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(DistributedEventRecordSheet);
    fixture.componentRef.setInput('view', 'rejections');
    fixture.componentRef.setInput('canManage', true);
    fixture.componentRef.setInput('recordId', '3');
    const recover = vi.fn();
    fixture.componentInstance.recover.subscribe(recover);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const buttons = Array.from(document.querySelectorAll('button'));
    const discard = buttons.find((button) => /discard/i.test(button.textContent || ''));
    expect(discard).toBeDefined();
    expect(buttons.some((button) => /redecode|re-decode/i.test(button.textContent || ''))).toBe(
      false,
    );
    discard!.click();
    expect(recover).toHaveBeenCalledWith({ record: detail.record, discard: true });
  });
});
