import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
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
  lucideChevronRight,
  lucideCircleAlert,
  lucideEllipsis,
  lucideInbox,
  lucideKeyRound,
  lucideList,
  lucidePencil,
  lucidePower,
  lucidePowerOff,
  lucideSearchX,
  lucideSend,
  lucideTrash2,
} from '@ng-icons/lucide';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { ColumnDef, PaginationState } from '@tanstack/angular-table';

import { AccountPreferencesService } from '../../../../../../core/services/account-preferences-service';
import { RelativeTimeLabel } from '../../../../../../shared/components/relative-time-label/relative-time-label';
import {
  TableLoading,
  TableLoadingBody,
} from '../../../../../../shared/components/table-loading/table-loading';
import { TablePaginator } from '../../../../../../shared/components/table-paginator/table-paginator';
import { ColumnValue } from '../../../../../../shared/directives/column-value';
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
  hiddenDetailColumns,
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
import { resolveDisplayTimeZone } from '../../../../../../shared/utils/zoned-time';
import { WebhookSubscriptionOutputDto } from '../../../../dtos/webhook.dto';
import {
  DELIVERY_STATUS_KEYS,
  DELIVERY_STATUS_VARIANTS,
  DISABLED_REASON_KEYS,
  SUBSCRIPTION_STATE_KEYS,
  SUBSCRIPTION_STATE_VARIANTS,
  subscribesAllEvents,
  subscriptionState,
} from '../../webhook-display';

/**
 * 订阅表：名称与地址、订阅事件、状态、近 7 天成功率、最近投递、创建时间与操作。
 * 行展开列出被折叠的列，再补停用原因、密钥前缀、请求头与说明（这几项不对应列，任何档位都可展开）。
 */
@Component({
  selector: 'app-webhook-subscription-table',
  imports: [
    ...HlmEmptyImports,
    NgTemplateOutlet,
    ColumnValue,
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
    RelativeTimeLabel,
    TablePaginator,
    ...HlmDropdownMenuImports,
    ...HlmTableImports,
    ...HlmTooltipImports,
    TruncatedTooltip,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [
    provideIcons({
      lucideChevronRight,
      lucideCircleAlert,
      lucideEllipsis,
      lucideInbox,
      lucideKeyRound,
      lucideList,
      lucidePencil,
      lucidePower,
      lucidePowerOff,
      lucideSearchX,
      lucideSend,
      lucideTrash2,
    }),
  ],
  templateUrl: './webhook-subscription-table.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WebhookSubscriptionTable {
  private readonly accountPreferences = inject(AccountPreferencesService);
  protected readonly displayTimeZone = this.accountPreferences.timeZone;
  protected readonly displayLocale = this.accountPreferences.displayLocale;
  /** 相对时间按实际生效的展示时区分日。 */
  protected readonly resolvedTimeZone = computed(() =>
    resolveDisplayTimeZone(this.displayTimeZone()),
  );
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif

  readonly subscriptions = input<WebhookSubscriptionOutputDto[]>([]);
  readonly totalCount = input(0);
  readonly pagination = input<PaginationState>({ pageIndex: 0, pageSize: 20 });
  readonly loading = input(false);
  readonly filtered = input(false);
  readonly loadError = input<string | null>(null);
  /** 写入口（编辑、测试、轮换、启停、删除）只在有 `App.Webhooks.Manage` 时渲染。 */
  readonly canManage = input(false);
  /** 分钟级时钟，最近投递的相对时间随它更新。 */
  readonly now = input(Date.now());
  /** 正在执行行操作的订阅：该行的操作按钮置灰、显示进行中。 */
  readonly busyId = input<string | null>(null);

  readonly retry = output<void>();
  readonly paginationChange = output<PaginationState>();
  readonly edit = output<string>();
  readonly sendTest = output<string>();
  readonly viewDeliveries = output<string>();
  readonly rotateSecret = output<string>();
  readonly enable = output<WebhookSubscriptionOutputDto>();
  readonly disable = output<string>();
  readonly delete = output<WebhookSubscriptionOutputDto>();

  protected readonly tableViewport = tableViewportSignal();
  protected readonly titleContentClass = TITLE_CONTENT_CLASS;
  private readonly tableFit = viewChild(TableFit);
  protected readonly foldLevel = computed(() => this.tableFit()?.level() ?? this.tableViewport());

  protected readonly columns: ColumnDef<AppTableFeatures, WebhookSubscriptionOutputDto>[] = [
    { accessorKey: 'name', id: 'name', enableSorting: false, meta: TITLE_COLUMN_META },
    {
      accessorKey: 'eventTypes',
      id: 'eventTypes',
      enableSorting: false,
      meta: { priority: 'secondary', detail: true },
    },
    {
      accessorKey: 'isEnabled',
      id: 'status',
      enableSorting: false,
      enableHiding: false,
      meta: { priority: 'primary', locked: true },
    },
    {
      accessorKey: 'successRateLast7Days',
      id: 'successRate',
      enableSorting: false,
      meta: { priority: 'tertiary', detail: true },
    },
    {
      accessorKey: 'lastDelivery',
      id: 'lastDelivery',
      enableSorting: false,
      meta: { priority: 'secondary', detail: true },
    },
    {
      accessorKey: 'creationTime',
      id: 'creationTime',
      enableSorting: false,
      meta: { priority: 'tertiary', detail: true },
    },
    { id: 'actions', enableSorting: false, enableHiding: false, meta: ACTIONS_COLUMN_META },
  ];

  private readonly columnVisibility = computed(() =>
    tableColumnVisibility(this.columns, this.foldLevel()),
  );

  /** 列标签的词条键：表头与展开详情共用。 */
  protected readonly columnLabelKeys: Readonly<Record<string, string>> = {
    name: 'webhooks.subscriptions.colName',
    eventTypes: 'webhooks.subscriptions.colEvents',
    status: 'webhooks.subscriptions.colStatus',
    successRate: 'webhooks.subscriptions.colSuccessRate',
    lastDelivery: 'webhooks.subscriptions.colLastDelivery',
    creationTime: 'webhooks.subscriptions.colCreatedAt',
  };

  protected readonly stateKeys = SUBSCRIPTION_STATE_KEYS;
  protected readonly stateVariants = SUBSCRIPTION_STATE_VARIANTS;
  protected readonly deliveryStatusKeys = DELIVERY_STATUS_KEYS;
  protected readonly deliveryStatusVariants = DELIVERY_STATUS_VARIANTS;
  protected readonly disabledReasonKeys = DISABLED_REASON_KEYS;
  protected readonly state = subscriptionState;
  protected readonly allEvents = subscribesAllEvents;

  protected detailColumns() {
    return hiddenDetailColumns(this.table);
  }

  protected readonly table = injectAppTable(() => ({
    data: this.subscriptions(),
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

  headerEntries(subscription: WebhookSubscriptionOutputDto): [string, string][] {
    return Object.entries(subscription.headers ?? {});
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'common.actions': 'Actions',
  'common.delete': 'Delete',
  'common.details': 'View details',
  'common.edit': 'Edit',
  'common.loadFailed': "Couldn't load the list",
  'common.pageOf': 'Page {{page}} of {{total}}',
  'common.pagination.first': 'First page',
  'common.pagination.last': 'Last page',
  'common.pagination.next': 'Next page',
  'common.pagination.previous': 'Previous page',
  'common.retry': 'Retry',
  'common.rowsPerPage': 'Items per page',
  'webhooks.action.disable': 'Disable',
  'webhooks.action.enable': 'Enable',
  'webhooks.action.rotateSecret': 'Rotate secret',
  'webhooks.action.sendTest': 'Send test event',
  'webhooks.action.sendTestDisabled': 'Enable the subscription to send a test event',
  'webhooks.action.viewDeliveries': 'View deliveries',
  'webhooks.action.viewDeliveriesOf': 'View deliveries of {{name}}',
  'webhooks.deliveryStatus.failed': 'Failed',
  'webhooks.deliveryStatus.pending': 'Retrying',
  'webhooks.deliveryStatus.succeeded': 'Succeeded',
  'webhooks.disabledReason.consecutiveFailures':
    'Disabled automatically after failing continuously since {{time}}',
  'webhooks.disabledReason.endpointGone': 'The endpoint returned 410 Gone at {{time}}',
  'webhooks.disabledReason.manual': 'Disabled manually at {{time}}',
  'webhooks.event.all': 'All events',
  'webhooks.subscriptionStatus.autoDisabled': 'Auto-disabled',
  'webhooks.subscriptionStatus.disabled': 'Disabled',
  'webhooks.subscriptionStatus.enabled': 'Enabled',
  'webhooks.subscriptions.colCreatedAt': 'Created at',
  'webhooks.subscriptions.colEvents': 'Events',
  'webhooks.subscriptions.colLastDelivery': 'Last delivery',
  'webhooks.subscriptions.colName': 'Name / URL',
  'webhooks.subscriptions.colStatus': 'Status',
  'webhooks.subscriptions.colSuccessRate': 'Success (7 days)',
  'webhooks.subscriptions.description': 'Description',
  'webhooks.subscriptions.disabledReason': 'Disabled reason',
  'webhooks.subscriptions.emptyFilteredHint': 'Adjust the search or filters',
  'webhooks.subscriptions.emptyFilteredTitle': 'No matching subscriptions',
  'webhooks.subscriptions.emptyHint': 'Create a subscription to push events to another system',
  'webhooks.subscriptions.emptyTitle': 'No subscriptions yet',
  'webhooks.subscriptions.headers': 'Headers',
  'webhooks.subscriptions.previousSecret': 'The previous secret also signs until {{time}}',
  'webhooks.subscriptions.secret': 'Secret',
  'webhooks.table.currentPageReport': '{{total}} total',
};
//#endif
