import {
  WebhookAttemptOutputDto,
  WebhookDeliveryDetailOutputDto,
  WebhookEventTypeOutputDto,
  WebhookSubscriptionOutputDto,
} from '../../src/app/features/platform/dtos/webhook.dto';

/**
 * 出站 Webhook 样本：一个正常投递的订阅、一个连续失败后被自动停用的订阅、一个手动停用的订阅，
 * 投递覆盖近 40 天（含失败与重试中），统计卡、环比与迷你趋势都有数据可看。时刻相对页面加载时刻生成，
 * 投递结果按序号取固定规律，截图与用例都可复现。
 */
export const WEBHOOK_TEST_EVENT = 'webhook.test';

/** 与后端登记一致：组件内置测试事件，本地身份另登记示例事件 `user.created`。 */
export const WEBHOOK_EVENT_TYPES: WebhookEventTypeOutputDto[] = [
  { name: WEBHOOK_TEST_EVENT, group: 'webhooks', description: 'Webhook:EventType:webhook.test' },
  //#if (LocalIdentity)
  { name: 'user.created', group: 'users', description: 'Webhook:EventType:user.created' },
  //#endif
];

/** 样本投递用的业务事件；没有本地身份时只有测试事件可发。 */
//#if (LocalIdentity)
const SAMPLE_EVENT = 'user.created';
//#else
const SAMPLE_EVENT = WEBHOOK_TEST_EVENT;
//#endif

/** 保留期：与组件默认值一致（`Leistd:Webhooks:Retention:Days`）。 */
export const WEBHOOK_RETENTION_DAYS = 62;

/** 存储形态：密钥明文只在 Mock 内部，输出时只给前缀；最近投递与成功率由处理器按投递现算。 */
export interface MockWebhookSubscription extends Omit<
  WebhookSubscriptionOutputDto,
  'secretPrefix' | 'lastDelivery' | 'successRateLast7Days'
> {
  secret: string;
}

/** 存储形态：订阅名称与地址在输出时按订阅现取，订阅改名后投递日志随之更新。 */
export type MockWebhookDelivery = Omit<
  WebhookDeliveryDetailOutputDto,
  'subscriptionName' | 'subscriptionUrl'
>;

const HOUR = 3_600_000;
const DAY = 24 * HOUR;
const seededAt = Date.now();
const iso = (time: number) => new Date(time).toISOString();

let sequence = 0;
/** 确定性的 GUID 形态 id。 */
export function nextMockWebhookId(): string {
  sequence += 1;
  return `00000000-0000-4000-8000-${sequence.toString(16).padStart(12, '0')}`;
}

export function mockWebhookSecret(): string {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
  const bytes = crypto.getRandomValues(new Uint8Array(32));
  return `whsec_${Array.from(bytes, (byte) => alphabet[byte % alphabet.length]).join('')}`;
}

export const WEBHOOK_SUBSCRIPTIONS: MockWebhookSubscription[] = [
  {
    id: '6b1f3c2a-0d4e-4f7a-9c1b-2a3d4e5f6a01',
    name: 'ERP order sync',
    description: 'Pushes account changes to the ERP integration queue.',
    url: 'https://erp.example.com/hooks/leistd',
    eventTypes: ['*'],
    headers: {},
    secret: 'whsec_8KfDq2Lr6ZxT0bW4nV9yE3sH7jM1pA5c',
    previousSecretExpiresAt: null,
    isEnabled: true,
    disabledReason: null,
    disabledAt: null,
    firstFailureAt: null,
    creationTime: iso(seededAt - 45 * DAY),
  },
  {
    id: '6b1f3c2a-0d4e-4f7a-9c1b-2a3d4e5f6a02',
    name: 'Customer portal provisioning',
    description: 'Provisions portal accounts for new users. Owner: customer success operations.',
    url: 'https://portal.example.com/api/webhooks/users',
    eventTypes: [SAMPLE_EVENT],
    headers: { 'X-Tenant-Code': 'acme' },
    secret: 'whsec_Qm7Vt3Xc9Bn1Lk5Hs8Dp2Fw6Gz4Jr0Ya',
    previousSecretExpiresAt: null,
    isEnabled: false,
    disabledReason: 'consecutiveFailures',
    disabledAt: iso(seededAt - 3 * DAY),
    firstFailureAt: iso(seededAt - 8 * DAY - 6 * HOUR),
    creationTime: iso(seededAt - 52 * DAY),
  },
  {
    id: '6b1f3c2a-0d4e-4f7a-9c1b-2a3d4e5f6a03',
    name: 'Data warehouse',
    description: null,
    url: 'https://dw.example.com/ingest/events',
    eventTypes: ['*'],
    headers: {},
    secret: 'whsec_Tz2Wq8Ne4Rv6Ub0Mc1Ki7Ox3Pl9Sa5Hd',
    previousSecretExpiresAt: null,
    isEnabled: false,
    disabledReason: 'manual',
    disabledAt: iso(seededAt - 20 * DAY),
    firstFailureAt: null,
    creationTime: iso(seededAt - 60 * DAY),
  },
];

/** 一次尝试实际发出的头部快照：组件写入的头在前，自定义头在后；不含签名。 */
export function mockAttemptHeaders(
  eventId: string,
  attemptedAt: number,
  headers: Record<string, string>,
): Record<string, string> {
  return {
    'content-type': 'application/json',
    'webhook-id': eventId,
    'webhook-timestamp': String(Math.floor(attemptedAt / 1000)),
    ...headers,
  };
}

export function mockPayload(type: string, occurredAt: number, data: object): string {
  return JSON.stringify({ type, timestamp: iso(occurredAt), data });
}

function attempt(
  eventId: string,
  attemptedAt: number,
  headers: Record<string, string>,
  result: Pick<WebhookAttemptOutputDto, 'statusCode' | 'response' | 'error' | 'durationMs'>,
): WebhookAttemptOutputDto {
  return {
    id: nextMockWebhookId(),
    attemptedAt: iso(attemptedAt),
    trigger: 'automatic',
    requestHeaders: mockAttemptHeaders(eventId, attemptedAt, headers),
    ...result,
  };
}

const OK = { statusCode: 200, response: '{"received":true}', error: null, durationMs: 182 };
const UNAVAILABLE = {
  statusCode: 503,
  response: 'Service Unavailable',
  error: null,
  durationMs: 1204,
};
const TIMEOUT = {
  statusCode: null,
  response: null,
  error: 'The request timed out after 15 seconds.',
  durationMs: 15000,
};

/** 首发失败后的重试间隔（小时），取组件默认退避表的前几档。 */
const RETRY_OFFSETS = [0, 5 / 3600, 5 / 60, 0.5, 2, 5, 10, 10];

function delivery(
  subscription: MockWebhookSubscription,
  occurredAt: number,
  index: number,
): MockWebhookDelivery {
  const eventId = nextMockWebhookId();
  const at = (hours: number) => occurredAt + 2_000 + hours * HOUR;
  const failingSince = subscription.firstFailureAt ? Date.parse(subscription.firstFailureAt) : null;
  let status: MockWebhookDelivery['status'] = 'succeeded';
  let attempts: WebhookAttemptOutputDto[];
  let nextAttemptAt: string | null = null;

  if (failingSince !== null && occurredAt >= failingSince) {
    // 自动停用前一直失败：退避表用完记为失败
    status = 'failed';
    attempts = RETRY_OFFSETS.map((offset, n) =>
      attempt(eventId, at(offset), subscription.headers, n % 3 === 1 ? TIMEOUT : UNAVAILABLE),
    );
  } else if (seededAt - occurredAt < 2 * HOUR) {
    // 最近两小时的投递还在重试
    status = 'pending';
    attempts = [0, RETRY_OFFSETS[1]].map((offset) =>
      attempt(eventId, at(offset), subscription.headers, UNAVAILABLE),
    );
    nextAttemptAt = iso(Math.max(at(RETRY_OFFSETS[2]), seededAt + 10 * 60_000));
  } else if (index % 23 === 7) {
    status = 'failed';
    attempts = RETRY_OFFSETS.map((offset) =>
      attempt(eventId, at(offset), subscription.headers, UNAVAILABLE),
    );
  } else if (index % 9 === 4) {
    // 首发失败、第一次重试成功
    attempts = [
      attempt(eventId, at(0), subscription.headers, TIMEOUT),
      attempt(eventId, at(RETRY_OFFSETS[1]), subscription.headers, OK),
    ];
  } else {
    attempts = [attempt(eventId, at(0), subscription.headers, OK)];
  }

  const last = attempts.at(-1)!;
  return {
    id: nextMockWebhookId(),
    eventId,
    eventType: SAMPLE_EVENT,
    subscriptionId: subscription.id,
    status,
    attemptCount: attempts.length,
    lastStatusCode: last.statusCode ?? null,
    lastError: last.error ?? null,
    lastAttemptAt: last.attemptedAt,
    nextAttemptAt,
    nextTrigger: status === 'pending' ? 'automatic' : null,
    creationTime: iso(occurredAt),
    payload: mockPayload(SAMPLE_EVENT, occurredAt, { userId: eventId.slice(-8) }),
    // 倒序：最近一次在前
    attempts: [...attempts].reverse(),
  };
}

/** 订阅在 [from, to) 内每隔 stepHours 发生一个事件。 */
function deliveriesOf(
  subscription: MockWebhookSubscription,
  from: number,
  to: number,
  stepHours: number,
): MockWebhookDelivery[] {
  const items: MockWebhookDelivery[] = [];
  for (let time = from, index = 0; time < to; time += stepHours * HOUR, index += 1) {
    items.push(delivery(subscription, time, index));
  }
  return items;
}

function seedDeliveries(): MockWebhookDelivery[] {
  const [erp, portal, warehouse] = WEBHOOK_SUBSCRIPTIONS;
  const items = [
    // 起点不对齐整点：桶边界上不会扎堆
    ...deliveriesOf(erp, seededAt - 40 * DAY + 17 * 60_000, seededAt - 2 * HOUR, 5),
    // 最近两小时的两条还在重试
    ...deliveriesOf(erp, seededAt - 90 * 60_000, seededAt, 1),
    ...deliveriesOf(portal, seededAt - 40 * DAY + 41 * 60_000, Date.parse(portal.disabledAt!), 11),
    ...deliveriesOf(
      warehouse,
      seededAt - 40 * DAY + 29 * 60_000,
      Date.parse(warehouse.disabledAt!),
      13,
    ),
  ];
  return items.sort((a, b) => Date.parse(b.creationTime) - Date.parse(a.creationTime));
}

/** 按创建时刻倒序。 */
export const WEBHOOK_DELIVERIES: MockWebhookDelivery[] = seedDeliveries();
