import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { toast } from '@spartan-ng/brain/sonner';
import { of, Subject } from 'rxjs';

//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../core/i18n/transloco.testing';
//#endif
import { AccountPreferencesService } from '../../../../../../core/services/account-preferences-service';
import {
  DistributedEventCatalogOutputDto,
  DistributedEventRejectionDetailOutputDto,
  DistributedEventOutboxDetailOutputDto,
} from '../../../../dtos/distributed-event.dto';
import { DistributedEventService } from '../../../../services/distributed-event-service';
import { DistributedEventRecordSheet } from './distributed-event-record-sheet';

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

describe('DistributedEventRecordSheet', () => {
  let fixture: ComponentFixture<DistributedEventRecordSheet>;

  afterEach(() => fixture?.destroy());

  it('omits tenant identity from the detail overview', async () => {
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
    fixture.componentRef.setInput('recordId', DETAIL.record.id);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const detail = document.querySelector('[data-testid=event-detail]');
    expect(detail).not.toBeNull();
    expect(detail!.textContent).not.toContain(TENANT_ID);
    expect(detail!.textContent).not.toContain('tenantId');
  });

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
    const header = document.querySelector('[hlmSheetHeader]')!;
    expect(
      Array.from(header.querySelectorAll('button')).some((button) =>
        /redecode|re-decode/i.test(button.textContent || ''),
      ),
    ).toBe(false);
    // 破坏性的丢弃不平铺在标题行，收进 … 菜单的分隔线之后
    expect(
      Array.from(header.querySelectorAll('button')).some((button) =>
        /discard/i.test(button.textContent || ''),
      ),
    ).toBe(false);
    (header.querySelector('[data-testid=event-detail-more]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
    const menu = document.querySelector('hlm-dropdown-menu')!;
    const items = Array.from(menu.children);
    const discard = items.at(-1) as HTMLButtonElement;
    expect(items.at(-2)?.matches('hlm-dropdown-menu-separator')).toBe(true);
    expect(discard.textContent).toMatch(/discard/i);
    expect(discard.getAttribute('data-variant')).toBe('destructive');
    discard.click();
    expect(recover).toHaveBeenCalledWith({ record: detail.record, discard: true });
  });

  async function open(service: object, inputs: Record<string, unknown>) {
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
        { provide: DistributedEventService, useValue: service },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(DistributedEventRecordSheet);
    for (const [key, value] of Object.entries(inputs)) fixture.componentRef.setInput(key, value);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return document.querySelector('[data-testid=event-detail]')!;
  }

  // 详情首次加载：表单形骨架占住正文，不放裸 spinner；失败在同一位置给原因，重试只重发详情请求
  it('shows a skeleton while the detail loads and retries a failed detail in place', async () => {
    const pending = new Subject<DistributedEventOutboxDetailOutputDto>();
    const getOutbox = vi.fn().mockReturnValueOnce(pending).mockReturnValueOnce(of(DETAIL));
    const sheet = await open({ getOutbox }, { recordId: DETAIL.record.id });
    expect(sheet.querySelector('app-dialog-loading')).not.toBeNull();
    expect(sheet.querySelector('hlm-spinner')).toBeNull();

    pending.error(new Error('Detail unavailable'));
    fixture.detectChanges();
    await fixture.whenStable();
    expect(sheet.querySelector('app-dialog-loading')).toBeNull();
    sheet.querySelector<HTMLButtonElement>('[data-testid=event-detail-retry]')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(getOutbox).toHaveBeenCalledTimes(2);
    expect(sheet.querySelector('[data-testid=event-detail-retry]')).toBeNull();
    expect(sheet.querySelector('[data-testid=event-detail-status]')).not.toBeNull();
  });

  it('puts the status next to the title and the view, record and event IDs on the meta line', async () => {
    const sheet = await open({ getOutbox: () => of(DETAIL) }, { recordId: DETAIL.record.id });
    const title = sheet.querySelector('[hlmSheetTitle]')!;
    expect(title.classList).toContain('text-base');
    expect(title.parentElement!.querySelector('[data-testid=event-detail-status]')).not.toBeNull();
    const meta = sheet.querySelector('[data-testid=event-detail-meta]')!;
    expect(meta.classList).toContain('text-xs');
    expect(meta.textContent).toMatch(/outbox/i);
    expect(meta.textContent).toContain(DETAIL.record.id);
    expect(meta.textContent).toContain(DETAIL.record.eventId);
    expect(sheet.querySelector('[data-testid=event-detail-alert]')).toBeNull();
    for (const heading of Array.from(sheet.querySelectorAll('section h3')))
      expect(heading.closest('.border-b')).not.toBeNull();
    for (const list of Array.from(sheet.querySelectorAll('dl')))
      expect(list.classList).toContain('grid-cols-[8rem_minmax(0,1fr)]');
  });

  it('explains a dead letter with its error code, diagnostic and next step above the overview', async () => {
    const dead: DistributedEventOutboxDetailOutputDto = {
      ...DETAIL,
      record: {
        ...DETAIL.record,
        status: 'deadLettered',
        lastErrorCode: 'retry-window-exceeded',
        lastError: 'Broker connection refused.',
      },
    };
    const sheet = await open(
      { getOutbox: () => of(dead) },
      { recordId: dead.record.id, canManage: true },
    );
    const alert = sheet.querySelector('[data-testid=event-detail-alert]')!;
    expect(alert.getAttribute('data-slot')).toBe('alert');
    expect(alert.textContent).toContain('retry-window-exceeded');
    expect(alert.textContent).toContain('Broker connection refused.');
    //#if (IncludeLocalization)
    expect(alert.textContent).toContain('distributedEvents.nextStep.redeliver');
    //#else
    expect(alert.textContent).toContain('then redeliver');
    //#endif
    expect(alert.compareDocumentPosition(sheet.querySelector('section')!)).toBe(
      Node.DOCUMENT_POSITION_FOLLOWING,
    );
    expect(sheet.querySelector('[hlmSheetHeader]')?.textContent).toMatch(/redeliver/i);
  });

  it('lists catalog jumps as rows with counts and links only where records exist', async () => {
    const catalog: DistributedEventCatalogOutputDto = {
      eventName: 'order.paid',
      outgoing: true,
      incoming: false,
      consumers: [],
      pendingOutbox: 0,
      pendingInbox: 0,
      deadLettered: 2,
      failedInbox: 0,
    };
    const sheet = await open(
      {},
      { recordId: catalog.eventName, view: 'catalog', catalogDetail: catalog },
    );
    const navigate = vi.fn();
    fixture.componentInstance.navigate.subscribe(navigate);
    const rows = Array.from(sheet.querySelectorAll('[data-testid=event-catalog-links] li'));
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('0');
    expect(rows[0].querySelector('button')).toBeNull();
    expect(rows[1].textContent).toContain('2');
    (rows[1].querySelector('button') as HTMLButtonElement).click();
    expect(navigate).toHaveBeenCalledWith({
      view: 'outbox',
      status: 'deadLettered',
      eventName: 'order.paid',
    });
  });

  it('confirms a payload copy on the button and a record ID copy from the menu with a toast', async () => {
    const write = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue(undefined);
    const notice = vi.spyOn(toast, 'success').mockReturnValue('copied');
    try {
      const detail: DistributedEventRejectionDetailOutputDto = {
        record: {
          id: '3',
          transport: 'RabbitMQ',
          coordinates: {},
          reason: 'invalid-envelope',
          error: 'Safe reason.',
          status: 'pending',
          redecodeCount: 0,
          receivedAt: '2026-11-09T00:00:00Z',
          lastModificationTime: '2026-11-09T00:00:00Z',
        },
        headers: {},
        coordinates: {},
      };
      const sheet = await open(
        { getRejection: () => of(detail), getRejectionPayload: () => of({ rawBody: 'AAEC' }) },
        { recordId: '3', view: 'rejections', canManage: true, payloadEnabled: true },
      );
      fixture.componentInstance.loadPayload();
      fixture.detectChanges();
      await fixture.whenStable();
      const copy = () =>
        sheet.querySelector('[data-testid=event-copy-payload]') as HTMLButtonElement;
      const icon = () => copy().querySelector('ng-icon')!.innerHTML;
      const copyIcon = icon();
      copy().click();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(write).toHaveBeenCalledWith('AAEC');
      //#if (IncludeLocalization)
      expect(copy().textContent?.trim()).toBe('distributedEvents.copied');
      //#else
      expect(copy().textContent?.trim()).toBe('Copied');
      //#endif
      // 图标换成对勾：渲染出的 SVG 与复制图标不同，且与 lucideCheck 一致
      expect(icon()).not.toBe(copyIcon);
      expect(icon()).toContain('M20 6');
      expect(fixture.componentInstance['payloadClipboard'].copied()).toBe(true);
      // 事件 ID 的复制图标不受载荷复制影响
      expect(fixture.componentInstance['clipboard'].copied()).toBe(false);

      (sheet.querySelector('[data-testid=event-detail-more]') as HTMLButtonElement).click();
      fixture.detectChanges();
      await fixture.whenStable();
      (
        document.querySelector('hlm-dropdown-menu [hlmDropdownMenuItem]') as HTMLButtonElement
      ).click();
      await fixture.whenStable();
      expect(write).toHaveBeenLastCalledWith('3');
      //#if (IncludeLocalization)
      expect(notice).toHaveBeenCalledWith('distributedEvents.copied');
      //#else
      expect(notice).toHaveBeenCalledWith('Copied');
      //#endif
    } finally {
      write.mockRestore();
      notice.mockRestore();
    }
  });

  it('does not report success when the clipboard is unavailable', async () => {
    const write = vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('denied'));
    const notice = vi.spyOn(toast, 'success').mockReturnValue('copied');
    try {
      const detail: DistributedEventRejectionDetailOutputDto = {
        record: {
          id: '3',
          transport: 'RabbitMQ',
          coordinates: {},
          reason: 'invalid-envelope',
          error: 'Safe reason.',
          status: 'pending',
          redecodeCount: 0,
          receivedAt: '2026-11-09T00:00:00Z',
          lastModificationTime: '2026-11-09T00:00:00Z',
        },
        headers: {},
        coordinates: {},
      };
      const sheet = await open(
        { getRejection: () => of(detail) },
        { recordId: '3', view: 'rejections', canManage: true },
      );
      (sheet.querySelector('[data-testid=event-detail-more]') as HTMLButtonElement).click();
      fixture.detectChanges();
      await fixture.whenStable();
      (
        document.querySelector('hlm-dropdown-menu [hlmDropdownMenuItem]') as HTMLButtonElement
      ).click();
      await fixture.whenStable();
      expect(write).toHaveBeenCalledWith('3');
      expect(notice).not.toHaveBeenCalled();
    } finally {
      write.mockRestore();
      notice.mockRestore();
    }
  });
});
