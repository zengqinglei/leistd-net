import { requirePermission } from './authorization';
import {
  CreateWebhookSubscriptionInputDto,
  UpdateWebhookSubscriptionInputDto,
  WebhookDeliveryDetailOutputDto,
  WebhookDeliveryOutputDto,
  WebhookStatisticsOutputDto,
  WebhookSubscriptionOutputDto,
  WebhookSubscriptionSecretOutputDto,
} from '../../src/app/features/platform/dtos/webhook.dto';
import { PERMISSIONS } from '../../src/app/shared/constants/permission.constants';
import { PagedResultDto } from '../../src/app/shared/dtos/paged-result.dto';
import { MockException, MockRequest, MockResponse } from '../core/models';
import {
  MockWebhookDelivery,
  MockWebhookSubscription,
  WEBHOOK_DELIVERIES,
  WEBHOOK_EVENT_TYPES,
  WEBHOOK_RETENTION_DAYS,
  WEBHOOK_SUBSCRIPTIONS,
  WEBHOOK_TEST_EVENT,
  mockAttemptHeaders,
  mockPayload,
  mockWebhookSecret,
  nextMockWebhookId,
} from '../data/webhooks';
import { isUsableTimeZone, resolvePeriod, todayIn } from '../utils/statistics-calendar';

/** 订阅全部事件（含以后新增的）的取值。 */
const ALL_EVENTS = '*';
/** 与后端入参常量一致（`CreateWebhookSubscriptionInputDto`）。 */
const MAX_NAME_LENGTH = 128;
const MAX_DESCRIPTION_LENGTH = 512;
const MAX_URL_LENGTH = 2048;
const MAX_HEADER_COUNT = 20;
/** 由组件写入、不能被自定义头覆盖的头（与后端同一集合，大小写不敏感）。 */
const RESERVED_HEADERS = /^(webhook-.*|content-type|content-length|host|user-agent)$/i;
/** 头名须是 HTTP 令牌（RFC 9110 §5.6.2）。 */
const HEADER_TOKEN = /^[!#$%&'*+\-.^_`|~0-9A-Za-z]+$/;
/** 完整 Guid：投递关键字只在这种形态下按事件 id 精确匹配。 */
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
/** 轮换后旧密钥继续签名的时长（`Leistd:Webhooks:SecretRotationOverlap` 默认值）。 */
const ROTATION_OVERLAP_MS = 24 * 3_600_000;
const SEVEN_DAYS_MS = 7 * 24 * 3_600_000;

function query(req: MockRequest, name: string): string | undefined {
  const raw = req.queryParams[name];
  const value = Array.isArray(raw) ? raw[0] : raw;
  return value === undefined || value === null || value === '' ? undefined : String(value);
}

function notFound(code: 'Webhook:SubscriptionNotFound' | 'Webhook:DeliveryNotFound') {
  return new MockException(404, {
    code,
    message:
      code === 'Webhook:SubscriptionNotFound'
        ? 'The webhook subscription does not exist.'
        : 'The webhook delivery does not exist.',
  });
}

function findSubscription(id: string): MockWebhookSubscription {
  const subscription = WEBHOOK_SUBSCRIPTIONS.find((item) => item.id === id);
  if (!subscription) {
    throw notFound('Webhook:SubscriptionNotFound');
  }
  return subscription;
}

function findDelivery(id: string): MockWebhookDelivery {
  const delivery = WEBHOOK_DELIVERIES.find((item) => item.id === id);
  if (!delivery) {
    throw notFound('Webhook:DeliveryNotFound');
  }
  return delivery;
}

function subscriptionOutput(
  subscription: MockWebhookSubscription,
  now = Date.now(),
): WebhookSubscriptionOutputDto {
  const { secret, ...rest } = subscription;
  const own = WEBHOOK_DELIVERIES.filter((item) => item.subscriptionId === subscription.id);
  const last = own[0];
  // 近 7 天成功率：最近 7×24 小时内创建的投递，成功 ÷ 终态，没有终态时为空
  const recent = own.filter((item) => now - Date.parse(item.creationTime) < SEVEN_DAYS_MS);
  const succeeded = recent.filter((item) => item.status === 'succeeded').length;
  const terminal = recent.filter((item) => item.status !== 'pending').length;
  return {
    ...rest,
    secretPrefix: secret.slice(0, 'whsec_'.length + 4),
    lastDelivery: last
      ? {
          id: last.id,
          status: last.status,
          lastStatusCode: last.lastStatusCode,
          creationTime: last.creationTime,
        }
      : null,
    successRateLast7Days: terminal > 0 ? succeeded / terminal : null,
  };
}

/**
 * 投递 DTO 自带字符串 `status`，Mock 拦截器会把带 `status` 的返回值当成 `MockResponse`，正文变成 null；
 * 显式包一层，让它按 200 正文返回。
 */
function deliveryResponse<T>(body: T): MockResponse {
  return { status: 200, body };
}

function deliveryOutput(delivery: MockWebhookDelivery): WebhookDeliveryOutputDto {
  const { payload: _payload, attempts: _attempts, ...rest } = delivery;
  const subscription = WEBHOOK_SUBSCRIPTIONS.find((item) => item.id === delivery.subscriptionId);
  return {
    ...rest,
    subscriptionName: subscription?.name ?? '',
    subscriptionUrl: subscription?.url ?? '',
  };
}

function deliveryDetail(delivery: MockWebhookDelivery): WebhookDeliveryDetailOutputDto {
  return {
    ...deliveryOutput(delivery),
    payload: delivery.payload,
    attempts: delivery.attempts,
  };
}

function page<T>(items: T[], req: MockRequest): PagedResultDto<T> {
  const offset = Number(query(req, 'offset') ?? 0);
  const limit = Number(query(req, 'limit') ?? 10);
  return { totalCount: items.length, items: items.slice(offset, offset + limit) };
}

function isControlChar(char: string): boolean {
  const code = char.charCodeAt(0);
  return code < 0x20 || (code >= 0x7f && code <= 0x9f);
}

function isAbsoluteHttpUrl(value: string): boolean {
  try {
    const url = new URL(value);
    return (url.protocol === 'http:' || url.protocol === 'https:') && !/\s/.test(value);
  } catch {
    return false;
  }
}

// 与后端一致：入参校验先于查找，失败是 400 字段错误、不带业务码，一次报全
function validateInput(
  input: CreateWebhookSubscriptionInputDto | UpdateWebhookSubscriptionInputDto,
) {
  const errors: { field: string; detail: string }[] = [];
  const name = input.name?.trim() ?? '';
  if (!name) {
    errors.push({ field: 'name', detail: 'The Name field is required.' });
  } else if (name.length > MAX_NAME_LENGTH) {
    errors.push({ field: 'name', detail: `Name must not exceed ${MAX_NAME_LENGTH} characters.` });
  }
  if ((input.description ?? '').length > MAX_DESCRIPTION_LENGTH) {
    errors.push({
      field: 'description',
      detail: `Description must not exceed ${MAX_DESCRIPTION_LENGTH} characters.`,
    });
  }
  if (!input.url || input.url.length > MAX_URL_LENGTH || !isAbsoluteHttpUrl(input.url)) {
    errors.push({ field: 'url', detail: 'The URL must be an absolute http or https URL.' });
  }
  const declared = WEBHOOK_EVENT_TYPES.map((item) => item.name);
  if (!input.eventTypes?.length) {
    errors.push({ field: 'eventTypes', detail: 'Select at least one event type.' });
  } else if (input.eventTypes.some((type) => type !== ALL_EVENTS && !declared.includes(type))) {
    // 内置测试事件不在登记列表里：与后端一样不能订阅
    errors.push({ field: 'eventTypes', detail: 'The event type is not registered.' });
  } else if (input.eventTypes.includes(ALL_EVENTS) && new Set(input.eventTypes).size > 1) {
    errors.push({
      field: 'eventTypes',
      detail: "'*' subscribes to all event types and cannot be combined with other event types.",
    });
  }
  const headers = Object.entries(input.headers ?? {});
  const names = headers.map(([name]) => name.toLowerCase());
  if (headers.length > MAX_HEADER_COUNT) {
    errors.push({
      field: 'headers',
      detail: `No more than ${MAX_HEADER_COUNT} custom headers are allowed.`,
    });
  } else if (headers.some(([name]) => !HEADER_TOKEN.test(name))) {
    errors.push({ field: 'headers', detail: 'The header name is not a valid HTTP token.' });
  } else if (headers.some(([name]) => RESERVED_HEADERS.test(name))) {
    errors.push({
      field: 'headers',
      detail: 'Custom headers cannot override webhook-* or Content-Type headers.',
    });
  } else if (headers.some(([, value]) => [...(value ?? '')].some(isControlChar))) {
    errors.push({ field: 'headers', detail: 'Header values cannot contain control characters.' });
  } else if (new Set(names).size !== names.length) {
    errors.push({ field: 'headers', detail: 'A header is specified more than once.' });
  }
  if (errors.length > 0) {
    throw new MockException(400, { errors });
  }
}

function disabledConflict() {
  return new MockException(409, {
    code: 'Webhook:SubscriptionDisabled',
    message: 'The webhook subscription is disabled.',
  });
}

/**
 * Mock 没有真实的接收方：测试事件与手动重发立即记一次成功的尝试，与后端"提交后立即入队发送"的可观察结果一致。
 */
function sendNow(delivery: MockWebhookDelivery, trigger: 'automatic' | 'manual', now: number) {
  const subscription = findSubscription(delivery.subscriptionId);
  const attempt = {
    id: nextMockWebhookId(),
    attemptedAt: new Date(now).toISOString(),
    trigger,
    requestHeaders: mockAttemptHeaders(delivery.eventId, now, subscription.headers),
    statusCode: 200,
    durationMs: 96,
    response: '{"received":true}',
    error: null,
  };
  delivery.attempts = [attempt, ...delivery.attempts];
  delivery.status = 'succeeded';
  delivery.attemptCount += 1;
  delivery.lastStatusCode = 200;
  delivery.lastError = null;
  delivery.lastAttemptAt = attempt.attemptedAt;
  delivery.nextAttemptAt = null;
  delivery.nextTrigger = null;
  subscription.firstFailureAt = null;
}

function getSubscriptions(req: MockRequest) {
  requirePermission(PERMISSIONS.webhooks.default);
  const keyword = query(req, 'keyword')?.toLowerCase();
  const isEnabled = query(req, 'isEnabled');
  const eventType = query(req, 'eventType');
  const items = [...WEBHOOK_SUBSCRIPTIONS]
    .filter(
      (item) =>
        !keyword ||
        item.name.toLowerCase().includes(keyword) ||
        item.url.toLowerCase().includes(keyword),
    )
    .filter((item) => isEnabled === undefined || String(item.isEnabled) === isEnabled)
    .filter(
      (item) =>
        !eventType || item.eventTypes.includes(eventType) || item.eventTypes.includes(ALL_EVENTS),
    )
    .sort((a, b) => Date.parse(b.creationTime) - Date.parse(a.creationTime));
  const now = Date.now();
  return page(
    items.map((item) => subscriptionOutput(item, now)),
    req,
  );
}

function createSubscription(req: MockRequest): WebhookSubscriptionSecretOutputDto {
  requirePermission(PERMISSIONS.webhooks.manage);
  const body = req.body as CreateWebhookSubscriptionInputDto;
  validateInput(body);
  const subscription: MockWebhookSubscription = {
    id: crypto.randomUUID(),
    name: body.name.trim(),
    description: body.description?.trim() || null,
    url: body.url,
    eventTypes: [...body.eventTypes],
    headers: { ...(body.headers ?? {}) },
    secret: mockWebhookSecret(),
    previousSecretExpiresAt: null,
    isEnabled: true,
    disabledReason: null,
    disabledAt: null,
    firstFailureAt: null,
    creationTime: new Date().toISOString(),
  };
  WEBHOOK_SUBSCRIPTIONS.unshift(subscription);
  return { subscription: subscriptionOutput(subscription), secret: subscription.secret };
}

function updateSubscription(req: MockRequest) {
  requirePermission(PERMISSIONS.webhooks.manage);
  const body = req.body as UpdateWebhookSubscriptionInputDto;
  validateInput(body);
  const subscription = findSubscription(req.params['id']);
  Object.assign(subscription, {
    name: body.name.trim(),
    description: body.description?.trim() || null,
    url: body.url,
    eventTypes: [...body.eventTypes],
    headers: { ...(body.headers ?? {}) },
  });
  return subscriptionOutput(subscription);
}

function deleteSubscription(req: MockRequest) {
  requirePermission(PERMISSIONS.webhooks.manage);
  const id = req.params['id'];
  findSubscription(id);
  WEBHOOK_SUBSCRIPTIONS.splice(
    WEBHOOK_SUBSCRIPTIONS.findIndex((item) => item.id === id),
    1,
  );
  // 一并删除它的投递与尝试，它们随之退出统计
  for (let index = WEBHOOK_DELIVERIES.length - 1; index >= 0; index -= 1) {
    if (WEBHOOK_DELIVERIES[index].subscriptionId === id) {
      WEBHOOK_DELIVERIES.splice(index, 1);
    }
  }
  return null;
}

function enableSubscription(req: MockRequest) {
  requirePermission(PERMISSIONS.webhooks.manage);
  const subscription = findSubscription(req.params['id']);
  if (!subscription.isEnabled) {
    Object.assign(subscription, {
      isEnabled: true,
      disabledReason: null,
      disabledAt: null,
      firstFailureAt: null,
    });
  }
  if (query(req, 'retryFailed') === 'true') {
    const now = Date.now();
    WEBHOOK_DELIVERIES.filter(
      (item) => item.subscriptionId === subscription.id && item.status === 'failed',
    ).forEach((item) => sendNow(item, 'manual', now));
  }
  return subscriptionOutput(subscription);
}

function disableSubscription(req: MockRequest) {
  requirePermission(PERMISSIONS.webhooks.manage);
  const subscription = findSubscription(req.params['id']);
  if (subscription.isEnabled) {
    Object.assign(subscription, {
      isEnabled: false,
      disabledReason: 'manual',
      disabledAt: new Date().toISOString(),
    });
    // 同一事务里把待投递的记录改为失败，写明原因
    for (const item of WEBHOOK_DELIVERIES) {
      if (item.subscriptionId === subscription.id && item.status === 'pending') {
        Object.assign(item, {
          status: 'failed',
          lastError: 'The subscription was disabled.',
          nextAttemptAt: null,
          nextTrigger: null,
        });
      }
    }
  }
  return subscriptionOutput(subscription);
}

function rotateSecret(req: MockRequest): WebhookSubscriptionSecretOutputDto {
  requirePermission(PERMISSIONS.webhooks.manage);
  const subscription = findSubscription(req.params['id']);
  subscription.secret = mockWebhookSecret();
  subscription.previousSecretExpiresAt = new Date(Date.now() + ROTATION_OVERLAP_MS).toISOString();
  return { subscription: subscriptionOutput(subscription), secret: subscription.secret };
}

function sendTestEvent(req: MockRequest) {
  requirePermission(PERMISSIONS.webhooks.manage);
  const subscription = findSubscription(req.params['id']);
  if (!subscription.isEnabled) {
    throw disabledConflict();
  }
  const now = Date.now();
  const delivery: MockWebhookDelivery = {
    id: nextMockWebhookId(),
    eventId: nextMockWebhookId(),
    eventType: WEBHOOK_TEST_EVENT,
    subscriptionId: subscription.id,
    status: 'pending',
    attemptCount: 0,
    lastStatusCode: null,
    lastError: null,
    lastAttemptAt: null,
    nextAttemptAt: new Date(now).toISOString(),
    nextTrigger: 'automatic',
    creationTime: new Date(now).toISOString(),
    payload: mockPayload(WEBHOOK_TEST_EVENT, now, { subscriptionId: subscription.id }),
    attempts: [],
  };
  sendNow(delivery, 'automatic', now);
  WEBHOOK_DELIVERIES.unshift(delivery);
  return deliveryResponse(deliveryOutput(delivery));
}

function getDeliveries(req: MockRequest) {
  requirePermission(PERMISSIONS.webhooks.default);
  const keyword = query(req, 'keyword')?.trim().toLowerCase();
  const subscriptionId = query(req, 'subscriptionId');
  const status = query(req, 'status');
  const eventType = query(req, 'eventType');
  // 与后端一致：完整 Guid 才按事件 id 精确匹配，类型名不区分大小写包含匹配
  const items = WEBHOOK_DELIVERIES.filter(
    (item) =>
      !keyword ||
      (GUID.test(keyword) && item.eventId.toLowerCase() === keyword) ||
      item.eventType.toLowerCase().includes(keyword),
  )
    .filter((item) => !subscriptionId || item.subscriptionId === subscriptionId)
    .filter((item) => !status || item.status === status)
    .filter((item) => !eventType || item.eventType === eventType)
    .sort((a, b) => Date.parse(b.creationTime) - Date.parse(a.creationTime));
  return page(items.map(deliveryOutput), req);
}

function retryDelivery(req: MockRequest) {
  requirePermission(PERMISSIONS.webhooks.manage);
  const delivery = findDelivery(req.params['id']);
  if (delivery.status === 'pending') {
    throw new MockException(409, {
      code: 'Webhook:DeliveryNotRetryable',
      message: 'Only succeeded or failed deliveries can be retried.',
    });
  }
  if (!findSubscription(delivery.subscriptionId).isEnabled) {
    throw disabledConflict();
  }
  sendNow(delivery, 'manual', Date.now());
  return deliveryResponse(deliveryOutput(delivery));
}

/**
 * 投递统计：与平台统计同一套日历区间与分桶（见 utils/statistics-calendar.ts）；按投递创建时刻分桶、按当前状态计数。
 * 上一区间起点早于保留期下限时上一区间为空；重试中与已停用订阅是当前存量。
 */
export function getWebhookStatistics(
  params: Record<string, unknown>,
  now: number = Date.now(),
): WebhookStatisticsOutputDto {
  requirePermission(PERMISSIONS.webhooks.default);
  const text = (name: string) => {
    const value = Array.isArray(params[name]) ? (params[name] as unknown[])[0] : params[name];
    return typeof value === 'string' ? value : '';
  };
  const isDate = (value: string) => /^\d{4}-\d{2}-\d{2}$/.test(value);
  const errors: { field: string; detail: string }[] = [];
  for (const field of ['startDate', 'endDate'] as const) {
    if (!isDate(text(field))) {
      errors.push({ field, detail: `${field} is not a valid date.` });
    }
  }
  if (!text('timeZone') || !isUsableTimeZone(text('timeZone'))) {
    errors.push({ field: 'timeZone', detail: 'The time zone is not a valid IANA time zone.' });
  }
  if (errors.length === 0 && text('endDate') > todayIn(text('timeZone'), now)) {
    errors.push({ field: 'endDate', detail: 'The end date cannot be later than today.' });
  }
  if (errors.length === 0 && text('startDate') > text('endDate')) {
    errors.push({
      field: 'endDate',
      detail: 'The end date cannot be earlier than the start date.',
    });
  }
  if (errors.length > 0) {
    throw new MockException(400, { errors });
  }

  const timeZone = text('timeZone');
  const subscriptionId = text('subscriptionId');
  const period = resolvePeriod(text('startDate'), text('endDate'), timeZone, now);
  const buckets = period.edges.slice(0, -1);
  const scoped = WEBHOOK_DELIVERIES.filter(
    (item) => !subscriptionId || item.subscriptionId === subscriptionId,
  );
  const count = (from: number, to: number, status?: MockWebhookDelivery['status']) =>
    scoped.filter((item) => {
      const time = Date.parse(item.creationTime);
      return time >= from && time < to && (!status || item.status === status);
    }).length;
  const rate = (from: number, to: number) => {
    const succeeded = count(from, to, 'succeeded');
    const terminal = succeeded + count(from, to, 'failed');
    return terminal > 0 ? succeeded / terminal : null;
  };
  const series = <T>(pick: (from: number, to: number) => T) =>
    buckets.map((from, index) => pick(from, period.edges[index + 1]));
  // 上一区间超出保留期时没有可比数据
  const comparable = period.previousStart >= now - WEBHOOK_RETENTION_DAYS * 24 * 3_600_000;
  const metric = (status?: MockWebhookDelivery['status']) => ({
    value: count(period.start, period.end, status),
    previous: comparable ? count(period.previousStart, period.previousEnd, status) : null,
    series: series((from, to) => count(from, to, status)),
  });

  return {
    range: {
      startDate: text('startDate'),
      endDate: text('endDate'),
      timeZone,
      granularity: period.granularity,
      start: new Date(period.start).toISOString(),
      end: new Date(period.end).toISOString(),
      previousStart: new Date(period.previousStart).toISOString(),
      previousEnd: new Date(period.previousEnd).toISOString(),
    },
    buckets: buckets.map((edge) => new Date(edge).toISOString()),
    successRate: {
      value: rate(period.start, period.end),
      previous: comparable ? rate(period.previousStart, period.previousEnd) : null,
      series: series(rate),
    },
    deliveries: metric(),
    succeeded: metric('succeeded'),
    failed: metric('failed'),
    retrying: { value: scoped.filter((item) => item.status === 'pending').length },
    disabledSubscriptions: {
      value: WEBHOOK_SUBSCRIPTIONS.filter(
        (item) => !item.isEnabled && (!subscriptionId || item.id === subscriptionId),
      ).length,
    },
  };
}

export const WEBHOOK_API = {
  'GET /api/v1/webhooks/event-types': () => {
    requirePermission(PERMISSIONS.webhooks.default);
    return WEBHOOK_EVENT_TYPES;
  },
  'GET /api/v1/webhooks/statistics': (req: MockRequest) => getWebhookStatistics(req.queryParams),
  'GET /api/v1/webhooks/subscriptions': (req: MockRequest) => getSubscriptions(req),
  'GET /api/v1/webhooks/subscriptions/:id': (req: MockRequest) => {
    requirePermission(PERMISSIONS.webhooks.default);
    return subscriptionOutput(findSubscription(req.params['id']));
  },
  'POST /api/v1/webhooks/subscriptions': (req: MockRequest) => createSubscription(req),
  'PUT /api/v1/webhooks/subscriptions/:id': (req: MockRequest) => updateSubscription(req),
  'DELETE /api/v1/webhooks/subscriptions/:id': (req: MockRequest) => deleteSubscription(req),
  'POST /api/v1/webhooks/subscriptions/:id/enable': (req: MockRequest) => enableSubscription(req),
  'POST /api/v1/webhooks/subscriptions/:id/disable': (req: MockRequest) => disableSubscription(req),
  'POST /api/v1/webhooks/subscriptions/:id/rotate-secret': (req: MockRequest) => rotateSecret(req),
  'POST /api/v1/webhooks/subscriptions/:id/test': (req: MockRequest) => sendTestEvent(req),
  'GET /api/v1/webhooks/deliveries': (req: MockRequest) => getDeliveries(req),
  'GET /api/v1/webhooks/deliveries/:id': (req: MockRequest) => {
    requirePermission(PERMISSIONS.webhooks.default);
    return deliveryResponse(deliveryDetail(findDelivery(req.params['id'])));
  },
  'POST /api/v1/webhooks/deliveries/:id/retry': (req: MockRequest) => retryDelivery(req),
};
