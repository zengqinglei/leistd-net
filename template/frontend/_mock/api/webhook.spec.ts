import { HttpHeaders, HttpRequest } from '@angular/common/http';

import { getWebhookStatistics, WEBHOOK_API } from './webhook';
import {
  WebhookDeliveryOutputDto,
  WebhookSubscriptionOutputDto,
  WebhookSubscriptionSecretOutputDto,
} from '../../src/app/features/platform/dtos/webhook.dto';
import { PagedResultDto } from '../../src/app/shared/dtos/paged-result.dto';
import { MockException, MockRequest, MockResponse } from '../core/models';
import { WEBHOOK_DELIVERIES, WEBHOOK_SUBSCRIPTIONS } from '../data/webhooks';
import { setMockSessionUserId } from '../utils/current-user';

type Api = typeof WEBHOOK_API;

function request(overrides: Partial<MockRequest> = {}): MockRequest {
  return {
    original: new HttpRequest('GET', '/api/v1/webhooks'),
    url: '/api/v1/webhooks',
    headers: new HttpHeaders(),
    queryParams: {},
    body: null,
    params: {},
    ...overrides,
  };
}

function call<T>(route: keyof Api, overrides: Partial<MockRequest> = {}): T {
  return (WEBHOOK_API[route] as (req: MockRequest) => unknown)(request(overrides)) as T;
}

function rejectionOf(run: () => unknown): MockException {
  try {
    run();
  } catch (error) {
    expect(error).toBeInstanceOf(MockException);
    return error as MockException;
  }
  throw new Error('expected the handler to reject');
}

/**
 * Webhook Mock 与后端可观察行为一致：权限门、入参校验先于查找、停用终结待投递、重发只对终态开放、
 * 删除连带投递、统计在保留期外不给上一区间。
 */
describe('webhook mock', () => {
  let subscriptions: typeof WEBHOOK_SUBSCRIPTIONS;
  let deliveries: typeof WEBHOOK_DELIVERIES;
  const [erp, portal] = WEBHOOK_SUBSCRIPTIONS;

  beforeEach(() => {
    subscriptions = structuredClone(WEBHOOK_SUBSCRIPTIONS);
    deliveries = structuredClone(WEBHOOK_DELIVERIES);
    setMockSessionUserId('user_admin');
  });

  afterEach(() => {
    WEBHOOK_SUBSCRIPTIONS.splice(0, WEBHOOK_SUBSCRIPTIONS.length, ...subscriptions);
    WEBHOOK_DELIVERIES.splice(0, WEBHOOK_DELIVERIES.length, ...deliveries);
    setMockSessionUserId(null);
  });

  it('requires the read permission for the list', () => {
    setMockSessionUserId('user_demo');

    expect(rejectionOf(() => call('GET /api/v1/webhooks/subscriptions')).status).toBe(403);
  });

  it('reports every invalid field at once without a business code', () => {
    const error = rejectionOf(() =>
      call('POST /api/v1/webhooks/subscriptions', {
        body: { name: ' ', url: 'ftp://example.com', eventTypes: [], headers: {} },
      }),
    );

    expect(error.status).toBe(400);
    expect(error.error.code).toBeUndefined();
    expect(error.error.errors.map((item: { field: string }) => item.field)).toEqual([
      'name',
      'url',
      'eventTypes',
    ]);
  });

  it('rejects custom headers that override component headers', () => {
    const error = rejectionOf(() =>
      call('POST /api/v1/webhooks/subscriptions', {
        body: {
          name: 'Spec',
          url: 'https://spec.example.test/hook',
          eventTypes: ['*'],
          headers: { 'Webhook-Id': 'forged' },
        },
      }),
    );

    expect(error.error.errors[0].field).toBe('headers');
  });

  it('rejects the test event, "*" mixed with other types, and malformed or duplicate headers', () => {
    const fieldOf = (body: Record<string, unknown>) =>
      rejectionOf(() =>
        call('POST /api/v1/webhooks/subscriptions', {
          body: { name: 'Spec', url: 'https://spec.example.test/hook', eventTypes: ['*'], ...body },
        }),
      ).error.errors[0].field;

    expect(fieldOf({ eventTypes: ['webhook.test'] })).toBe('eventTypes');
    expect(fieldOf({ eventTypes: ['*', 'webhook.test'] })).toBe('eventTypes');
    expect(fieldOf({ headers: { 'User-Agent': 'forged' } })).toBe('headers');
    expect(fieldOf({ headers: { 'X Tenant': 'a' } })).toBe('headers');
    expect(fieldOf({ headers: { 'X-Tenant': 'a\nb' } })).toBe('headers');
    expect(fieldOf({ headers: { 'X-Tenant': 'a', 'x-tenant': 'b' } })).toBe('headers');
  });

  it('does not list the built-in test event among the subscribable event types', () => {
    const types = call<{ name: string }[]>('GET /api/v1/webhooks/event-types');

    expect(types.map((type) => type.name)).not.toContain('webhook.test');
  });

  it('matches an event id only in full and event types case-insensitively', () => {
    const sample = WEBHOOK_DELIVERIES[0];
    const search = (keyword: string) =>
      call<PagedResultDto<WebhookDeliveryOutputDto>>('GET /api/v1/webhooks/deliveries', {
        queryParams: { keyword, limit: 1000 },
      }).items.map((item) => item.id);

    expect(search(sample.eventId.toUpperCase())).toEqual([sample.id]);
    expect(search(sample.eventId.slice(0, 8))).not.toContain(sample.id);
    expect(search(sample.eventType.toUpperCase())).toContain(sample.id);
  });

  it('returns the plain secret only in the create response', () => {
    const created = call<WebhookSubscriptionSecretOutputDto>(
      'POST /api/v1/webhooks/subscriptions',
      {
        body: { name: 'Spec', url: 'https://spec.example.test/hook', eventTypes: ['*'] },
      },
    );
    const listed = call<PagedResultDto<WebhookSubscriptionOutputDto>>(
      'GET /api/v1/webhooks/subscriptions',
      { queryParams: { limit: 100 } },
    ).items.find((item) => item.id === created.subscription.id)!;

    expect(created.secret).toMatch(/^whsec_/);
    expect(listed.secretPrefix).toBe(created.secret.slice(0, 10));
    expect(JSON.stringify(listed)).not.toContain(created.secret);
  });

  it('matches subscriptions to all events when filtering by an event type', () => {
    const page = call<PagedResultDto<WebhookSubscriptionOutputDto>>(
      'GET /api/v1/webhooks/subscriptions',
      { queryParams: { eventType: 'webhook.test', limit: 100 } },
    );

    expect(page.items.map((item) => item.id)).toContain(erp.id);
  });

  it('marks pending deliveries as failed when a subscription is disabled', () => {
    const pending = WEBHOOK_DELIVERIES.filter(
      (item) => item.subscriptionId === erp.id && item.status === 'pending',
    );
    expect(pending.length).toBeGreaterThan(0);

    call('POST /api/v1/webhooks/subscriptions/:id/disable', { params: { id: erp.id } });

    expect(pending.every((item) => item.status === 'failed' && !item.nextAttemptAt)).toBe(true);
  });

  it('refuses a test event to a disabled subscription', () => {
    const error = rejectionOf(() =>
      call('POST /api/v1/webhooks/subscriptions/:id/test', { params: { id: portal.id } }),
    );

    expect(error.status).toBe(409);
    expect(error.error.code).toBe('Webhook:SubscriptionDisabled');
  });

  it('delivers a test event and lists it first in the delivery log', () => {
    // 投递 DTO 带字符串 status，必须显式包成 200 响应，否则拦截器把它当 MockResponse、正文为 null
    const response = call<MockResponse>('POST /api/v1/webhooks/subscriptions/:id/test', {
      params: { id: erp.id },
    });
    const delivery = response.body as WebhookDeliveryOutputDto;
    const page = call<PagedResultDto<WebhookDeliveryOutputDto>>('GET /api/v1/webhooks/deliveries', {
      queryParams: { subscriptionId: erp.id, limit: 1 },
    });

    expect(response.status).toBe(200);
    expect(delivery.eventType).toBe('webhook.test');
    expect(delivery.status).toBe('succeeded');
    expect(page.items[0].id).toBe(delivery.id);
  });

  it('only resends deliveries in a terminal state', () => {
    const pending = WEBHOOK_DELIVERIES.find(
      (item) => item.subscriptionId === erp.id && item.status === 'pending',
    )!;

    const error = rejectionOf(() =>
      call('POST /api/v1/webhooks/deliveries/:id/retry', { params: { id: pending.id } }),
    );

    expect(error.error.code).toBe('Webhook:DeliveryNotRetryable');
  });

  it('records a manual attempt and keeps the event id when resending', () => {
    const failed = WEBHOOK_DELIVERIES.find(
      (item) => item.subscriptionId === erp.id && item.status === 'failed',
    )!;
    const eventId = failed.eventId;

    call('POST /api/v1/webhooks/deliveries/:id/retry', { params: { id: failed.id } });

    expect(failed.eventId).toBe(eventId);
    expect(failed.attempts[0].trigger).toBe('manual');
  });

  it('resends failed deliveries when enabling with retryFailed', () => {
    call('POST /api/v1/webhooks/subscriptions/:id/enable', {
      params: { id: portal.id },
      queryParams: { retryFailed: 'true' },
    });

    expect(
      WEBHOOK_DELIVERIES.some(
        (item) => item.subscriptionId === portal.id && item.status === 'failed',
      ),
    ).toBe(false);
    expect(WEBHOOK_SUBSCRIPTIONS.find((item) => item.id === portal.id)!.disabledReason).toBeNull();
  });

  it('deletes the delivery log together with the subscription', () => {
    call('DELETE /api/v1/webhooks/subscriptions/:id', { params: { id: erp.id } });

    expect(WEBHOOK_DELIVERIES.some((item) => item.subscriptionId === erp.id)).toBe(false);
  });

  it('counts deliveries by status and scopes the statistics to one subscription', () => {
    const now = Date.now();
    const today = new Date(now).toISOString().slice(0, 10);
    const all = getWebhookStatistics({ startDate: today, endDate: today, timeZone: 'UTC' }, now);
    const one = getWebhookStatistics(
      { startDate: today, endDate: today, timeZone: 'UTC', subscriptionId: portal.id },
      now,
    );

    expect(all.deliveries.series!.reduce((sum, value) => sum + value, 0)).toBe(
      all.deliveries.value,
    );
    expect(all.succeeded.value + all.failed.value).toBeLessThanOrEqual(all.deliveries.value);
    // 自动停用的订阅三天前就不再有新投递
    expect(one.deliveries.value).toBe(0);
    expect(one.disabledSubscriptions.value).toBe(1);
  });

  it('gives no previous range once it starts before the retention window', () => {
    const now = Date.now();
    const day = (offset: number) => new Date(now + offset * 86_400_000).toISOString().slice(0, 10);

    const recent = getWebhookStatistics(
      { startDate: day(-29), endDate: day(0), timeZone: 'UTC' },
      now,
    );
    const old = getWebhookStatistics(
      { startDate: day(-40), endDate: day(0), timeZone: 'UTC' },
      now,
    );

    expect(recent.deliveries.previous).not.toBeNull();
    expect(old.deliveries.previous).toBeNull();
    expect(old.successRate.previous).toBeNull();
  });
});
