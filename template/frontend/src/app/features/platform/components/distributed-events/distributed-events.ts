import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Params, Router } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoDirective, TranslocoService, translateSignal } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideSearch,
  lucideRefreshCw,
  lucideSend,
  lucidePanelRight,
  lucideRotateCw,
  lucideCopy,
  lucideCheck,
  lucideInfo,
  lucideX,
  lucideInbox,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCheckbox } from '@spartan-ng/helm/checkbox';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmInputGroupImports } from '@spartan-ng/helm/input-group';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmSheetImports } from '@spartan-ng/helm/sheet';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmTabsImports } from '@spartan-ng/helm/tabs';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { ColumnDef } from '@tanstack/angular-table';
import {
  Observable,
  Subject,
  catchError,
  combineLatest,
  finalize,
  forkJoin,
  of,
  startWith,
  switchMap,
  tap,
} from 'rxjs';

import {
  //#if (!IncludeLocalization)
  ENGLISH,
  //#endif
  EventDetail,
  EventRecord,
  EventRow,
  EventView,
  STATUS_VARIANTS,
  canRecover,
  eventRow,
} from './distributed-event-display';
import { applicationErrorMessage } from '../../../../core/errors/application-http-error';
import { ConfirmService } from '../../../../core/feedback/confirm-service';
import { AccountPreferencesService } from '../../../../core/services/account-preferences-service';
import { AuthService } from '../../../../core/services/auth-service';
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { LayoutService } from '../../../../core/services/layout-service';
import { FacetedFilter } from '../../../../shared/components/faceted-filter/faceted-filter';
import { StatPanel } from '../../../../shared/components/stat-panel/stat-panel';
import { TablePaginator } from '../../../../shared/components/table-paginator/table-paginator';
import { PERMISSIONS } from '../../../../shared/constants/permission.constants';
import { TableFit } from '../../../../shared/directives/table-fit';
import { TruncatedTooltip } from '../../../../shared/directives/truncated-tooltip';
import { PagedResultDto } from '../../../../shared/dtos/paged-result.dto';
import { AppDate, formatAppDate } from '../../../../shared/pipes/app-date-pipe';
import { AppNumber } from '../../../../shared/pipes/app-number-pipe';
import { injectCopyToClipboard } from '../../../../shared/utils/clipboard';
//#if (!IncludeLocalization)
import { englishText } from '../../../../shared/utils/english-text';
//#endif
import {
  ACTIONS_COLUMN_META,
  TITLE_COLUMN_META,
  TITLE_CONTENT_CLASS,
  tableColumnVisibility,
} from '../../../../shared/utils/table-column-meta';
import { AppTableFeatures, injectAppTable } from '../../../../shared/utils/table-features';
import { tableViewportSignal } from '../../../../shared/utils/table-viewport';
import * as Dto from '../../dtos/distributed-event.dto';
import { DistributedEventService } from '../../services/distributed-event-service';

@Component({
  selector: 'app-distributed-events',
  imports: [
    NgIcon,
    HlmBadge,
    HlmButton,
    HlmCheckbox,
    ...HlmEmptyImports,
    ...HlmInputGroupImports,
    ...HlmSelectImports,
    ...HlmSheetImports,
    HlmSpinner,
    ...HlmTableImports,
    ...HlmTabsImports,
    ...HlmTooltipImports,
    FacetedFilter,
    StatPanel,
    TablePaginator,
    TableFit,
    TruncatedTooltip,
    AppDate,
    AppNumber,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [
    provideIcons({
      lucideSearch,
      lucideRefreshCw,
      lucideSend,
      lucidePanelRight,
      lucideRotateCw,
      lucideCopy,
      lucideCheck,
      lucideInfo,
      lucideX,
      lucideInbox,
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
  private readonly auth = inject(AuthService);
  private readonly confirm = inject(ConfirmService);
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
  readonly isHost = computed(() => !this.auth.currentUser()?.tenantId);
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
  readonly database = computed(() => (this.isHost() ? this.query()['database'] || 'host' : null));
  readonly recordId = computed<string | null>(() => this.query()['record'] || null);
  readonly page = computed(() => Math.max(0, Number(this.query()['page']) || 0));
  readonly pageSize = computed(() =>
    [10, 20, 50, 100].includes(Number(this.query()['pageSize']))
      ? Number(this.query()['pageSize'])
      : 10,
  );
  readonly cutoff = signal(new Date().toISOString());
  readonly rows = signal<EventRow[]>([]);
  readonly totalCount = signal(0);
  readonly status = signal<Dto.DistributedEventStatusOutputDto | null>(null);
  readonly databases = signal<Dto.DistributedEventDatabaseOutputDto[]>([]);
  readonly catalog = signal<Dto.DistributedEventCatalogOutputDto[]>([]);
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly detail = signal<EventDetail | null>(null);
  readonly detailError = signal<string | null>(null);
  readonly detailLoading = signal(false);
  readonly payload = signal<string | null>(null);
  readonly payloadError = signal<string | null>(null);
  readonly payloadLoading = signal(false);
  readonly selected = signal<string[]>([]);
  readonly busy = signal(false);
  readonly displayTimeZone = this.preferences.timeZone;
  readonly displayLocale = this.preferences.displayLocale;
  readonly clipboard = injectCopyToClipboard();
  readonly variants = STATUS_VARIANTS;
  readonly titleClass = `${TITLE_CONTENT_CLASS} max-w-60`;
  readonly tableViewport = tableViewportSignal();
  private readonly tableFit = viewChild(TableFit);
  private readonly refresh = new Subject<void>();
  private readonly detailRefresh = new Subject<void>();
  private payloadSubscription?: { unsubscribe(): void };
  readonly canTest = computed(
    () =>
      this.canManage() &&
      this.status()?.testEventEnabled &&
      (!this.isHost() || this.database() === 'host'),
  );
  readonly bulkAllowed = computed(() =>
    this.view() === 'outbox'
      ? this.query()['status'] === 'deadLettered'
      : this.view() === 'inbox'
        ? ['failed', 'processed'].includes(this.query()['status'])
        : this.view() === 'rejections' && this.query()['status'] === 'pending',
  );
  readonly allChecked = computed(() => {
    const eligible = this.rows().filter((row) => row.record && canRecover(row.record));
    return eligible.length > 0 && eligible.every((row) => this.selected().includes(row.id));
  });
  readonly columns = computed<ColumnDef<AppTableFeatures, EventRow>[]>(() => {
    const column = (
      id: string,
      priority: 'primary' | 'secondary' | 'tertiary' = 'tertiary',
    ): ColumnDef<AppTableFeatures, EventRow> => ({ id, enableSorting: false, meta: { priority } });
    const result: ColumnDef<AppTableFeatures, EventRow>[] = [
      ...(this.canManage() && this.view() !== 'catalog'
        ? [
            {
              id: 'select',
              meta: { priority: 'primary' as const, locked: true },
              enableSorting: false,
            },
          ]
        : []),
      { id: 'event', meta: TITLE_COLUMN_META, enableSorting: false },
    ];
    if (this.view() === 'catalog')
      return [
        ...result,
        column('direction', 'secondary'),
        column('consumer', 'secondary'),
        column('lastSent'),
        column('lastReceived'),
        column('lastProcessed'),
        column('pending', 'secondary'),
        column('anomalies', 'primary'),
        column('error'),
        { id: 'actions', enableSorting: false, enableHiding: false, meta: ACTIONS_COLUMN_META },
      ];
    if (this.isHost() && this.view() !== 'rejections') result.push(column('tenant', 'secondary'));
    if (this.view() === 'inbox') result.push(column('consumer', 'secondary'));
    if (this.view() !== 'rejections') result.push(column('orderingKey', 'secondary'));
    result.push(
      {
        ...column('status', 'primary'),
        enableHiding: false,
        meta: { priority: 'primary', locked: true },
      },
      column('attempts', 'secondary'),
      column('error', 'secondary'),
    );
    if (this.view() !== 'rejections') result.push(column('next'));
    result.push(column('time'), {
      id: 'actions',
      enableSorting: false,
      enableHiding: false,
      meta: ACTIONS_COLUMN_META,
    });
    return result;
  });
  readonly table = injectAppTable(() => ({
    data: this.rows(),
    columns: this.columns(),
    getRowId: (row) => row.id,
    manualPagination: true,
    rowCount: this.totalCount(),
    state: {
      pagination: { pageIndex: this.page(), pageSize: this.pageSize() },
      columnVisibility: tableColumnVisibility(
        this.columns(),
        this.tableFit()?.level() ?? this.tableViewport(),
      ),
    },
  }));
  readonly catalogDetail = computed(() =>
    this.view() === 'catalog'
      ? (this.catalog().find((item) => item.eventName === this.recordId()) ?? null)
      : null,
  );
  readonly catalogFields = computed(() =>
    this.catalogDetail() ? Object.entries(this.catalogDetail()!) : [],
  );
  readonly detailFields = computed(() =>
    this.detail() ? Object.entries(this.detail()!.record) : [],
  );
  readonly detailHeaders = computed(() => Object.entries(this.detail()?.headers ?? {}));
  readonly detailCoordinates = computed(() => {
    const detail = this.detail();
    return detail && 'coordinates' in detail ? Object.entries(detail.coordinates) : [];
  });
  readonly states = computed(() =>
    this.view() === 'outbox'
      ? ['pending', 'sent', 'deadLettered']
      : this.view() === 'inbox'
        ? ['pending', 'processed', 'failed']
        : ['pending', 'redecoded', 'discarded'],
  );
  readonly facetKeys = computed(() =>
    this.view() === 'catalog'
      ? []
      : [
          'status',
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
    const query$ = this.route.queryParams;
    combineLatest([query$, this.refresh.pipe(startWith(undefined))])
      .pipe(
        switchMap(([query]) => {
          this.loading.set(true);
          this.loadError.set(null);
          this.selected.set([]);
          const database = this.database();
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
            status: this.service.getStatus(database),
            catalog: this.service.getCatalog(database),
            databases: this.isHost() ? this.service.getDatabases() : of([]),
            list: list$,
          }).pipe(
            tap((result) => {
              this.status.set(result.status);
              this.catalog.set(result.catalog);
              this.databases.set(result.databases);
              if (this.view() === 'catalog') {
                const catalog = result.catalog
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
                      tenant: '',
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
              this.rows.set([]);
              this.totalCount.set(0);
              this.loadError.set(applicationErrorMessage(error));
              return of(null);
            }),
            finalize(() => this.loading.set(false)),
          );
        }),
        takeUntilDestroyed(this.owner),
      )
      .subscribe();
    combineLatest([query$, this.detailRefresh.pipe(startWith(undefined))])
      .pipe(
        switchMap(() => {
          this.payloadSubscription?.unsubscribe();
          this.payload.set(null);
          this.payloadError.set(null);
          this.payloadLoading.set(false);
          this.detail.set(null);
          this.detailError.set(null);
          const id = this.recordId();
          const database = this.database();
          if (!id || this.view() === 'catalog') return of(null);
          this.detailLoading.set(true);
          const request$: Observable<EventDetail> =
            this.view() === 'outbox'
              ? this.service.getOutbox(id, database)
              : this.view() === 'inbox'
                ? this.service.getInbox(id, database)
                : this.service.getRejection(id, database);
          return request$.pipe(
            tap((detail) => this.detail.set(detail)),
            catchError((error: unknown) => {
              this.detailError.set(applicationErrorMessage(error));
              return of(null);
            }),
            finalize(() => this.detailLoading.set(false)),
          );
        }),
        takeUntilDestroyed(this.owner),
      )
      .subscribe();
    effect(() => {
      if (!this.canManage()) {
        this.payloadSubscription?.unsubscribe();
        this.payload.set(null);
        this.selected.set([]);
      }
    });
    this.owner.onDestroy(() => this.payloadSubscription?.unsubscribe());
  }

  listInput(query: Params = this.query()): Record<string, unknown> {
    return {
      database: this.database(),
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

  setView(view: string): void {
    this.update({
      view,
      status: null,
      eventName: null,
      errorCode: null,
      reason: null,
      consumer: null,
      keyword: null,
      anomalies: null,
    });
  }

  jump(view: EventView, status: string, eventName?: string): void {
    this.update({
      view,
      status,
      eventName: eventName || null,
      keyword: null,
      errorCode: null,
      reason: null,
      consumer: null,
      anomalies: null,
    });
  }

  reload(): void {
    this.cutoff.set(new Date().toISOString());
    this.refresh.next();
    this.detailRefresh.next();
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
  lastPage(): number {
    return Math.max(0, Math.ceil(this.totalCount() / this.pageSize()) - 1);
  }
  recoverable(record: EventRecord): boolean {
    return canRecover(record);
  }
  badge(status: string): 'warning' | 'success' | 'destructive' | 'secondary' {
    return STATUS_VARIANTS[status as keyof typeof STATUS_VARIANTS] ?? 'secondary';
  }
  statusLabel(row: EventRow): string {
    return row.status === 'pending' && row.attempts > 0 && this.view() !== 'rejections'
      ? 'retrying'
      : row.status;
  }
  toggle(id: string, checked: boolean): void {
    this.selected.update((ids) =>
      checked ? [...new Set([...ids, id])].slice(0, 100) : ids.filter((value) => value !== id),
    );
  }
  toggleAll(checked: boolean): void {
    this.selected.set(
      checked
        ? this.rows()
            .filter((row) => row.record && canRecover(row.record))
            .map((row) => row.id)
            .slice(0, 100)
        : [],
    );
  }
  onRowClick(event: MouseEvent, row: EventRow): void {
    if (!(event.target as HTMLElement).closest('button,a,hlm-checkbox')) {
      this.openRecord(row.id);
    }
  }

  readonly databaseLabel = (value: unknown): string =>
    String(value) === 'host' ? this.t('distributedEvents.hostDatabase') : String(value ?? '');

  options(key: string) {
    const values =
      key === 'status'
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
      label: key === 'status' ? this.t(`distributedEvents.state.${value}`) : value,
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
    const ids = row ? [row.id] : this.selected();
    if ((mode !== 'filter' && !ids.length) || (mode === 'filter' && !this.bulkAllowed())) return;
    const action = discard
      ? 'discard'
      : view === 'outbox'
        ? 'redeliver'
        : view === 'inbox'
          ? 'replay'
          : 'redecode';
    const input = this.listInput();
    const {
      database: _database,
      offset: _offset,
      limit: _limit,
      status: _status,
      ...filter
    } = input;
    const body =
      mode === 'filter'
        ? { filter: { ...filter, ...(view === 'inbox' ? { status: this.query()['status'] } : {}) } }
        : { ids };
    const count = mode === 'filter' ? this.totalCount() : ids.length;
    const isProcessed = row
      ? row.status === 'processed'
      : view === 'inbox' &&
        (this.query()['status'] === 'processed' ||
          this.rows().some((item) => ids.includes(item.id) && item.status === 'processed'));
    const database = this.database();
    const scope = this.isHost()
      ? this.databaseLabel(database)
      : this.t('distributedEvents.tenantScope');
    const criteria = ['status', 'eventName', 'keyword', 'errorCode', 'consumer', 'reason']
      .filter((key) => this.query()[key])
      .map((key) => {
        const label =
          key === 'keyword'
            ? this.t('distributedEvents.search')
            : this.t(`distributedEvents.field.${key}`);
        const value =
          key === 'status'
            ? this.t(`distributedEvents.state.${this.query()[key]}`)
            : String(this.query()[key]);
        return `${label}: ${value}`;
      })
      .join(' · ');
    this.busy.set(true);
    try {
      await this.confirm.open({
        owner: this.owner,
        header: this.t(`distributedEvents.action.${action}`),
        confirmText: this.t(`distributedEvents.action.${action}`),
        variant: discard ? 'destructive' : 'default',
        message: `${this.t('distributedEvents.confirm', { count, scope, before: formatAppDate(this.cutoff(), 'full', this.displayTimeZone(), this.displayLocale()), filter: mode === 'filter' ? criteria : ids.join(', ') })}
${this.t(isProcessed ? 'distributedEvents.replayWarning' : 'distributedEvents.recoveryWarning')}`,
        action: () => {
          const request =
            view === 'outbox'
              ? this.service.redeliver(body as Dto.RecoverDistributedEventOutboxInputDto, database)
              : view === 'inbox'
                ? this.service.replay(body as Dto.RecoverDistributedEventInboxInputDto, database)
                : discard
                  ? this.service.discard(
                      body as Dto.RecoverDistributedEventRejectionInputDto,
                      database,
                    )
                  : this.service.redecode(
                      body as Dto.RecoverDistributedEventRejectionInputDto,
                      database,
                    );
          return request.pipe(
            tap((result) => {
              toast.success(
                this.t('distributedEvents.recovered', {
                  count: result.affectedCount,
                  skipped: result.skipped.length,
                }),
              );
              this.refresh.next();
              this.detailRefresh.next();
            }),
          );
        },
      });
    } finally {
      this.busy.set(false);
    }
  }

  sendTest(): void {
    if (!this.canTest() || this.busy()) return;
    this.busy.set(true);
    this.service
      .sendTestEvent(this.database())
      .pipe(
        takeUntilDestroyed(this.owner),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (result) => {
          toast.success(this.t('distributedEvents.testSent', { id: result.eventId }));
          this.cutoff.set(new Date().toISOString());
          this.update({
            view: 'outbox',
            keyword: result.eventId,
            status: null,
            eventName: null,
            errorCode: null,
            consumer: null,
          });
          this.refresh.next();
        },
        error: (error: unknown) => toast.error(applicationErrorMessage(error)),
      });
  }

  loadPayload(): void {
    if (!this.canManage() || !this.recordId() || this.payloadLoading()) return;
    this.payloadLoading.set(true);
    this.payloadError.set(null);
    const id = this.recordId()!;
    const database = this.database();
    const request: Observable<
      Dto.DistributedEventPayloadOutputDto | Dto.DistributedEventRawMessageOutputDto
    > =
      this.view() === 'outbox'
        ? this.service.getOutboxPayload(id, database)
        : this.view() === 'inbox'
          ? this.service.getInboxPayload(id, database)
          : this.service.getRejectionPayload(id, database);
    this.payloadSubscription = request
      .pipe(
        takeUntilDestroyed(this.owner),
        finalize(() => this.payloadLoading.set(false)),
      )
      .subscribe({
        next: (result) => this.payload.set('payload' in result ? result.payload : result.rawBody),
        error: (error: unknown) => this.payloadError.set(applicationErrorMessage(error)),
      });
  }

  fieldValue(_key: string, value: unknown): string {
    return value === null || value === undefined
      ? '—'
      : typeof value === 'object'
        ? JSON.stringify(value)
        : String(value);
  }
  isDate(key: string): boolean {
    return /At$|Time$/.test(key);
  }
}
