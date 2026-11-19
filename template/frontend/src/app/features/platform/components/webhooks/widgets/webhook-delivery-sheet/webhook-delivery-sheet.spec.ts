import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { WebhookDeliverySheet } from './webhook-delivery-sheet';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../core/i18n/transloco.testing';
//#endif
import { AccountPreferencesService } from '../../../../../../core/services/account-preferences-service';
import { WebhookDeliveryDetailOutputDto } from '../../../../dtos/webhook.dto';

const DELIVERY: WebhookDeliveryDetailOutputDto = {
  id: 'delivery-1',
  eventId: '5c1d2e3f-0000-4000-8000-000000000001',
  eventType: 'order.created',
  subscriptionId: 'sub-1',
  subscriptionName: 'ERP order sync',
  subscriptionUrl: 'https://erp.example.com/hooks/orders',
  status: 'failed',
  attemptCount: 2,
  lastStatusCode: 503,
  lastError: null,
  lastAttemptAt: '2026-11-07T10:00:00.000Z',
  creationTime: '2026-11-07T09:00:00.000Z',
  payload: '{"type":"order.created"}',
  attempts: [
    {
      id: 'attempt-2',
      attemptedAt: '2026-11-07T10:00:00.000Z',
      trigger: 'automatic',
      requestHeaders: { 'webhook-id': '5c1d2e3f-0000-4000-8000-000000000001' },
      statusCode: 503,
      durationMs: 120,
      response: 'unavailable',
    },
  ],
};

/** 断言用的文案：多语言形态下单测词条为空、回落成键名；不含本地化时是组件内的英文表。 */
const TEXT = {
  //#if (IncludeLocalization)
  overview: 'webhooks.sheet.overview',
  attempts: 'webhooks.sheet.attempts',
  request: 'webhooks.sheet.request',
  statusCode: 'webhooks.sheet.alert.statusCode',
  error: 'webhooks.sheet.alert.error',
  failedNext: 'webhooks.sheet.alert.failedNext',
  retryNext: 'webhooks.sheet.alert.retryNext',
  //#else
  overview: 'Overview',
  attempts: 'Attempts',
  request: 'Request',
  statusCode: 'The last attempt returned 503',
  error: 'Delivery error',
  failedNext: 'Automatic retries have ended.',
  retryNext: 'The next automatic attempt is at',
  //#endif
};

/** 投递详情抽屉的版式（AC15）：标题行带状态与操作、元信息行、未成功时的提示、分区顺序与 8rem 标签列。 */
describe('WebhookDeliverySheet', () => {
  let fixture: ComponentFixture<WebhookDeliverySheet>;

  const sheet = () =>
    document.querySelector<HTMLElement>('[data-testid="webhook-delivery-sheet"]')!;
  const alert = () => sheet().querySelector<HTMLElement>('[data-testid="webhook-delivery-alert"]');

  async function open(delivery: WebhookDeliveryDetailOutputDto, canManage = true): Promise<void> {
    fixture = TestBed.createComponent(WebhookDeliverySheet);
    fixture.componentRef.setInput('delivery', delivery);
    fixture.componentRef.setInput('canManage', canManage);
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    await fixture.whenStable();
    await vi.waitFor(() => expect(sheet()).not.toBeNull());
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WebhookDeliverySheet],
      providers: [
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
        {
          provide: AccountPreferencesService,
          useValue: { timeZone: signal('UTC'), displayLocale: signal('en') },
        },
      ],
    }).compileComponents();
  });

  afterEach(() => {
    fixture?.destroy();
    document.querySelector('.cdk-overlay-container')?.remove();
  });

  it('puts the status after the 16px title, the resend on its right and the metadata below', async () => {
    await open(DELIVERY);

    const title = sheet().querySelector<HTMLElement>('[hlmSheetTitle]')!;
    expect(title.textContent?.trim()).toBe('order.created');
    expect(title.classList).toContain('text-base');
    // 状态徽章紧跟标题，带状态点
    const status = title.nextElementSibling as HTMLElement;
    expect(status.dataset['testid']).toBe('webhook-delivery-sheet-status');
    expect(status.hasAttribute('appStatusDot')).toBe(true);

    // 重发与标题在同一行，位于右侧
    const titleRow = title.parentElement!.parentElement!.parentElement!;
    const retry = sheet().querySelector<HTMLElement>(
      '[data-testid="webhook-delivery-sheet-retry"]',
    )!;
    expect(titleRow.contains(retry)).toBe(true);
    expect(retry.getBoundingClientRect().left).toBeGreaterThan(
      status.getBoundingClientRect().right,
    );

    const meta = sheet().querySelector<HTMLElement>('[data-testid="webhook-delivery-sheet-meta"]')!;
    expect(meta.classList).toContain('text-xs');
    expect(meta.textContent).toContain('ERP order sync');
    expect(meta.textContent).toContain(DELIVERY.eventId);
    expect(meta.querySelector('button[aria-label]')).not.toBeNull();
  });

  it('orders the sections as overview, attempts and request with underlined headings', async () => {
    await open(DELIVERY);

    const headings = [...sheet().querySelectorAll<HTMLElement>('section > h3')];
    expect(headings.map((heading) => heading.textContent?.trim())).toEqual([
      TEXT.overview,
      TEXT.attempts,
      TEXT.request,
    ]);
    for (const heading of headings) {
      expect(heading.classList).toContain('border-b');
      expect(heading.classList).toContain('pb-3');
    }
    const overview = sheet().querySelector<HTMLElement>(
      '[data-testid="webhook-delivery-overview"]',
    )!;
    expect(overview.classList).toContain('grid-cols-[8rem_minmax(0,1fr)]');
  });

  it('tops a failed delivery with a destructive alert naming the status code and the next step', async () => {
    await open(DELIVERY);

    const box = alert()!;
    expect(box.getAttribute('data-slot')).toBe('alert');
    expect(box.classList).toContain('text-destructive');
    expect(box.querySelector('[hlmAlertTitle]')!.textContent?.trim()).toBe(TEXT.statusCode);
    expect(box.querySelector('[hlmAlertDescription]')!.textContent).toContain(TEXT.failedNext);
    // 提示在正文最前，概要之前
    const body = box.parentElement!;
    expect(body.firstElementChild).toBe(box);
  });

  it('explains a retrying delivery without a status code by its error and the next attempt', async () => {
    await open({
      ...DELIVERY,
      status: 'pending',
      lastStatusCode: null,
      lastError: 'Connection refused',
      nextAttemptAt: '2026-11-07T10:05:00.000Z',
      nextTrigger: 'automatic',
    });

    const box = alert()!;
    expect(box.classList).not.toContain('text-destructive');
    expect(box.querySelector('[hlmAlertTitle]')!.textContent?.trim()).toBe(TEXT.error);
    const description = box.querySelector('[hlmAlertDescription]')!.textContent;
    expect(description).toContain('Connection refused');
    expect(description).toContain(TEXT.retryNext);
    expect(
      sheet().querySelector<HTMLButtonElement>('[data-testid="webhook-delivery-sheet-retry"]')!
        .disabled,
    ).toBe(true);
  });

  it('shows no alert for a succeeded delivery and no resend without the manage permission', async () => {
    await open({ ...DELIVERY, status: 'succeeded', lastStatusCode: 200 }, false);

    expect(alert()).toBeNull();
    expect(sheet().querySelector('[data-testid="webhook-delivery-sheet-retry"]')).toBeNull();
  });
});
