import { PagedRequestDto } from '../../../shared/dtos/paged-request.dto';
import { StatisticsQuery, StatisticsRangeDto, StatisticValueDto } from './platform-statistics.dto';

/** 投递状态；`pending` 在界面上显示为“重试中”。 */
export type WebhookDeliveryStatus = 'pending' | 'succeeded' | 'failed';
export type WebhookAttemptTrigger = 'automatic' | 'manual';
export type WebhookDisabledReason = 'manual' | 'endpointGone' | 'consecutiveFailures';

/** 可订阅的事件类型，按登记顺序，含内置的 `webhook.test`。 */
export interface WebhookEventTypeOutputDto {
  name: string;
  group: string;
  /** 说明的本地化键，组件原样下发。 */
  description?: string | null;
}

export interface WebhookSubscriptionLastDeliveryDto {
  id: string;
  status: WebhookDeliveryStatus;
  lastStatusCode?: number | null;
  creationTime: string;
}

/** 订阅；不含密钥明文，只有前缀。 */
export interface WebhookSubscriptionOutputDto {
  id: string;
  name: string;
  description?: string | null;
  url: string;
  eventTypes: string[];
  headers: Record<string, string>;
  /** 当前密钥的前缀，含 `whsec_`。 */
  secretPrefix: string;
  /** 旧密钥继续签名的截止时刻；不在轮换过渡期时为空。 */
  previousSecretExpiresAt?: string | null;
  isEnabled: boolean;
  disabledReason?: WebhookDisabledReason | null;
  disabledAt?: string | null;
  firstFailureAt?: string | null;
  lastDelivery?: WebhookSubscriptionLastDeliveryDto | null;
  /** 近 7 天成功率（0–1）；没有终态投递时为空。 */
  successRateLast7Days?: number | null;
  creationTime: string;
}

export interface CreateWebhookSubscriptionInputDto {
  name: string;
  description?: string | null;
  url: string;
  eventTypes: string[];
  headers?: Record<string, string> | null;
}

export interface UpdateWebhookSubscriptionInputDto {
  name: string;
  description?: string | null;
  url: string;
  eventTypes: string[];
  /** 为空即清空。 */
  headers?: Record<string, string> | null;
}

/** 创建与轮换密钥的结果；`secret` 是只出现这一次的明文密钥。 */
export interface WebhookSubscriptionSecretOutputDto {
  subscription: WebhookSubscriptionOutputDto;
  secret: string;
}

export interface GetWebhookSubscriptionsInputDto extends PagedRequestDto {
  keyword?: string;
  isEnabled?: boolean;
  eventType?: string;
}

export interface WebhookDeliveryOutputDto {
  id: string;
  /** 即 `webhook-id`，手动重发不变。 */
  eventId: string;
  eventType: string;
  subscriptionId: string;
  subscriptionName: string;
  subscriptionUrl: string;
  status: WebhookDeliveryStatus;
  attemptCount: number;
  lastStatusCode?: number | null;
  lastError?: string | null;
  lastAttemptAt?: string | null;
  /** 只在 `pending` 时有值。 */
  nextAttemptAt?: string | null;
  /** 只在 `pending` 时有值。 */
  nextTrigger?: WebhookAttemptTrigger | null;
  /** 事件发布时刻，统计按它分桶。 */
  creationTime: string;
}

export interface WebhookAttemptOutputDto {
  id: string;
  attemptedAt: string;
  trigger: WebhookAttemptTrigger;
  /** 当次实际发出的请求头，不含 `webhook-signature`。 */
  requestHeaders: Record<string, string>;
  statusCode?: number | null;
  durationMs: number;
  /** 响应体，截断到 1024 个字符。 */
  response?: string | null;
  error?: string | null;
}

export interface WebhookDeliveryDetailOutputDto extends WebhookDeliveryOutputDto {
  /** 请求体：发布时冻结的 JSON 信封 `{type, timestamp, data}` 原文。 */
  payload: string;
  /** 按尝试时刻倒序。 */
  attempts: WebhookAttemptOutputDto[];
}

export interface GetWebhookDeliveriesInputDto extends PagedRequestDto {
  keyword?: string;
  subscriptionId?: string;
  status?: WebhookDeliveryStatus;
  eventType?: string;
}

/**
 * 成功率（0–1）：成功 ÷（成功 + 失败），不含重试中；分母为 0 的区间或桶为 null。
 * 上一区间起点早于保留期下限时 previous 为 null，界面显示“无可比数据”。
 */
export interface WebhookSuccessRateDto {
  value: number | null;
  previous?: number | null;
  series?: (number | null)[] | null;
}

/** `GET /api/v1/webhooks/statistics` 的响应，统计当前作用域；带 `subscriptionId` 时只统计该订阅。 */
export interface WebhookStatisticsOutputDto {
  range: StatisticsRangeDto;
  /** 各桶起点的 UTC ISO 串，升序。 */
  buckets: string[];
  successRate: WebhookSuccessRateDto;
  /** 按投递创建时刻分桶；一个事件发给 N 个订阅计 N。上一区间超出保留期时 previous 为 null。 */
  deliveries: StatisticValueDto;
  /** 按投递当前状态计入其创建时的桶。 */
  succeeded: StatisticValueDto;
  failed: StatisticValueDto;
  /** 当前存量，不随区间变化，只有 value。 */
  retrying: StatisticValueDto;
  /** 当前存量，不随区间变化，只有 value。 */
  disabledSubscriptions: StatisticValueDto;
}

export interface WebhookStatisticsQuery extends StatisticsQuery {
  subscriptionId?: string;
}
