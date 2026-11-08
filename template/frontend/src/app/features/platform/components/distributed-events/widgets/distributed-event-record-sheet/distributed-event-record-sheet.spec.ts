import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { DistributedEventRecordSheet } from './distributed-event-record-sheet';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../core/i18n/transloco.testing';
//#endif
import { AccountPreferencesService } from '../../../../../../core/services/account-preferences-service';
import { DistributedEventOutboxDetailOutputDto } from '../../../../dtos/distributed-event.dto';
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
});
