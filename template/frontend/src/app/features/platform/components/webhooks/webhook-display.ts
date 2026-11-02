import {
  WebhookAttemptTrigger,
  WebhookDeliveryStatus,
  WebhookDisabledReason,
  WebhookSubscriptionOutputDto,
} from '../../dtos/webhook.dto';
import { WEBHOOK_ALL_EVENTS } from '../../services/webhook-service';

/** 页面上的两个视图，写在查询参数 `view` 里；缺省是订阅。 */
export type WebhookView = 'subscriptions' | 'deliveries';

export type BadgeVariant = 'success' | 'warning' | 'destructive' | 'secondary' | 'outline';

/** 投递状态的徽章与词条：`pending` 显示为"重试中"。 */
export const DELIVERY_STATUS_VARIANTS: Record<WebhookDeliveryStatus, BadgeVariant> = {
  succeeded: 'success',
  pending: 'warning',
  failed: 'destructive',
};
export const DELIVERY_STATUS_KEYS: Record<WebhookDeliveryStatus, string> = {
  succeeded: 'webhooks.deliveryStatus.succeeded',
  pending: 'webhooks.deliveryStatus.pending',
  failed: 'webhooks.deliveryStatus.failed',
};

export const TRIGGER_KEYS: Record<WebhookAttemptTrigger, string> = {
  automatic: 'webhooks.trigger.automatic',
  manual: 'webhooks.trigger.manual',
};

/** 停用原因的说明句，参数 `time` 是停用（或开始失败）的完整时刻。 */
export const DISABLED_REASON_KEYS: Record<WebhookDisabledReason, string> = {
  manual: 'webhooks.disabledReason.manual',
  endpointGone: 'webhooks.disabledReason.endpointGone',
  consecutiveFailures: 'webhooks.disabledReason.consecutiveFailures',
};

/** 订阅状态：启用；手动停用；端点注销或连续失败导致的自动停用（需要关注）。 */
export type SubscriptionState = 'enabled' | 'disabled' | 'autoDisabled';

export function subscriptionState(subscription: WebhookSubscriptionOutputDto): SubscriptionState {
  if (subscription.isEnabled) {
    return 'enabled';
  }
  return subscription.disabledReason && subscription.disabledReason !== 'manual'
    ? 'autoDisabled'
    : 'disabled';
}

export const SUBSCRIPTION_STATE_VARIANTS: Record<SubscriptionState, BadgeVariant> = {
  enabled: 'success',
  disabled: 'secondary',
  autoDisabled: 'warning',
};
export const SUBSCRIPTION_STATE_KEYS: Record<SubscriptionState, string> = {
  enabled: 'webhooks.subscriptionStatus.enabled',
  disabled: 'webhooks.subscriptionStatus.disabled',
  autoDisabled: 'webhooks.subscriptionStatus.autoDisabled',
};

/** 订阅了全部事件（含以后新增的）。 */
export function subscribesAllEvents(eventTypes: readonly string[]): boolean {
  return eventTypes.includes(WEBHOOK_ALL_EVENTS);
}

/** 请求头按 `name: value` 逐行写出，顺序即发送顺序。 */
export function headerLines(headers: Record<string, string> | null | undefined): string {
  return Object.entries(headers ?? {})
    .map(([name, value]) => `${name}: ${value}`)
    .join('\n');
}

/** 请求体原文格式化为缩进的 JSON；不是合法 JSON 时原样返回。 */
export function prettyJson(text: string): string {
  try {
    return JSON.stringify(JSON.parse(text), null, 2);
  } catch {
    return text;
  }
}

/**
 * 成功率的变化写百分点差（0.006 表示 +0.6 个百分点），同样按整数百分比取整（计数指标的变化率见 `shared/utils/stat-trend`）；比例本身已是百分数，
 * 再求变化率会把 50%→100% 写成 +100%。上一区间为空（无终态投递或超出保留期）时不比较。
 */
export function rateChangeOf(
  value: number | null | undefined,
  previous: number | null | undefined,
): number | null {
  if (value == null || previous == null) {
    return null;
  }
  return Math.round((value - previous) * 100) / 100;
}
