import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCircleAlert,
  lucideInbox,
  lucidePanelRight,
  lucideRotateCw,
  lucideSearchX,
} from '@ng-icons/lucide';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { ColumnDef, PaginationState } from '@tanstack/angular-table';

import { AccountPreferencesService } from '../../../../../../core/services/account-preferences-service';
import {
  TableLoading,
  TableLoadingBody,
} from '../../../../../../shared/components/table-loading/table-loading';
import { TablePaginator } from '../../../../../../shared/components/table-paginator/table-paginator';
import { StatusDot } from '../../../../../../shared/directives/status-dot';
import { TableFit } from '../../../../../../shared/directives/table-fit';
import { TruncatedTooltip } from '../../../../../../shared/directives/truncated-tooltip';
import { AppDate } from '../../../../../../shared/pipes/app-date-pipe';
import { AppNumber } from '../../../../../../shared/pipes/app-number-pipe';
//#if (!IncludeLocalization)
import { englishText } from '../../../../../../shared/utils/english-text';
//#endif
import {
  ACTIONS_COLUMN_META,
  tableColumnVisibility,
  TITLE_COLUMN_META,
  TITLE_CONTENT_CLASS,
} from '../../../../../../shared/utils/table-column-meta';
import {
  injectAppTable,
  type AppTableFeatures,
} from '../../../../../../shared/utils/table-features';
import { resolveTableUpdater } from '../../../../../../shared/utils/table-query-state';
import { tableViewportSignal } from '../../../../../../shared/utils/table-viewport';
import { WebhookDeliveryOutputDto } from '../../../../dtos/webhook.dto';
import { DELIVERY_STATUS_KEYS, DELIVERY_STATUS_VARIANTS } from '../../webhook-display';

/**
 * 投递日志表：事件类型与 id、订阅、状态、尝试次数、最近状态码、下次重试、发生时间与操作（详情、重发）。
 * 不做行展开：点整行或"详情"打开右侧投递详情抽屉，抽屉顶部列全各列。重发只对终态开放。
 */
@Component({
  selector: 'app-webhook-delivery-table',
  imports: [
    ...HlmEmptyImports,
    NgTemplateOutlet,
    TableFit,
    TableLoading,
    TableLoadingBody,
    AppDate,
    AppNumber,
    NgIcon,
    HlmBadge,
    HlmButton,
    HlmSpinner,
    StatusDot,
    TablePaginator,
    ...HlmTableImports,
    ...HlmTooltipImports,
    TruncatedTooltip,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [
    provideIcons({
      lucideCircleAlert,
      lucideInbox,
      lucidePanelRight,
      lucideRotateCw,
      lucideSearchX,
    }),
  ],
  templateUrl: './webhook-delivery-table.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WebhookDeliveryTable {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly accountPreferences = inject(AccountPreferencesService);
  protected readonly displayTimeZone = this.accountPreferences.timeZone;
  protected readonly displayLocale = this.accountPreferences.displayLocale;
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif

  readonly deliveries = input<WebhookDeliveryOutputDto[]>([]);
  readonly totalCount = input(0);
  readonly pagination = input<PaginationState>({ pageIndex: 0, pageSize: 20 });
  readonly loading = input(false);
  readonly filtered = input(false);
  readonly loadError = input<string | null>(null);
  readonly canManage = input(false);
  /** 抽屉里打开的投递：该行高亮。 */
  readonly selectedId = input<string | null>(null);
  readonly retryingId = input<string | null>(null);

  readonly reload = output<void>();
  readonly paginationChange = output<PaginationState>();
  readonly open = output<string>();
  readonly retry = output<WebhookDeliveryOutputDto>();

  protected readonly tableViewport = tableViewportSignal();
  protected readonly titleContentClass = TITLE_CONTENT_CLASS;
  private readonly tableFit = viewChild(TableFit);
  protected readonly foldLevel = computed(() => this.tableFit()?.level() ?? this.tableViewport());

  protected readonly columns: ColumnDef<AppTableFeatures, WebhookDeliveryOutputDto>[] = [
    { accessorKey: 'eventType', id: 'event', enableSorting: false, meta: TITLE_COLUMN_META },
    {
      accessorKey: 'subscriptionName',
      id: 'subscription',
      enableSorting: false,
      meta: { priority: 'secondary' },
    },
    {
      accessorKey: 'status',
      id: 'status',
      enableSorting: false,
      enableHiding: false,
      meta: { priority: 'primary', locked: true },
    },
    {
      accessorKey: 'attemptCount',
      id: 'attempts',
      enableSorting: false,
      meta: { priority: 'tertiary' },
    },
    {
      accessorKey: 'lastStatusCode',
      id: 'lastStatusCode',
      enableSorting: false,
      meta: { priority: 'tertiary' },
    },
    {
      accessorKey: 'nextAttemptAt',
      id: 'nextAttemptAt',
      enableSorting: false,
      meta: { priority: 'tertiary' },
    },
    {
      accessorKey: 'creationTime',
      id: 'creationTime',
      enableSorting: false,
      meta: { priority: 'secondary' },
    },
    { id: 'actions', enableSorting: false, enableHiding: false, meta: ACTIONS_COLUMN_META },
  ];

  private readonly columnVisibility = computed(() =>
    tableColumnVisibility(this.columns, this.foldLevel()),
  );

  protected readonly columnLabelKeys: Readonly<Record<string, string>> = {
    event: 'webhooks.deliveries.colEvent',
    subscription: 'webhooks.deliveries.colSubscription',
    status: 'webhooks.deliveries.colStatus',
    attempts: 'webhooks.deliveries.colAttempts',
    lastStatusCode: 'webhooks.deliveries.colLastStatusCode',
    nextAttemptAt: 'webhooks.deliveries.colNextAttempt',
    creationTime: 'webhooks.deliveries.colOccurredAt',
  };

  protected readonly statusKeys = DELIVERY_STATUS_KEYS;
  protected readonly statusVariants = DELIVERY_STATUS_VARIANTS;

  protected readonly table = injectAppTable(() => ({
    data: this.deliveries(),
    columns: this.columns,
    getRowId: (row) => row.id,
    manualPagination: true,
    rowCount: this.totalCount(),
    onPaginationChange: (updater) =>
      this.paginationChange.emit(resolveTableUpdater(updater, this.pagination())),
    state: {
      columnVisibility: this.columnVisibility(),
      pagination: this.pagination(),
    },
  }));

  readonly currentPage = computed(() => this.pagination().pageIndex + 1);
  readonly totalPages = computed(() => Math.max(1, this.table.getPageCount()));

  changePageSize(pageSize: number): void {
    this.paginationChange.emit({ pageIndex: 0, pageSize });
  }

  /** 聚焦该投递行的"详情"按钮（关闭详情抽屉后回焦）；行不在当前页时返回 `false`。 */
  focusDetails(id: string): boolean {
    const row = Array.from(
      this.host.nativeElement.querySelectorAll<HTMLElement>('[data-testid="webhook-delivery-row"]'),
    ).find((element) => element.dataset['deliveryId'] === id);
    const button = row?.querySelector<HTMLElement>('[data-testid="webhook-delivery-details"]');
    button?.focus();
    return !!button;
  }

  /** 点整行打开详情；点在行内按钮上时由按钮自己处理。 */
  onRowClick(event: MouseEvent, id: string): void {
    if ((event.target as HTMLElement).closest('button, a')) {
      return;
    }
    this.open.emit(id);
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'common.actions': 'Actions',
  'common.details': 'View details',
  'common.loadFailed': "Couldn't load the list",
  'common.pageOf': 'Page {{page}} of {{total}}',
  'common.pagination.first': 'First page',
  'common.pagination.last': 'Last page',
  'common.pagination.next': 'Next page',
  'common.pagination.previous': 'Previous page',
  'common.retry': 'Retry',
  'common.rowsPerPage': 'Items per page',
  'webhooks.action.retry': 'Resend',
  'webhooks.action.retryPending': 'Still retrying; next attempt at {{time}}',
  'webhooks.deliveries.colAttempts': 'Attempts',
  'webhooks.deliveries.colEvent': 'Event',
  'webhooks.deliveries.colLastStatusCode': 'Last status',
  'webhooks.deliveries.colNextAttempt': 'Next retry',
  'webhooks.deliveries.colOccurredAt': 'Occurred at',
  'webhooks.deliveries.colStatus': 'Status',
  'webhooks.deliveries.colSubscription': 'Subscription',
  'webhooks.deliveries.emptyFilteredHint': 'Adjust the search or filters',
  'webhooks.deliveries.emptyFilteredTitle': 'No matching deliveries',
  'webhooks.deliveries.emptyHint': 'Deliveries appear here once events are published',
  'webhooks.deliveries.emptyTitle': 'No deliveries yet',
  'webhooks.deliveries.noResponse': 'No response',
  'webhooks.deliveryStatus.failed': 'Failed',
  'webhooks.deliveryStatus.pending': 'Retrying',
  'webhooks.deliveryStatus.succeeded': 'Succeeded',
  'webhooks.table.currentPageReport': '{{total}} total',
};
//#endif
