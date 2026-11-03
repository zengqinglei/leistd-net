import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowDownRight, lucideArrowUpRight, lucideInfo } from '@ng-icons/lucide';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmChartImports } from '@spartan-ng/helm/chart';
import { HlmToggleGroupImports } from '@spartan-ng/helm/toggle-group';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

import { AccountPreferencesService } from '../../../../../../core/services/account-preferences-service';
import { StatPanel } from '../../../../../../shared/components/stat-panel/stat-panel';
import { AppNumber, formatAppNumber } from '../../../../../../shared/pipes/app-number-pipe';
//#if (!IncludeLocalization)
import { englishText } from '../../../../../../shared/utils/english-text';
//#endif
import { changeOf, sparklineOf } from '../../../../../../shared/utils/stat-trend';
import { WebhookStatisticsOutputDto } from '../../../../dtos/webhook.dto';
import { RangePreset } from '../../../dashboard/dashboard-calendar';
import { rateChangeOf } from '../../webhook-display';

type KpiId = 'successRate' | 'deliveries' | 'failed' | 'disabledSubscriptions';

/** 指标卡数值从这里起用紧凑写法，与仪表盘一致。 */
const COMPACT_FROM = 10_000;

interface KpiView {
  id: KpiId;
  valueText: string;
  /** 紧凑写法时的完整整数，放进提示。 */
  valueFull: string | null;
  /** 与上一区间的变化（比例）；没有可比数据时为 null。存量指标不比较，为 undefined。 */
  change: number | null | undefined;
  higherIsWorse: boolean;
  sparkline: ReturnType<typeof sparklineOf>;
  /** 卡片底行的副文案：卡片上是短说法，完整口径放进提示。 */
  note: KpiNote;
}

interface KpiNote {
  caption: string;
  basis: string;
  params?: Record<string, unknown>;
  testId?: string;
}

/**
 * 投递统计：区间分段（今天 / 7 天 / 30 天）、摘要行与四张指标卡（成功率、投递数、失败、已停用订阅），
 * 写法同仪表盘的 KPI 卡。前三张带环比与迷你趋势；失败卡整体是跳转到"投递日志筛选失败"的按钮。
 */
@Component({
  selector: 'app-webhook-statistics',
  // prettier-ignore
  imports: [
    NgTemplateOutlet,
    NgIcon,
    HlmBadge,
    HlmButton,
    ...HlmChartImports,
    ...HlmToggleGroupImports,
    ...HlmTooltipImports,
    StatPanel,
    AppNumber,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [provideIcons({ lucideArrowDownRight, lucideArrowUpRight, lucideInfo })],
  templateUrl: './webhook-statistics.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WebhookStatistics {
  protected readonly displayLocale = inject(AccountPreferencesService).displayLocale;
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif

  readonly statistics = input<WebhookStatisticsOutputDto | null>(null);
  readonly preset = input<RangePreset>('7d');
  readonly loading = input(false);
  /** 刷新失败：保留已有数据，提示并给重试。 */
  readonly loadFailed = input(false);
  /** 按订阅筛选时的订阅名，摘要行写明统计范围。 */
  readonly subscriptionName = input<string | null>(null);
  readonly presetChange = output<RangePreset>();
  readonly retry = output<void>();
  readonly showFailed = output<void>();

  protected readonly days = computed(() => {
    const preset = this.preset();
    return preset === 'today' ? 1 : preset === '7d' ? 7 : 30;
  });

  /** 指标卡标题与口径说明的词条键，逐个写出：动态拼接的键不在 i18n 静态检查之内。 */
  protected readonly titleKeys: Record<KpiId, string> = {
    successRate: 'webhooks.stats.successRate',
    deliveries: 'webhooks.stats.deliveries',
    failed: 'webhooks.stats.failed',
    disabledSubscriptions: 'webhooks.stats.disabledShort',
  };
  /** 口径说明按钮的无障碍名称用完整指标名；卡片标题是短说法。 */
  protected readonly nameKeys: Record<KpiId, string> = {
    successRate: 'webhooks.stats.successRate',
    deliveries: 'webhooks.stats.deliveries',
    failed: 'webhooks.stats.failed',
    disabledSubscriptions: 'webhooks.stats.disabledSubscriptions',
  };
  protected readonly infoKeys: Record<KpiId, string> = {
    successRate: 'webhooks.stats.info.successRate',
    deliveries: 'webhooks.stats.info.deliveries',
    failed: 'webhooks.stats.info.failed',
    disabledSubscriptions: 'webhooks.stats.info.disabledSubscriptions',
  };

  readonly kpis = computed<KpiView[]>(() => {
    const statistics = this.statistics();
    if (!statistics) {
      return [];
    }
    const locale = this.displayLocale();
    const count = (value: number) => ({
      valueText: formatAppNumber(value, value >= COMPACT_FROM ? 'compact' : 'integer', locale),
      valueFull: value >= COMPACT_FROM ? formatAppNumber(value, 'integer', locale) : null,
    });
    const days = this.days();
    const retrying = statistics.retrying.value;
    const note = (id: KpiId, change: number | null | undefined): KpiNote => {
      if (id === 'disabledSubscriptions') {
        return {
          caption: 'webhooks.stats.currentCount',
          basis: 'webhooks.stats.currentCountBasis',
        };
      }
      if (id === 'failed' && retrying > 0) {
        return {
          caption: 'webhooks.stats.retrying',
          basis: 'webhooks.stats.retryingBasis',
          params: { count: formatAppNumber(retrying, 'integer', locale) },
          testId: 'kpi-retrying',
        };
      }
      if (change === null) {
        return {
          caption: 'webhooks.stats.noComparison',
          basis: 'webhooks.stats.noComparisonBasis',
          testId: 'kpi-no-comparison',
        };
      }
      // 区间都含今天，上一区间比较相同的本地进度（同仪表盘）
      return days === 1
        ? { caption: 'webhooks.stats.vsYesterday', basis: 'webhooks.stats.vsYesterdayBasis' }
        : {
            caption: 'webhooks.stats.vsPrevious',
            basis: 'webhooks.stats.vsPreviousBasis',
            params: { days },
          };
    };
    const rate = statistics.successRate;
    const kpis: Omit<KpiView, 'note'>[] = [
      {
        id: 'successRate',
        valueText: rate.value == null ? '-' : formatAppNumber(rate.value, 'percent', locale),
        valueFull: null,
        change: rateChangeOf(rate.value, rate.previous),
        higherIsWorse: false,
        sparkline: sparklineOf(rate.series, 'var(--chart-1)'),
      },
      {
        id: 'deliveries',
        ...count(statistics.deliveries.value),
        change: changeOf(statistics.deliveries.value, statistics.deliveries.previous),
        higherIsWorse: false,
        sparkline: sparklineOf(statistics.deliveries.series, 'var(--chart-1)'),
      },
      {
        id: 'failed',
        ...count(statistics.failed.value),
        change: changeOf(statistics.failed.value, statistics.failed.previous),
        higherIsWorse: true,
        sparkline: sparklineOf(statistics.failed.series, 'var(--chart-3)'),
      },
      {
        id: 'disabledSubscriptions',
        ...count(statistics.disabledSubscriptions.value),
        change: undefined,
        higherIsWorse: true,
        sparkline: null,
      },
    ];
    return kpis.map((kpi) => ({ ...kpi, note: note(kpi.id, kpi.change) }));
  });

  /** 摘要行的数据：投递数与需要关注的订阅数（已停用存量），由模板翻译。 */
  readonly summary = computed(() => {
    const statistics = this.statistics();
    return statistics
      ? {
          deliveries: formatAppNumber(statistics.deliveries.value, 'integer', this.displayLocale()),
          attention: statistics.disabledSubscriptions.value,
        }
      : null;
  });

  /** 分段控件不可取消选中（`nullable` 为假），单选时只会给出另一档的值。 */
  onPresetChange(value: RangePreset | readonly RangePreset[] | null | undefined): void {
    const preset = Array.isArray(value) ? value[0] : value;
    if (preset != null && preset !== this.preset()) {
      this.presetChange.emit(preset as RangePreset);
    }
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'common.retry': 'Retry',
  'webhooks.range.days30': '30 days',
  'webhooks.range.days7': '7 days',
  'webhooks.range.label': 'Statistics range',
  'webhooks.range.lastDays': 'Last {{days}} days',
  'webhooks.range.today': 'Today',
  'webhooks.stats.attention': 'Needs attention',
  'webhooks.stats.currentCount': 'Current count',
  'webhooks.stats.currentCountBasis':
    'Subscriptions disabled right now. Not affected by the range.',
  'webhooks.stats.deliveries': 'Deliveries',
  'webhooks.stats.disabledShort': 'Disabled',
  'webhooks.stats.disabledSubscriptions': 'Disabled subscriptions',
  'webhooks.stats.failed': 'Failed',
  'webhooks.stats.info.deliveries':
    'Deliveries created in the range. An event sent to N subscriptions counts N times.',
  'webhooks.stats.info.disabledSubscriptions':
    'Subscriptions disabled right now, manually or automatically. Not affected by the range.',
  'webhooks.stats.info.failed':
    'Deliveries created in the range whose current status is failed. Resending a delivery updates the count.',
  'webhooks.stats.info.successRate':
    'Succeeded ÷ (succeeded + failed) for deliveries created in the range. Deliveries still retrying are not counted.',
  'webhooks.stats.infoLabel': 'How {{metric}} is calculated',
  'webhooks.stats.label': 'Delivery statistics',
  'webhooks.stats.noComparison': 'No earlier data',
  'webhooks.stats.noComparisonBasis':
    'The previous range is older than the delivery log retention, so there is nothing to compare with.',
  'webhooks.stats.refreshFailed': "Couldn't refresh the statistics. Showing the last loaded data.",
  'webhooks.stats.retrying': '{{count}} retrying',
  'webhooks.stats.retryingBasis':
    '{{count}} more deliveries are still retrying and are not counted as failed.',
  'webhooks.stats.showFailed': 'Show failed deliveries',
  'webhooks.stats.successRate': 'Success rate',
  'webhooks.stats.vsPrevious': 'vs prior {{days}} days',
  'webhooks.stats.vsPreviousBasis': 'vs the same point of the previous {{days}} days',
  'webhooks.stats.vsYesterday': 'vs yesterday',
  'webhooks.stats.vsYesterdayBasis': 'vs the same time yesterday',
  'webhooks.summary.attention': '{{count}} subscriptions need attention',
  'webhooks.summary.deliveries': '{{count}} deliveries',
  'webhooks.summary.subscription': 'Subscription: {{name}}',
};
//#endif
