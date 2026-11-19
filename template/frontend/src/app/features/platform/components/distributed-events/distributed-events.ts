import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Params, Router } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoDirective, TranslocoService, translateSignal } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCircleAlert,
  lucideInbox,
  lucideRefreshCw,
  lucideSearch,
  lucideSend,
  lucideX,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCheckbox } from '@spartan-ng/helm/checkbox';
// 日期区间输入框自带日历按钮及其图标，本页不必登记 lucideCalendar。
import { HlmDateRangeInput, HlmDateRangePicker } from '@spartan-ng/helm/date-picker';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmInputGroupImports } from '@spartan-ng/helm/input-group';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTabsImports } from '@spartan-ng/helm/tabs';
import { HlmToggleGroupImports } from '@spartan-ng/helm/toggle-group';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import {
  Observable,
  Subject,
  catchError,
  combineLatest,
  finalize,
  forkJoin,
  interval,
  of,
  startWith,
  switchMap,
  tap,
} from 'rxjs';

import { applicationErrorMessage } from '../../../../core/errors/application-http-error';
import { AccountPreferencesService } from '../../../../core/services/account-preferences-service';
//#if (!(TenantWorkspace))
import { AuthService } from '../../../../core/services/auth-service';
//#endif
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { LayoutService } from '../../../../core/services/layout-service';
//#if (TenantWorkspace)
import { WorkspaceContextService } from '../../../../core/services/workspace-context-service';
//#endif
import { FacetedFilter } from '../../../../shared/components/faceted-filter/faceted-filter';
import { RelativeTimeLabel } from '../../../../shared/components/relative-time-label/relative-time-label';
import { PERMISSIONS } from '../../../../shared/constants/permission.constants';
import { StatusDot } from '../../../../shared/directives/status-dot';
import { PagedResultDto } from '../../../../shared/dtos/paged-result.dto';
import { formatAppDate } from '../../../../shared/pipes/app-date-pipe';
import { AppNumber } from '../../../../shared/pipes/app-number-pipe';
import {
  dateRangeFormatter,
  dateRangeFromQuery,
  dateRangeParser,
  dateRangeToQuery,
  validTimeRange,
} from '../../../../shared/utils/date-range-query';
//#if (!IncludeLocalization)
import { englishText } from '../../../../shared/utils/english-text';
//#endif
import { resolveDisplayTimeZone } from '../../../../shared/utils/zoned-time';
import * as Dto from '../../dtos/distributed-event.dto';
import { DistributedEventService } from '../../services/distributed-event-service';
// prettier-ignore
import {
  EventRecord,
  EventView,
  EventRow,
  canDiscard,
  canRecover,
  eventRow,
  recoveryAction,
  reasonKey,
  stateLabelKey,
  //#if (!IncludeLocalization)
  ENGLISH,
  //#endif
} from './distributed-event-display';
import { DistributedCatalogTable } from './widgets/distributed-catalog-table/distributed-catalog-table';
import { DistributedEventRecordSheet } from './widgets/distributed-event-record-sheet/distributed-event-record-sheet';
import { DistributedEventRecoveryConfirmService } from './widgets/distributed-event-recovery-confirm/distributed-event-recovery-confirm';
import { DistributedEventStatistics } from './widgets/distributed-event-statistics/distributed-event-statistics';
import { DistributedInboxTable } from './widgets/distributed-inbox-table/distributed-inbox-table';
import { DistributedOutboxTable } from './widgets/distributed-outbox-table/distributed-outbox-table';
import { DistributedRejectionTable } from './widgets/distributed-rejection-table/distributed-rejection-table';

/** 影响列表结果的筛选参数，与视图、分页、时间范围一起组成加载条件。 */
const LIST_FILTER_KEYS = [
  'status',
  'eventName',
  'keyword',
  'errorCode',
  'consumer',
  'reason',
  'direction',
  'anomalies',
];

@Component({
  selector: 'app-distributed-events',
  imports: [
    NgIcon,
    HlmBadge,
    HlmButton,
    HlmCheckbox,
    HlmDateRangePicker,
    HlmDateRangeInput,
    ...HlmEmptyImports,
    ...HlmInputGroupImports,
    ...HlmSelectImports,
    HlmSpinner,
    ...HlmTabsImports,
    ...HlmToggleGroupImports,
    ...HlmTooltipImports,
    FacetedFilter,
    RelativeTimeLabel,
    StatusDot,
    AppNumber,
    DistributedEventStatistics,
    DistributedOutboxTable,
    DistributedInboxTable,
    DistributedRejectionTable,
    DistributedCatalogTable,
    DistributedEventRecordSheet,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [
    provideIcons({
      lucideCircleAlert,
      lucideInbox,
      lucideRefreshCw,
      lucideSearch,
      lucideSend,
      lucideX,
    }),
  ],
  templateUrl: './distributed-events.html',
  host: { class: 'block h-full min-w-0' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DistributedEvents {
  private readonly service = inject(DistributedEventService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly owner = inject(DestroyRef);
  private readonly authorization = inject(AuthorizationService);
  //#if (TenantWorkspace)
  private readonly workspace = inject(WorkspaceContextService);
  //#else
  private readonly auth = inject(AuthService);
  //#endif
  private readonly confirm = inject(DistributedEventRecoveryConfirmService);
  private readonly preferences = inject(AccountPreferencesService);
  private readonly layout = inject(LayoutService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  private readonly title = translateSignal('distributedEvents.title');
  protected readonly t = (key: string, params?: Record<string, unknown>) =>
    this.transloco.translate(key, params);
  //#else
  protected readonly t = englishText(ENGLISH);
  //#endif
  //#if (TenantWorkspace)
  readonly isHost = computed(() => !this.workspace.tenantId());
  //#else
  readonly isHost = computed(() => !this.auth.currentUser()?.tenantId);
  //#endif
  readonly canManage = computed(() => this.authorization.has(PERMISSIONS.distributedEvents.manage));
  readonly query = toSignal(this.route.queryParams, {
    initialValue: this.route.snapshot.queryParams,
  });
  readonly view = computed<EventView>(() => {
    const value = this.query()['view'];
    return value === 'inbox' || value === 'catalog' || (value === 'rejections' && this.isHost())
      ? value
      : 'outbox';
  });
  readonly recordId = computed<string | null>(() => this.query()['record'] || null);
  readonly page = computed(() => Math.max(0, Number(this.query()['page']) || 0));
  readonly pageSize = computed(() =>
    [10, 20, 50, 100].includes(Number(this.query()['pageSize']))
      ? Number(this.query()['pageSize'])
      : 10,
  );
  readonly cutoff = signal(new Date().toISOString());
  /**
   * 最后一次成功加载所用的截止时间：表格与总数都出自这次加载，按条件恢复的 before 与确认框都以它为准。
   * 刷新会先推进 cutoff，失败时保留的仍是旧截止下的结果。
   */
  readonly loadedCutoff = signal<string | null>(null);
  readonly rows = signal<EventRow[]>([]);
  readonly totalCount = signal(0);
  readonly status = signal<Dto.DistributedEventStatusOutputDto | null>(null);
  readonly catalog = signal<Dto.DistributedEventCatalogOutputDto[]>([]);
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly selected = signal<string[]>([]);
  readonly busy = signal(false);
  readonly displayTimeZone = this.preferences.timeZone;
  readonly displayLocale = this.preferences.displayLocale;
  /** 传输行相对时间的分钟级时钟；每次加载也对一次时，刷新后立即反映最新上报。 */
  readonly now = signal(Date.now());
  readonly relativeTimeZone = computed(() => resolveDisplayTimeZone(this.displayTimeZone()));
  /** URL 里合法的时间范围；只有一端、格式错误或起止颠倒时为 null，控件、请求与按条件恢复都当作未筛选。 */
  readonly timeRange = computed(() =>
    validTimeRange(this.query()['startTime'], this.query()['endTime']),
  );
  readonly selectedRange = computed<[Date, Date] | undefined>(() =>
    dateRangeFromQuery(this.query()['startTime'], this.query()['endTime'], this.displayTimeZone()),
  );
  readonly formatRange = computed(() => dateRangeFormatter(this.displayLocale()));
  readonly parseRangeInput = computed(() => dateRangeParser(this.displayLocale()));
  private readonly refresh = new Subject<void>();
  readonly detailVersion = signal(0);
  readonly canTest = computed(
    () => this.isHost() && this.canManage() && this.status()?.testEventEnabled,
  );
  readonly bulkAllowed = computed(() =>
    this.view() === 'outbox'
      ? this.query()['status'] === 'deadLettered'
      : this.view() === 'inbox'
        ? ['failed', 'processed'].includes(this.query()['status'])
        : this.view() === 'rejections' && this.query()['status'] === 'pending',
  );
  readonly bulkDiscardAllowed = computed(
    () =>
      this.view() === 'rejections' &&
      ['pending', 'redecodePending'].includes(this.query()['status']),
  );
  /** 勾选里符合各动作的记录 ID；拒收的重新解码与丢弃各自只统计、只提交适用的记录。 */
  readonly selectedForRecovery = computed(() => this.selectedFor(false));
  readonly selectedForDiscard = computed(() => this.selectedFor(true));
  /**
   * 按条件恢复只针对屏幕上那次成功加载的结果：列表请求进行中、加载失败或尚无结果时都不可用，
   * 免得用新条件配旧总数打开确认框。按勾选 ID 恢复不受影响。
   */
  readonly filterRecoveryReady = computed(
    () => !this.loading() && !this.loadError() && this.loadedCutoff() !== null,
  );
  /** 选择条：有勾选，或状态筛选满足按条件恢复时出现。 */
  readonly selectionBarVisible = computed(
    () =>
      this.canManage() &&
      this.view() !== 'catalog' &&
      (this.selected().length > 0 || this.bulkAllowed() || this.bulkDiscardAllowed()),
  );
  /** 状态快捷切换的当前值；URL 里不属于本视图的状态视为未选。 */
  readonly statusFilter = computed(() =>
    this.states().includes(this.query()['status']) ? (this.query()['status'] as string) : null,
  );
  readonly catalogDetail = computed(() =>
    this.view() === 'catalog'
      ? (this.catalog().find((item) => item.eventName === this.recordId()) ?? null)
      : null,
  );
  readonly states = computed(() =>
    this.view() === 'outbox'
      ? ['pending', 'sent', 'deadLettered']
      : this.view() === 'inbox'
        ? ['pending', 'processed', 'failed']
        : ['pending', 'redecoded', 'discarded', 'redecodePending'],
  );
  readonly facetKeys = computed(() =>
    this.view() === 'catalog'
      ? ['direction']
      : [
          'eventName',
          ...(this.view() === 'rejections' ? ['reason'] : ['errorCode']),
          ...(this.view() === 'inbox' ? ['consumer'] : []),
        ],
  );

  constructor() {
    effect(() => {
      //#if (IncludeLocalization)
      this.layout.title.set(this.title());
      //#else
      this.layout.title.set(this.t('distributedEvents.title'));
      //#endif
    });
    let renderedView = this.view();
    // 上次成功加载的列表条件；刷新失败时只有条件未变才保留旧行
    let loadedKey: string | null = null;
    const query$ = this.route.queryParams;
    combineLatest([query$, this.refresh.pipe(startWith(undefined))])
      .pipe(
        switchMap(([query]) => {
          this.loading.set(true);
          this.loadError.set(null);
          this.selected.set([]);
          const view = this.view();
          if (view !== renderedView) this.rows.set([]);
          renderedView = view;
          const key = this.listKey(query);
          const input = this.listInput(query);
          const list$: Observable<PagedResultDto<EventRecord>> =
            this.view() === 'catalog'
              ? of({ items: [], totalCount: 0 })
              : this.view() === 'outbox'
                ? this.service.getOutboxList(input as Dto.GetDistributedEventOutboxInputDto)
                : this.view() === 'inbox'
                  ? this.service.getInboxList(input as Dto.GetDistributedEventInboxInputDto)
                  : this.service.getRejectionList(
                      input as Dto.GetDistributedEventRejectionsInputDto,
                    );
          return forkJoin({
            status: this.service.getStatus(),
            catalog: this.service.getCatalog(),
            list: list$,
          }).pipe(
            tap((result) => {
              loadedKey = key;
              this.loadedCutoff.set(String(input['before']));
              this.now.set(Date.now());
              this.status.set(result.status);
              this.catalog.set(result.catalog);
              if (this.view() === 'catalog') {
                const catalog = result.catalog
                  .filter(
                    (item) =>
                      !query['direction'] ||
                      (query['direction'] === 'outgoing' ? item.outgoing : item.incoming),
                  )
                  .filter((item) => !query['keyword'] || item.eventName.includes(query['keyword']))
                  .filter(
                    (item) =>
                      query['anomalies'] !== 'true' || item.deadLettered + item.failedInbox > 0,
                  );
                this.totalCount.set(catalog.length);
                this.rows.set(
                  catalog
                    .slice(this.page() * this.pageSize(), (this.page() + 1) * this.pageSize())
                    .map((item) => ({
                      id: item.eventName,
                      eventName: item.eventName,
                      eventId: '',
                      status: '',
                      orderingKey: '',
                      consumer: item.consumers.join(', ') || '—',
                      attempts: 0,
                      error: item.lastErrorCode || '—',
                      next: null,
                      time: item.lastSentAt ?? null,
                      catalog: item,
                    })),
                );
              } else {
                this.rows.set(result.list.items.map(eventRow));
                this.totalCount.set(result.list.totalCount);
              }
            }),
            catchError((error: unknown) => {
              // 同条件刷新失败保留表格并提示；条件变了（或首次加载）失败则清空并进入可重试的空态，
              // 免得旧条件下的行与总数被当成新条件的结果，按条件恢复拿旧总数去确认新条件
              if (this.rows().length && key === loadedKey) {
                toast.error(applicationErrorMessage(error));
              } else {
                loadedKey = null;
                this.loadedCutoff.set(null);
                this.rows.set([]);
                this.totalCount.set(0);
                this.loadError.set(applicationErrorMessage(error));
              }
              return of(null);
            }),
            finalize(() => this.loading.set(false)),
          );
        }),
        takeUntilDestroyed(this.owner),
      )
      .subscribe();
    effect(() => {
      if (!this.canManage()) this.selected.set([]);
    });
    interval(60_000)
      .pipe(takeUntilDestroyed(this.owner))
      .subscribe(() => this.now.set(Date.now()));
  }

  /** 决定列表结果的条件；打开详情（record）不算。视图、分页与时间范围取解析后的值。 */
  private listKey(query: Params): string {
    return JSON.stringify([
      this.view(),
      this.page(),
      this.pageSize(),
      validTimeRange(query['startTime'], query['endTime']),
      ...LIST_FILTER_KEYS.map((key) => query[key] ?? null),
    ]);
  }

  listInput(query: Params = this.query()): Record<string, unknown> {
    return {
      ...validTimeRange(query['startTime'], query['endTime']),
      status: query['status'],
      eventName: query['eventName'],
      keyword: query['keyword'],
      errorCode: query['errorCode'],
      consumer: query['consumer'],
      reason: query['reason'],
      before: this.cutoff(),
      offset: this.page() * this.pageSize(),
      limit: this.pageSize(),
    };
  }

  update(query: Params, reset = true): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { ...(reset ? { page: null, record: null } : {}), ...query },
      queryParamsHandling: 'merge',
    });
  }

  setFilter(key: string, value: string | boolean | null): void {
    this.update({ [key]: value });
  }

  /** 切换视图、跳转与清除筛选都清掉时间范围：新视图从全部时间看起。 */
  setView(view: string): void {
    this.update({
      view,
      startTime: null,
      endTime: null,
      status: null,
      eventName: null,
      errorCode: null,
      reason: null,
      consumer: null,
      keyword: null,
      anomalies: null,
      direction: null,
    });
  }

  jump(view: EventView, status: string, eventName?: string): void {
    this.update({
      view,
      status,
      startTime: null,
      endTime: null,
      eventName: eventName || null,
      keyword: null,
      errorCode: null,
      reason: null,
      consumer: null,
      anomalies: null,
      direction: null,
    });
  }

  /** 日期按展示时区取整天换算成 UTC 写回 URL；页码随 update 回到第一页。 */
  onRangeChange(range: [Date, Date] | null): void {
    this.update(dateRangeToQuery(range, this.displayTimeZone()));
  }

  reload(): void {
    this.cutoff.set(new Date().toISOString());
    this.refresh.next();
    this.detailVersion.update((version) => version + 1);
  }
  openRecord(id: string): void {
    this.update({ record: id }, false);
  }
  closeDetail(): void {
    this.update({ record: null }, false);
  }
  changePage(page: number): void {
    this.update({ page, record: null }, false);
  }
  action(): string {
    return recoveryAction(this.view());
  }
  protected reasonLabel(reason: string): string {
    const key = reasonKey(reason);
    return key ? this.t(key) : reason;
  }
  protected stateText(state: string): string {
    return this.t(stateLabelKey(this.view(), state));
  }
  protected healthVariant(status: Dto.DistributedEventTransportStatus) {
    return status === 'healthy' ? 'success' : status === 'unknown' ? 'secondary' : 'warning';
  }
  private selectedFor(discard: boolean): string[] {
    const ids = new Set(this.selected());
    return this.rows()
      .filter(
        (row) =>
          ids.has(row.id) &&
          !!row.record &&
          (discard ? canDiscard(row.record) : canRecover(row.record)),
      )
      .map((row) => row.id);
  }
  options(key: string) {
    const values =
      key === 'direction'
        ? ['outgoing', 'incoming']
        : key === 'status'
          ? this.states()
          : key === 'eventName'
            ? this.catalog().map((item) => item.eventName)
            : key === 'consumer'
              ? this.catalog().flatMap((item) => item.consumers)
              : key === 'reason'
                ? ['invalid-envelope', 'unknown-tenant', 'invalid-payload']
                : [
                    ...this.rows().map((row) => row.error),
                    'message-too-large',
                    'retry-window-exceeded',
                    'handler-exception',
                  ];
    return [
      ...new Set(
        values
          .filter((value) => value && value !== '—')
          .concat(this.query()[key] ? [this.query()[key]] : []),
      ),
    ].map((value) => ({
      value,
      label:
        key === 'direction'
          ? this.t(`distributedEvents.${value}`)
          : key === 'status'
            ? this.t(`distributedEvents.state.${value}`)
            : key === 'reason'
              ? this.reasonLabel(value)
              : value,
    }));
  }

  async recover(
    mode: 'selected' | 'filter' | 'single',
    row?: EventRecord,
    discard = false,
  ): Promise<void> {
    if (!this.canManage() || this.busy()) return;
    const view = this.view();
    if (view === 'catalog') return;
    const ids = row ? [row.id] : this.selectedFor(discard);
    if (
      (mode !== 'filter' && !ids.length) ||
      (mode === 'filter' &&
        (!(discard ? this.bulkDiscardAllowed() : this.bulkAllowed()) ||
          !this.filterRecoveryReady()))
    )
      return;
    const action = discard
      ? 'discard'
      : view === 'outbox'
        ? 'redeliver'
        : view === 'inbox'
          ? 'replay'
          : 'redecode';
    // 不在加载中且没有失败时，当前筛选就是最后一次成功加载的筛选；截止取那次加载的值
    const loadedCutoff = this.loadedCutoff() ?? this.cutoff();
    const input: Record<string, unknown> = { ...this.listInput(), before: loadedCutoff };
    const { offset: _offset, limit: _limit, status: _status, ...filter } = input;
    const body =
      mode === 'filter'
        ? {
            filter: {
              ...filter,
              ...(view === 'inbox' || view === 'rejections'
                ? { status: this.query()['status'] }
                : {}),
            },
          }
        : { ids };
    const count = mode === 'filter' ? this.totalCount() : ids.length;
    const criteria = ['status', 'eventName', 'keyword', 'errorCode', 'consumer', 'reason']
      .filter((key) => this.query()[key])
      .map((key) => {
        const label =
          key === 'keyword'
            ? this.t('distributedEvents.search')
            : this.t(`distributedEvents.field.${key}`);
        const value =
          key === 'status'
            ? this.stateText(this.query()[key])
            : key === 'reason'
              ? this.reasonLabel(this.query()[key])
              : String(this.query()[key]);
        return `${label}: ${value}`;
      })
      .concat(this.rangeCriterion())
      .join(' · ');
    this.busy.set(true);
    try {
      await this.confirm.open({
        owner: this.owner,
        header: this.t(
          `distributedEvents.confirmation.${mode === 'filter' ? 'filterQuestion' : mode === 'selected' ? 'selectedQuestion' : 'question'}.${action}`,
          { count },
        ),
        confirmText: this.t(`distributedEvents.confirmation.button.${action}`, { count }),
        cancelText: this.t('distributedEvents.cancel'),
        cutoff: formatAppDate(loadedCutoff, 'full', this.displayTimeZone(), this.displayLocale()),
        criteria: mode === 'filter' ? criteria : ids.join(', '),
        cutoffLabel: this.t('distributedEvents.confirmation.cutoff'),
        criteriaLabel: this.t(
          mode === 'filter'
            ? 'distributedEvents.confirmation.criteria'
            : 'distributedEvents.confirmation.ids',
        ),
        variant: discard ? 'destructive' : 'default',
        message: this.t(
          view === 'inbox'
            ? 'distributedEvents.replayWarning'
            : `distributedEvents.confirmation.consequence.${action}`,
        ),
        action: () => {
          const request =
            view === 'outbox'
              ? this.service.redeliver(body as Dto.RecoverDistributedEventOutboxInputDto)
              : view === 'inbox'
                ? this.service.replay(body as Dto.RecoverDistributedEventInboxInputDto)
                : discard
                  ? this.service.discard(body as Dto.RecoverDistributedEventRejectionInputDto)
                  : this.service.redecode(body as Dto.RecoverDistributedEventRejectionInputDto);
          return request.pipe(
            tap((result) => {
              toast.success(
                this.t(`distributedEvents.confirmation.result.${action}`, {
                  count: result.affectedCount - result.deferredCount,
                  deferred: result.deferredCount,
                  skipped: result.skipped.length,
                }),
              );
              this.refresh.next();
              this.detailVersion.update((version) => version + 1);
            }),
          );
        },
      });
    } finally {
      this.busy.set(false);
    }
  }

  /** 确认框里的时间范围写完整时刻，与"加载截止"同一写法，分享链接带来的非整天边界也不失真。 */
  private rangeCriterion(): string[] {
    const range = this.timeRange();
    if (!range) return [];
    const format = (instant: string) =>
      formatAppDate(instant, 'full', this.displayTimeZone(), this.displayLocale());
    return [
      `${this.t('distributedEvents.field.timeRange')}: ${format(range.startTime)} ~ ${format(range.endTime)}`,
    ];
  }

  sendTest(): void {
    if (!this.canTest() || this.busy()) return;
    this.busy.set(true);
    this.service
      .sendTestEvent()
      .pipe(
        takeUntilDestroyed(this.owner),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (result) => {
          toast.success(this.t('distributedEvents.testSent', { id: result.eventId }));
          this.cutoff.set(new Date().toISOString());
          // 即使已在发件箱也清掉时间范围与其余筛选，保证刚创建的事件能被列出。
          this.update({
            view: 'outbox',
            keyword: result.eventId,
            startTime: null,
            endTime: null,
            status: null,
            eventName: null,
            errorCode: null,
            consumer: null,
            reason: null,
            anomalies: null,
            direction: null,
          });
          this.refresh.next();
        },
        error: (error: unknown) => toast.error(applicationErrorMessage(error)),
      });
  }
}
