import { DOCUMENT, Location } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  linkedSignal,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Params, Router } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoDirective, TranslocoService, translateSignal } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus, lucideRefreshCw, lucideSearch } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import {
  HlmInputGroup,
  HlmInputGroupAddon,
  HlmInputGroupInput,
} from '@spartan-ng/helm/input-group';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTabsImports } from '@spartan-ng/helm/tabs';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { PaginationState } from '@tanstack/angular-table';
import {
  combineLatest,
  defer,
  EMPTY,
  firstValueFrom,
  interval,
  merge,
  of,
  Subject,
  tap,
} from 'rxjs';
import {
  catchError,
  debounceTime,
  distinctUntilChanged,
  filter,
  finalize,
  map,
  startWith,
  switchMap,
} from 'rxjs/operators';

import { WebhookView } from './webhook-display';
import { WebhookDeliverySheet } from './widgets/webhook-delivery-sheet/webhook-delivery-sheet';
import { WebhookDeliveryTable } from './widgets/webhook-delivery-table/webhook-delivery-table';
import { WebhookEnableDialog } from './widgets/webhook-enable-dialog/webhook-enable-dialog';
import {
  WebhookSecretDialog,
  WebhookSecretKind,
} from './widgets/webhook-secret-dialog/webhook-secret-dialog';
import { WebhookStatistics } from './widgets/webhook-statistics/webhook-statistics';
import { WebhookSubscriptionDialog } from './widgets/webhook-subscription-dialog/webhook-subscription-dialog';
import { WebhookSubscriptionTable } from './widgets/webhook-subscription-table/webhook-subscription-table';
import { applicationErrorMessage } from '../../../../core/errors/application-http-error';
import { ConfirmService } from '../../../../core/feedback/confirm-service';
import { AccountPreferencesService } from '../../../../core/services/account-preferences-service';
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { LayoutService } from '../../../../core/services/layout-service';
import { FacetedFilter } from '../../../../shared/components/faceted-filter/faceted-filter';
import { PERMISSIONS } from '../../../../shared/constants/permission.constants';
//#if (!IncludeLocalization)
import { englishText } from '../../../../shared/utils/english-text';
//#endif
import { paginationFromQuery } from '../../../../shared/utils/table-query-state';
import { resolveDisplayTimeZone, zonedCalendarDate } from '../../../../shared/utils/zoned-time';
import {
  CreateWebhookSubscriptionInputDto,
  GetWebhookDeliveriesInputDto,
  GetWebhookSubscriptionsInputDto,
  WebhookDeliveryDetailOutputDto,
  WebhookDeliveryOutputDto,
  WebhookDeliveryStatus,
  WebhookEventTypeOutputDto,
  WebhookStatisticsOutputDto,
  WebhookStatisticsQuery,
  WebhookSubscriptionOutputDto,
} from '../../dtos/webhook.dto';
import { WebhookService } from '../../services/webhook-service';
import { presetRange, RangePreset } from '../dashboard/dashboard-calendar';

const RANGE_PRESETS: readonly RangePreset[] = ['today', '7d', '30d'];
const DELIVERY_STATUSES: readonly WebhookDeliveryStatus[] = ['pending', 'succeeded', 'failed'];
/** 投递日志的订阅筛选项一次取够；订阅数通常很少，超出时仍可经"查看投递"按订阅跳转。 */
const SUBSCRIPTION_OPTION_LIMIT = 100;
/** 从列表打开投递详情时写进新历史条目的标记：有它说明前一条就是打开前的列表条目，刷新后仍随条目保留。 */
const DELIVERY_OPENED_STATE = 'webhookDeliveryOpened';

/** 只取列表相关的参数：打开投递详情（`delivery`）与切换区间（`range`）不重查列表。 */
const LIST_PARAMS = [
  'view',
  'keyword',
  'status',
  'eventType',
  'subscriptionId',
  'page',
  'pageSize',
] as const;

/**
 * 出站 Webhook 后台页（方案 A+）：顶部投递统计，标签页在"订阅"与"投递日志"两个视图间切换，
 * 视图、筛选、分页、统计区间与打开的投递详情都以 URL 查询参数为唯一来源。
 * 读需要 `App.Webhooks`；写入口（新建、编辑、测试、轮换、启停、删除、重发）只在有 `App.Webhooks.Manage` 时出现。
 */
@Component({
  selector: 'app-webhooks',
  imports: [
    NgIcon,
    HlmButton,
    HlmSpinner,
    HlmInputGroup,
    HlmInputGroupInput,
    HlmInputGroupAddon,
    ...HlmTabsImports,
    ...HlmTooltipImports,
    FacetedFilter,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
    WebhookStatistics,
    WebhookSubscriptionTable,
    WebhookDeliveryTable,
    WebhookDeliverySheet,
    WebhookSubscriptionDialog,
    WebhookSecretDialog,
    WebhookEnableDialog,
  ],
  providers: [provideIcons({ lucidePlus, lucideRefreshCw, lucideSearch })],
  templateUrl: './webhooks.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Webhooks {
  private readonly service = inject(WebhookService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly confirmService = inject(ConfirmService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly document = inject(DOCUMENT);
  private readonly layoutService = inject(LayoutService);
  private readonly authorizationService = inject(AuthorizationService);
  private readonly accountPreferences = inject(AccountPreferencesService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#else
  protected readonly t = englishText(ENGLISH);
  //#endif

  private readonly queryParams = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });
  private readonly searchSubject = new Subject<string>();
  private readonly listRefresh = new Subject<void>();
  private readonly detailRefresh = new Subject<void>();
  /** 编辑详情请求：打开编辑发 id，打开新建或关闭弹窗发 `null`，新值取消旧请求。 */
  private readonly editDetailRequests = new Subject<string | null>();
  private readonly statisticsRequests = new Subject<WebhookStatisticsQuery>();

  /** 写操作只读 `App.Webhooks.Manage`；前端隐藏只影响体验，后端逐个请求校验。 */
  readonly canManage = computed(() => this.authorizationService.has(PERMISSIONS.webhooks.manage));

  // ---- URL 状态 ----
  readonly view = computed<WebhookView>(() =>
    this.queryParams().get('view') === 'deliveries' ? 'deliveries' : 'subscriptions',
  );
  /**
   * 视图面板与当前标签互相关联：Brain 1.9.0 的标签 id 与 aria-controls 固定为下面的形态，没有覆盖入口；
   * 面板不用 hlmTabsContent，免得在 URL 更新前按 Brain 内部状态先隐藏。形态变了由 spec 的关联断言暴露。
   */
  protected readonly viewPanelId = computed(() => `brn-tabs-content-${this.view()}`);
  protected readonly viewTabId = computed(() => `brn-tabs-label-${this.view()}`);
  readonly preset = computed<RangePreset>(() => {
    const value = this.queryParams().get('range') as RangePreset | null;
    return value && RANGE_PRESETS.includes(value) ? value : '7d';
  });
  readonly pagination = computed(() => paginationFromQuery(this.queryParams()));
  readonly searchQuery = signal(this.route.snapshot.queryParamMap.get('keyword') ?? '');
  readonly selectedEventType = computed(() => this.queryParams().get('eventType'));
  /** 订阅视图的状态筛选：`enabled` / `disabled`。 */
  readonly selectedSubscriptionStatus = computed(() => {
    const value = this.queryParams().get('status');
    return value === 'enabled' || value === 'disabled' ? value : null;
  });
  /** 投递视图的状态筛选。 */
  readonly selectedDeliveryStatus = computed(() => {
    const value = this.queryParams().get('status') as WebhookDeliveryStatus | null;
    return value && DELIVERY_STATUSES.includes(value) ? value : null;
  });
  readonly selectedSubscriptionId = computed(() =>
    this.view() === 'deliveries' ? this.queryParams().get('subscriptionId') : null,
  );
  /** 抽屉里打开的投递。 */
  readonly deliveryId = computed(() =>
    this.view() === 'deliveries' ? this.queryParams().get('delivery') : null,
  );
  /** 最近打开的投递：经后退关闭时参数已先清掉，回焦仍要知道是哪一行。 */
  private readonly lastDeliveryId = linkedSignal<string | null, string | null>({
    source: this.deliveryId,
    computation: (id, previous) => id ?? previous?.value ?? null,
  });
  readonly hasActiveFilters = computed(
    () =>
      this.searchQuery().trim().length > 0 ||
      this.selectedEventType() !== null ||
      (this.view() === 'subscriptions'
        ? this.selectedSubscriptionStatus() !== null
        : this.selectedDeliveryStatus() !== null || this.selectedSubscriptionId() !== null),
  );

  // ---- 时钟与统计 ----
  /** 分钟级时钟：「今天」的判定与相对时间随它更新。 */
  readonly now = signal(Date.now());
  readonly timeZone = computed(() => resolveDisplayTimeZone(this.accountPreferences.timeZone()));
  private readonly today = computed(() => zonedCalendarDate(this.now(), this.timeZone()));
  readonly statistics = signal<WebhookStatisticsOutputDto | null>(null);
  readonly statisticsLoading = signal(false);
  readonly statisticsFailed = signal(false);
  /** 统计请求：区间换算成日历日；投递日志按订阅筛选时只统计该订阅。 */
  private readonly statisticsQuery = computed<WebhookStatisticsQuery>(
    () => ({
      ...presetRange(this.preset(), this.today()),
      timeZone: this.timeZone(),
      ...(this.selectedSubscriptionId() ? { subscriptionId: this.selectedSubscriptionId()! } : {}),
    }),
    { equal: (a, b) => JSON.stringify(a) === JSON.stringify(b) },
  );

  // ---- 列表 ----
  readonly subscriptions = signal<WebhookSubscriptionOutputDto[]>([]);
  readonly deliveries = signal<WebhookDeliveryOutputDto[]>([]);
  readonly totalRecords = signal(0);
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly eventTypes = signal<WebhookEventTypeOutputDto[]>([]);
  readonly eventTypesLoading = signal(false);
  readonly eventTypesError = signal<string | null>(null);
  /** 投递日志的订阅筛选项。 */
  readonly subscriptionOptionsSource = signal<WebhookSubscriptionOutputDto[]>([]);
  readonly subscriptionOptions = computed(() =>
    this.subscriptionOptionsSource().map((item) => ({ label: item.name, value: item.id })),
  );
  readonly selectedSubscriptionName = computed(() => {
    const id = this.selectedSubscriptionId();
    return id
      ? (this.subscriptionOptionsSource().find((item) => item.id === id)?.name ?? null)
      : null;
  });
  readonly eventTypeOptions = computed(() =>
    this.eventTypes().map((type) => ({ label: type.name, value: type.name })),
  );

  //#if (IncludeLocalization)
  private readonly optionTexts = {
    enabled: translateSignal('webhooks.subscriptionStatus.enabled', {}, { scope: 'webhooks' }),
    disabled: translateSignal('webhooks.subscriptionStatus.disabled', {}, { scope: 'webhooks' }),
    pending: translateSignal('webhooks.deliveryStatus.pending', {}, { scope: 'webhooks' }),
    succeeded: translateSignal('webhooks.deliveryStatus.succeeded', {}, { scope: 'webhooks' }),
    failed: translateSignal('webhooks.deliveryStatus.failed', {}, { scope: 'webhooks' }),
  };
  readonly subscriptionStatusOptions = computed(() => [
    { label: this.optionTexts.enabled(), value: 'enabled' },
    { label: this.optionTexts.disabled(), value: 'disabled' },
  ]);
  readonly deliveryStatusOptions = computed(() => [
    { label: this.optionTexts.succeeded(), value: 'succeeded' },
    { label: this.optionTexts.pending(), value: 'pending' },
    { label: this.optionTexts.failed(), value: 'failed' },
  ]);
  //#else
  readonly subscriptionStatusOptions = computed(() => [
    { label: 'Enabled', value: 'enabled' },
    { label: 'Disabled', value: 'disabled' },
  ]);
  readonly deliveryStatusOptions = computed(() => [
    { label: 'Succeeded', value: 'succeeded' },
    { label: 'Retrying', value: 'pending' },
    { label: 'Failed', value: 'failed' },
  ]);
  //#endif

  // ---- 投递详情抽屉 ----
  readonly delivery = signal<WebhookDeliveryDetailOutputDto | null>(null);
  readonly deliveryLoading = signal(false);
  readonly deliveryError = signal<string | null>(null);
  private readonly deliveryTable = viewChild(WebhookDeliveryTable);

  // ---- 行操作与对话框 ----
  /** 正在执行行操作的订阅（测试、轮换、启停）：防重入，并让该行显示进行中。 */
  readonly busySubscriptionId = signal<string | null>(null);
  readonly retryingDeliveryId = signal<string | null>(null);

  readonly editDialogVisible = signal(false);
  readonly editDialogLoading = signal(false);
  readonly editDialogError = signal<string | null>(null);
  readonly editingSubscriptionId = signal<string | null>(null);
  readonly selectedSubscription = signal<WebhookSubscriptionOutputDto | null>(null);

  readonly secretDialogVisible = signal(false);
  readonly secretValue = signal('');
  readonly secretKind = signal<WebhookSecretKind>('created');
  readonly secretSubscriptionName = signal('');

  readonly enableDialogVisible = signal(false);
  readonly enablingSubscription = signal<WebhookSubscriptionOutputDto | null>(null);

  constructor() {
    //#if (IncludeLocalization)
    const title = translateSignal('webhooks.page.title', {}, { scope: 'webhooks' });
    effect(() => this.layoutService.title.set(title()));
    //#else
    this.layoutService.title.set('Webhooks');
    //#endif

    interval(60_000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.now.set(Date.now()));

    this.searchSubject
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe((keyword) => this.updateQuery({ keyword: keyword.trim() || null, page: 1 }, true));
    effect(() => this.searchQuery.set(this.queryParams().get('keyword') ?? ''));

    this.loadEventTypes();

    // 列表：只随列表相关参数与显式刷新重查；视图切换时先清空另一视图的行，免得闪现旧表
    combineLatest([
      this.route.queryParamMap.pipe(
        map((params) => ({
          key: LIST_PARAMS.map((name) => params.get(name) ?? '').join('|'),
          params,
        })),
        distinctUntilChanged((a, b) => a.key === b.key),
        map(({ params }) => params),
      ),
      this.listRefresh.pipe(startWith(undefined)),
    ])
      .pipe(
        switchMap(([params]) =>
          defer(() => {
            this.loading.set(true);
            const deliveriesView = params.get('view') === 'deliveries';
            // 各视图把结果写进自己的行；返回总数
            const request = deliveriesView
              ? this.service.getDeliveries(this.deliveriesQuery(params)).pipe(
                  map((page) => () => {
                    this.deliveries.set(page.items);
                    return page.totalCount;
                  }),
                )
              : this.service.getSubscriptions(this.subscriptionsQuery(params)).pipe(
                  map((page) => () => {
                    this.subscriptions.set(page.items);
                    return page.totalCount;
                  }),
                );
            return request.pipe(
              catchError((error: unknown) => {
                const hasRows = deliveriesView
                  ? this.deliveries().length > 0
                  : this.subscriptions().length > 0;
                if (hasRows) {
                  this.showRequestError(error);
                } else {
                  this.loadError.set(applicationErrorMessage(error));
                }
                return EMPTY;
              }),
              finalize(() => this.loading.set(false)),
            );
          }),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((apply) => {
        this.loadError.set(null);
        this.totalRecords.set(apply());
      });

    // 视图切到投递日志时取一次订阅筛选项
    effect(() => {
      if (this.view() === 'deliveries') {
        untracked(() => this.loadSubscriptionOptions());
      }
    });

    // 统计：区间、时区或订阅筛选变化时取；只保留最后一次请求，失败时保留已有数据
    this.statisticsRequests
      .pipe(
        switchMap((query) => this.service.getStatistics(query).pipe(catchError(() => of(null)))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((statistics) => {
        this.statisticsLoading.set(false);
        if (statistics) {
          this.statistics.set(statistics);
        } else {
          this.statisticsFailed.set(true);
        }
      });
    effect(() => {
      const query = this.statisticsQuery();
      untracked(() => this.requestStatistics(query));
    });

    // 投递详情：打开的 id 变化或重试时取；换 id 丢弃迟到响应
    combineLatest([toObservable(this.deliveryId), this.detailRefresh.pipe(startWith(undefined))])
      .pipe(
        switchMap(([id]) => {
          if (!id) {
            this.delivery.set(null);
            this.deliveryError.set(null);
            return EMPTY;
          }
          return defer(() => {
            if (this.delivery()?.id !== id) {
              this.delivery.set(null);
            }
            this.deliveryLoading.set(true);
            this.deliveryError.set(null);
            return this.service.getDelivery(id).pipe(
              catchError((error: unknown) => {
                this.deliveryError.set(applicationErrorMessage(error));
                return EMPTY;
              }),
              finalize(() => this.deliveryLoading.set(false)),
            );
          });
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((delivery) => this.delivery.set(delivery));

    // 编辑详情：切换目标、打开新建或关闭弹窗都取消旧请求，迟到响应不会填进另一订阅或新建表单
    merge(
      this.editDetailRequests,
      toObservable(this.editDialogVisible).pipe(
        filter((visible) => !visible),
        map(() => null),
      ),
    )
      .pipe(
        switchMap((id) => {
          if (!id) {
            this.editDialogLoading.set(false);
            this.editDialogError.set(null);
            return EMPTY;
          }
          return defer(() => {
            this.editDialogLoading.set(true);
            this.editDialogError.set(null);
            return this.service.getSubscription(id).pipe(
              catchError((error: unknown) => {
                this.editDialogError.set(applicationErrorMessage(error));
                return EMPTY;
              }),
              finalize(() => this.editDialogLoading.set(false)),
            );
          });
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((subscription) => this.selectedSubscription.set(subscription));
  }

  // ---- 视图、区间与筛选 ----
  setView(view: string): void {
    if (view === this.view()) {
      return;
    }
    // 两个视图的筛选各自独立：切换时只保留区间
    this.navigate({ view: view === 'deliveries' ? 'deliveries' : null, range: this.rangeParam() });
  }

  setPreset(preset: RangePreset): void {
    this.updateQuery({ range: preset === '7d' ? null : preset });
  }

  onSearchQueryChange(value: string): void {
    this.searchQuery.set(value);
    this.searchSubject.next(value);
  }

  onFilterChange(name: 'status' | 'eventType' | 'subscriptionId', value: unknown): void {
    this.updateQuery({ [name]: typeof value === 'string' && value ? value : null, page: 1 });
  }

  onPaginationChange(pagination: PaginationState): void {
    this.updateQuery({ page: pagination.pageIndex + 1, pageSize: pagination.pageSize });
  }

  reload(): void {
    this.listRefresh.next();
    this.requestStatistics(this.statisticsQuery());
  }

  reloadStatistics(): void {
    this.requestStatistics(this.statisticsQuery());
  }

  /** 最近投递徽章与"查看投递"：投递日志按该订阅筛选。 */
  viewDeliveries(subscriptionId: string): void {
    this.navigate({ view: 'deliveries', subscriptionId, range: this.rangeParam() });
  }

  /** 失败卡：投递日志筛选失败，保留当前的订阅筛选。 */
  showFailedDeliveries(): void {
    this.navigate({
      view: 'deliveries',
      status: 'failed',
      subscriptionId: this.selectedSubscriptionId(),
      range: this.rangeParam(),
    });
  }

  /** 打开详情新增一条历史：浏览器后退即关闭抽屉（frontend-ui §2.7）。 */
  openDelivery(id: string): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { delivery: id },
      queryParamsHandling: 'merge',
      state: { [DELIVERY_OPENED_STATE]: true },
    });
  }

  /**
   * 抽屉关闭完毕（关闭按钮、Esc 或随导航关闭）。地址里还有 `delivery` 时由本方法收掉：从列表打开的条目（含刷新、
   * 离开再前进回来）退回打开前那条历史，不新增条目，后退不会重开、前进仍可重开；外部深链没有标记，移除参数并替换当前条目。
   * 随后回焦打开它的行入口。
   */
  closeDelivery(): void {
    const id = this.lastDeliveryId();
    // 读地址栏而不是路由状态：随后退关闭时路由还没处理完 popstate，地址栏已是目标条目
    if (this.router.parseUrl(this.location.path()).queryParamMap.has('delivery')) {
      const state = this.location.getState() as Record<string, unknown> | null;
      if (state?.[DELIVERY_OPENED_STATE] === true) {
        this.location.back();
      } else {
        this.updateQuery({ delivery: null }, true);
      }
    }
    this.focusDeliveryEntry(id);
  }

  reloadDelivery(): void {
    this.detailRefresh.next();
  }

  loadEventTypes(): void {
    if (this.eventTypesLoading()) return;
    this.eventTypesLoading.set(true);
    this.eventTypesError.set(null);
    this.service
      .getEventTypes()
      .pipe(
        finalize(() => this.eventTypesLoading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (types) => this.eventTypes.set(types),
        error: (error) => this.eventTypesError.set(applicationErrorMessage(error)),
      });
  }

  // ---- 订阅写操作 ----
  openCreateDialog(): void {
    this.editingSubscriptionId.set(null);
    this.selectedSubscription.set(null);
    this.editDetailRequests.next(null);
    this.editDialogVisible.set(true);
  }

  openEditDialog(id: string): void {
    this.editingSubscriptionId.set(id);
    this.selectedSubscription.set(null);
    this.editDetailRequests.next(id);
    this.editDialogVisible.set(true);
  }

  retrySubscriptionDetail(): void {
    const id = this.editingSubscriptionId();
    if (id) this.editDetailRequests.next(id);
  }

  readonly handleSave = (input: CreateWebhookSubscriptionInputDto) => {
    const editingId = this.editingSubscriptionId();
    if (editingId) {
      return this.service.updateSubscription(editingId, input).pipe(
        tap(() => {
          this.notify('webhooks.toast.updated', 'Subscription updated');
          this.editDialogVisible.set(false);
          this.reload();
        }),
      );
    }
    return this.service.createSubscription(input).pipe(
      tap((result) => {
        this.notify('webhooks.toast.created', 'Subscription created');
        this.editDialogVisible.set(false);
        this.revealSecret('created', result.subscription.name, result.secret);
        this.reload();
      }),
    );
  };

  onSecretDialogVisibleChange(visible: boolean): void {
    // 关闭即清空明文，下次误打开也不会显示上一次的密钥
    if (!visible) {
      this.secretValue.set('');
      this.secretSubscriptionName.set('');
    }
    this.secretDialogVisible.set(visible);
  }

  async sendTestEvent(id: string): Promise<void> {
    if (this.busySubscriptionId() !== null) return;
    this.busySubscriptionId.set(id);
    try {
      await firstValueFrom(this.service.sendTestEvent(id));
      this.notify('webhooks.toast.testSent', 'Test event queued');
      this.reload();
    } catch (error: unknown) {
      this.showRequestError(error);
    } finally {
      this.busySubscriptionId.set(null);
    }
  }

  async rotateSecret(id: string): Promise<void> {
    if (this.busySubscriptionId() !== null) return;
    this.busySubscriptionId.set(id);
    try {
      const confirmed = await this.confirmService.open({
        //#if (IncludeLocalization)
        header: this.transloco.translate('webhooks.confirm.rotateHeader'),
        message: this.transloco.translate('webhooks.confirm.rotateMessage'),
        confirmText: this.transloco.translate('webhooks.action.rotateSecret'),
        cancelText: this.transloco.translate('common.cancel'),
        //#else
        header: 'Rotate signing secret?',
        message:
          'A new secret is generated now. The previous secret keeps signing alongside it for a transition period, so receivers can switch without dropping requests.',
        confirmText: 'Rotate secret',
        //#endif
      });
      if (!confirmed) return;
      try {
        const result = await firstValueFrom(this.service.rotateSecret(id));
        this.revealSecret('rotated', result.subscription.name, result.secret);
        this.listRefresh.next();
      } catch (error: unknown) {
        this.showRequestError(error);
      }
    } finally {
      this.busySubscriptionId.set(null);
    }
  }

  async disableSubscription(id: string): Promise<void> {
    if (this.busySubscriptionId() !== null) return;
    this.busySubscriptionId.set(id);
    try {
      const confirmed = await this.confirmService.open({
        owner: this.destroyRef,
        action: () => this.service.disableSubscription(id),
        //#if (IncludeLocalization)
        header: this.transloco.translate('webhooks.confirm.disableHeader'),
        message: this.transloco.translate('webhooks.confirm.disableMessage'),
        confirmText: this.transloco.translate('webhooks.action.disable'),
        cancelText: this.transloco.translate('common.cancel'),
        //#else
        header: 'Disable subscription?',
        message:
          'Deliveries waiting to be retried are marked as failed, and events that occur while it is disabled are not sent later.',
        confirmText: 'Disable',
        //#endif
        variant: 'destructive',
      });
      if (!confirmed) return;
      this.notify('webhooks.toast.disabled', 'Subscription disabled');
      this.reload();
    } finally {
      this.busySubscriptionId.set(null);
    }
  }

  openEnableDialog(subscription: WebhookSubscriptionOutputDto): void {
    this.enablingSubscription.set(subscription);
    this.enableDialogVisible.set(true);
  }

  async enableSubscription(retryFailed: boolean): Promise<void> {
    const subscription = this.enablingSubscription();
    if (!subscription || this.busySubscriptionId() !== null) return;
    this.busySubscriptionId.set(subscription.id);
    try {
      await firstValueFrom(this.service.enableSubscription(subscription.id, retryFailed));
      this.notify('webhooks.toast.enabled', 'Subscription enabled');
      this.reload();
    } catch (error: unknown) {
      this.showRequestError(error);
    } finally {
      this.busySubscriptionId.set(null);
    }
  }

  async deleteSubscription(subscription: WebhookSubscriptionOutputDto): Promise<void> {
    if (this.busySubscriptionId() !== null) return;
    this.busySubscriptionId.set(subscription.id);
    try {
      const confirmed = await this.confirmService.open({
        owner: this.destroyRef,
        action: () => this.service.deleteSubscription(subscription.id),
        //#if (IncludeLocalization)
        header: this.transloco.translate('webhooks.confirm.deleteHeader'),
        message: this.transloco.translate('webhooks.confirm.deleteMessage', {
          name: subscription.name,
        }),
        confirmText: this.transloco.translate('common.delete'),
        cancelText: this.transloco.translate('common.cancel'),
        //#else
        header: 'Delete subscription?',
        message: `Delete "${subscription.name}"? Its delivery log is deleted with it and no longer counts in statistics.`,
        confirmText: 'Delete',
        //#endif
        variant: 'destructive',
      });
      if (!confirmed) return;
      this.notify('webhooks.toast.deleted', 'Subscription deleted');
      this.reload();
    } finally {
      this.busySubscriptionId.set(null);
    }
  }

  // ---- 投递写操作 ----
  async retryDelivery(delivery: Pick<WebhookDeliveryOutputDto, 'id' | 'status'>): Promise<void> {
    if (delivery.status === 'pending' || this.retryingDeliveryId() !== null) return;
    this.retryingDeliveryId.set(delivery.id);
    try {
      await firstValueFrom(this.service.retryDelivery(delivery.id));
      this.notify('webhooks.toast.retried', 'Delivery queued for resending');
      this.reload();
      if (this.deliveryId() === delivery.id) {
        this.detailRefresh.next();
      }
    } catch (error: unknown) {
      this.showRequestError(error);
    } finally {
      this.retryingDeliveryId.set(null);
    }
  }

  private revealSecret(kind: WebhookSecretKind, name: string, secret: string): void {
    this.secretKind.set(kind);
    this.secretSubscriptionName.set(name);
    this.secretValue.set(secret);
    this.secretDialogVisible.set(true);
  }

  private loadSubscriptionOptions(): void {
    this.service
      .getSubscriptions({ offset: 0, limit: SUBSCRIPTION_OPTION_LIMIT })
      .pipe(
        catchError(() => EMPTY),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((page) => this.subscriptionOptionsSource.set(page.items));
  }

  private requestStatistics(query: WebhookStatisticsQuery): void {
    this.statisticsLoading.set(true);
    this.statisticsFailed.set(false);
    this.statisticsRequests.next(query);
  }

  private subscriptionsQuery(params: ParamMap): GetWebhookSubscriptionsInputDto {
    const pagination = paginationFromQuery(params);
    const status = params.get('status');
    return {
      offset: pagination.pageIndex * pagination.pageSize,
      limit: pagination.pageSize,
      keyword: params.get('keyword') || undefined,
      isEnabled: status === 'enabled' ? true : status === 'disabled' ? false : undefined,
      eventType: params.get('eventType') || undefined,
    };
  }

  private deliveriesQuery(params: ParamMap): GetWebhookDeliveriesInputDto {
    const pagination = paginationFromQuery(params);
    const status = params.get('status') as WebhookDeliveryStatus | null;
    return {
      offset: pagination.pageIndex * pagination.pageSize,
      limit: pagination.pageSize,
      keyword: params.get('keyword') || undefined,
      subscriptionId: params.get('subscriptionId') || undefined,
      status: status && DELIVERY_STATUSES.includes(status) ? status : undefined,
      eventType: params.get('eventType') || undefined,
    };
  }

  /** 回焦该投递所在行的"详情"按钮；行不在当前页（深链到别页的投递）时回到"投递日志"标签。 */
  private focusDeliveryEntry(id: string | null): void {
    if (id && this.deliveryTable()?.focusDetails(id)) {
      return;
    }
    this.document.getElementById(this.viewTabId())?.focus();
  }

  private rangeParam(): RangePreset | null {
    return this.preset() === '7d' ? null : this.preset();
  }

  private updateQuery(queryParams: Params, replaceUrl = false): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      queryParamsHandling: 'merge',
      replaceUrl,
    });
  }

  /** 换一组查询参数（不合并）：视图切换与跳转时丢掉另一视图的筛选与页码。 */
  private navigate(queryParams: Params): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams });
  }

  //#if (IncludeLocalization)
  private notify(key: string, _english: string): void {
    toast.success(this.transloco.translate('common.success'), {
      description: this.transloco.translate(key),
    });
  }
  //#else
  private notify(_key: string, english: string): void {
    toast.success('Success', { description: english });
  }
  //#endif

  private showRequestError(error: unknown): void {
    //#if (IncludeLocalization)
    toast.error(this.transloco.translate('common.requestError'), {
      description: applicationErrorMessage(error),
    });
    //#else
    toast.error('Request failed', { description: applicationErrorMessage(error) });
    //#endif
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'common.cancel': 'Cancel',
  'common.clearFilter': 'Clear filter',
  'common.delete': 'Delete',
  'common.noResults': 'No results',
  'common.refresh': 'Refresh',
  'common.requestError': 'Request error',
  'common.success': 'Success',
  'webhooks.action.create': 'New subscription',
  'webhooks.action.disable': 'Disable',
  'webhooks.action.rotateSecret': 'Rotate secret',
  'webhooks.confirm.deleteHeader': 'Delete subscription?',
  'webhooks.confirm.deleteMessage':
    'Delete "{{name}}"? Its delivery log is deleted with it and no longer counts in statistics.',
  'webhooks.confirm.disableHeader': 'Disable subscription?',
  'webhooks.confirm.disableMessage':
    'Deliveries waiting to be retried are marked as failed, and events that occur while it is disabled are not sent later.',
  'webhooks.confirm.rotateHeader': 'Rotate signing secret?',
  'webhooks.confirm.rotateMessage':
    'A new secret is generated now. The previous secret keeps signing alongside it for a transition period, so receivers can switch without dropping requests.',
  'webhooks.deliveryStatus.failed': 'Failed',
  'webhooks.deliveryStatus.pending': 'Retrying',
  'webhooks.deliveryStatus.succeeded': 'Succeeded',
  'webhooks.filter.eventType': 'Event type',
  'webhooks.filter.searchDeliveries': 'Search by event ID or type...',
  'webhooks.filter.searchSubscriptions': 'Search by name or URL...',
  'webhooks.filter.status': 'Status',
  'webhooks.filter.subscription': 'Subscription',
  'webhooks.page.title': 'Webhooks',
  'webhooks.subscriptionStatus.disabled': 'Disabled',
  'webhooks.subscriptionStatus.enabled': 'Enabled',
  'webhooks.toast.created': 'Subscription created',
  'webhooks.toast.deleted': 'Subscription deleted',
  'webhooks.toast.disabled': 'Subscription disabled',
  'webhooks.toast.enabled': 'Subscription enabled',
  'webhooks.toast.retried': 'Delivery queued for resending',
  'webhooks.toast.testSent': 'Test event queued',
  'webhooks.toast.updated': 'Subscription updated',
  'webhooks.view.deliveries': 'Delivery log',
  'webhooks.view.label': 'View',
  'webhooks.view.subscriptions': 'Subscriptions',
};
//#endif
