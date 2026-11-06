import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideChevronRight, lucideCopy, lucideRotateCw } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmSheetImports } from '@spartan-ng/helm/sheet';
import { HlmSkeleton } from '@spartan-ng/helm/skeleton';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

import { AccountPreferencesService } from '../../../../../../core/services/account-preferences-service';
import { StatusDot } from '../../../../../../shared/directives/status-dot';
import { AppDate } from '../../../../../../shared/pipes/app-date-pipe';
import { AppNumber } from '../../../../../../shared/pipes/app-number-pipe';
import { injectCopyToClipboard } from '../../../../../../shared/utils/clipboard';
//#if (!IncludeLocalization)
import { englishText } from '../../../../../../shared/utils/english-text';
//#endif
import { WebhookDeliveryDetailOutputDto } from '../../../../dtos/webhook.dto';
import {
  DELIVERY_STATUS_KEYS,
  DELIVERY_STATUS_VARIANTS,
  headerLines,
  prettyJson,
  TRIGGER_KEYS,
} from '../../webhook-display';

/**
 * 投递详情抽屉（右侧 Sheet，窄屏占满）：标题区（事件类型、可复制的事件 id、状态、重发）→ 概要（列全表格各列）
 * → 请求（最近一次尝试的头部快照与格式化的请求体）→ 调用记录（倒序，逐次展开看头部与响应或错误）。
 * 打开与关闭由父页面按查询参数 `delivery` 驱动。`closed` 在退场动画结束、焦点已按 CDK 还原之后发出
 * （关闭按钮、Esc 与随导航关闭都会发），父页面据此收掉参数并回焦入口。
 */
@Component({
  selector: 'app-webhook-delivery-sheet',
  // prettier-ignore
  imports: [
    NgIcon,
    HlmBadge,
    HlmButton,
    HlmSkeleton,
    HlmSpinner,
    StatusDot,
    AppDate,
    AppNumber,
    ...HlmSheetImports,
    ...HlmTooltipImports,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [provideIcons({ lucideCheck, lucideChevronRight, lucideCopy, lucideRotateCw })],
  templateUrl: './webhook-delivery-sheet.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WebhookDeliverySheet {
  private readonly accountPreferences = inject(AccountPreferencesService);
  protected readonly displayTimeZone = this.accountPreferences.timeZone;
  protected readonly displayLocale = this.accountPreferences.displayLocale;
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#else
  protected readonly t = englishText(ENGLISH);
  //#endif
  protected readonly eventIdClipboard = injectCopyToClipboard();
  protected readonly payloadClipboard = injectCopyToClipboard();

  readonly open = input(false);
  readonly delivery = input<WebhookDeliveryDetailOutputDto | null>(null);
  readonly loading = input(false);
  readonly loadError = input<string | null>(null);
  readonly canManage = input(false);
  readonly retrying = input(false);
  readonly closed = output<void>();
  readonly reload = output<void>();
  readonly retry = output<WebhookDeliveryDetailOutputDto>();

  protected readonly statusKeys = DELIVERY_STATUS_KEYS;
  protected readonly statusVariants = DELIVERY_STATUS_VARIANTS;
  protected readonly triggerKeys = TRIGGER_KEYS;
  protected readonly headerLines = headerLines;

  /** 展开的调用记录（按尝试 id）；换一条投递时收起。 */
  private readonly expanded = signal<{ deliveryId: string | null; ids: ReadonlySet<string> }>({
    deliveryId: null,
    ids: new Set(),
  });

  protected readonly payload = computed(() => {
    const delivery = this.delivery();
    return delivery ? prettyJson(delivery.payload) : '';
  });

  /** 最近一次尝试（倒序的第一项）：请求区展示它的头部快照。 */
  protected readonly latestAttempt = computed(() => this.delivery()?.attempts[0] ?? null);

  protected readonly triggerCounts = computed(() => {
    const attempts = this.delivery()?.attempts ?? [];
    const manual = attempts.filter((attempt) => attempt.trigger === 'manual').length;
    return { automatic: attempts.length - manual, manual };
  });

  isExpanded(attemptId: string): boolean {
    const state = this.expanded();
    return state.deliveryId === this.delivery()?.id && state.ids.has(attemptId);
  }

  toggleAttempt(attemptId: string): void {
    const deliveryId = this.delivery()?.id ?? null;
    this.expanded.update((state) => {
      const ids = new Set(state.deliveryId === deliveryId ? state.ids : []);
      if (!ids.delete(attemptId)) {
        ids.add(attemptId);
      }
      return { deliveryId, ids };
    });
  }

  copyEventId(): void {
    const eventId = this.delivery()?.eventId;
    if (eventId) {
      void this.eventIdClipboard.copy(eventId).then((copied) => copied && this.notifyCopied());
    }
  }

  copyPayload(): void {
    const payload = this.payload();
    if (payload) {
      void this.payloadClipboard.copy(payload).then((copied) => copied && this.notifyCopied());
    }
  }

  private notifyCopied(): void {
    //#if (IncludeLocalization)
    toast.success(this.transloco.translate('webhooks.toast.copied'));
    //#else
    toast.success('Copied');
    //#endif
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'common.close': 'Close',
  'common.copy': 'Copy',
  'common.retry': 'Retry',
  'webhooks.action.retry': 'Resend',
  'webhooks.action.retryPending': 'Still retrying; next attempt at {{time}}',
  'webhooks.deliveries.colAttempts': 'Attempts',
  'webhooks.deliveries.colNextAttempt': 'Next retry',
  'webhooks.deliveries.colOccurredAt': 'Occurred at',
  'webhooks.deliveries.colSubscription': 'Subscription',
  'webhooks.deliveries.noResponse': 'No response',
  'webhooks.deliveryStatus.failed': 'Failed',
  'webhooks.deliveryStatus.pending': 'Retrying',
  'webhooks.deliveryStatus.succeeded': 'Succeeded',
  'webhooks.sheet.attemptCount': '{{count}} (automatic {{automatic}} · manual {{manual}})',
  'webhooks.sheet.attempts': 'Attempts',
  'webhooks.sheet.body': 'Body',
  'webhooks.sheet.copyEventId': 'Copy event ID',
  'webhooks.sheet.description': 'The request sent for this event and every attempt to deliver it.',
  'webhooks.sheet.duration': '{{ms}} ms',
  'webhooks.sheet.error': 'Error',
  'webhooks.sheet.headers': 'Request headers',
  'webhooks.sheet.lastResult': 'Last result',
  'webhooks.sheet.latestHeaders': 'Headers · latest attempt ({{time}})',
  'webhooks.sheet.noAttempts': 'Not attempted yet',
  'webhooks.sheet.overview': 'Overview',
  'webhooks.sheet.request': 'Request',
  'webhooks.sheet.response': 'Response',
  'webhooks.sheet.title': 'Delivery details',
  'webhooks.toast.copied': 'Copied',
  'webhooks.trigger.automatic': 'Automatic',
  'webhooks.trigger.manual': 'Manual',
};
//#endif
