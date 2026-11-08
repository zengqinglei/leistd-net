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
import { lucideSearch, lucideRefreshCw, lucideSend, lucideX, lucideInbox } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCheckbox } from '@spartan-ng/helm/checkbox';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmInputGroupImports } from '@spartan-ng/helm/input-group';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmTabsImports } from '@spartan-ng/helm/tabs';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import {
  Observable,
  Subject,
  catchError,
  combineLatest,
  finalize,
  forkJoin,
  map,
  of,
  startWith,
  switchMap,
  tap,
} from 'rxjs';

import {
  EventRecord,
  EventView,
  EventRow,
  eventRow,
  recoveryAction,
  reasonKey,
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
import { applicationErrorMessage } from '../../../../core/errors/application-http-error';
import { AccountPreferencesService } from '../../../../core/services/account-preferences-service';
import { AuthService } from '../../../../core/services/auth-service';
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { LayoutService } from '../../../../core/services/layout-service';
import { FacetedFilter } from '../../../../shared/components/faceted-filter/faceted-filter';
import { PERMISSIONS } from '../../../../shared/constants/permission.constants';
import { PagedResultDto } from '../../../../shared/dtos/paged-result.dto';
import { formatAppDate } from '../../../../shared/pipes/app-date-pipe';
import { AppNumber } from '../../../../shared/pipes/app-number-pipe';
//#if (!IncludeLocalization)
import { englishText } from '../../../../shared/utils/english-text';
//#endif
import * as Dto from '../../dtos/distributed-event.dto';
import { DistributedEventService } from '../../services/distributed-event-service';
//#if (LocalIdentity && IncludeMultiTenancy)
import { TenantService } from '../../services/tenant-service';
//#endif

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
    ...HlmTabsImports,
    ...HlmTooltipImports,
    FacetedFilter,
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
  providers: [provideIcons({ lucideSearch, lucideRefreshCw, lucideSend, lucideX, lucideInbox })],
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
  private readonly confirm = inject(DistributedEventRecoveryConfirmService);
  private readonly preferences = inject(AccountPreferencesService);
  private readonly layout = inject(LayoutService);
  //#if (LocalIdentity && IncludeMultiTenancy)
  private readonly tenants = inject(TenantService);
  private readonly tenantNameCache = new Map<string, string>();
  //#endif
  readonly tenantNames = signal<Record<string, string>>({});
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
  readonly selected = signal<string[]>([]);
  readonly busy = signal(false);
  readonly displayTimeZone = this.preferences.timeZone;
  readonly displayLocale = this.preferences.displayLocale;
  private readonly refresh = new Subject<void>();
  readonly detailVersion = signal(0);
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
        : ['pending', 'redecoded', 'discarded'],
  );
  readonly facetKeys = computed(() =>
    this.view() === 'catalog'
      ? ['direction']
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
    let renderedView = this.view();
    let renderedDatabase = this.database();
    const query$ = this.route.queryParams;
    combineLatest([query$, this.refresh.pipe(startWith(undefined))])
      .pipe(
        switchMap(([query]) => {
          this.loading.set(true);
          this.loadError.set(null);
          this.selected.set([]);
          const database = this.database();
          const view = this.view();
          if (view !== renderedView || database !== renderedDatabase) this.rows.set([]);
          renderedView = view;
          renderedDatabase = database;
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
            switchMap((result) =>
              this.resolveTenantNames(result.list.items).pipe(
                map((names) => ({ ...result, names })),
              ),
            ),
            tap((result) => {
              this.tenantNames.set(result.names);
              this.status.set(result.status);
              this.catalog.set(result.catalog);
              this.databases.set(result.databases);
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
    effect(() => {
      if (!this.canManage()) this.selected.set([]);
    });
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
      direction: null,
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
      direction: null,
    });
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
  readonly databaseLabel = (value: unknown): string =>
    String(value) === 'host' ? this.t('distributedEvents.hostDatabase') : String(value ?? '');

  action(): string {
    return recoveryAction(this.view());
  }
  protected reasonLabel(reason: string): string {
    const key = reasonKey(reason);
    return key ? this.t(key) : reason;
  }
  private resolveTenantNames(_records: EventRecord[]): Observable<Record<string, string>> {
    //#if (LocalIdentity && IncludeMultiTenancy)
    if (this.isHost() && this.authorization.has(PERMISSIONS.tenants.default)) {
      const ids = [
        ...new Set(
          _records.flatMap((row) => ('tenantId' in row && row.tenantId ? [row.tenantId] : [])),
        ),
      ];
      if (ids.length)
        return forkJoin(
          ids.map((id) =>
            this.tenantNameCache.has(id)
              ? of([id, this.tenantNameCache.get(id)!])
              : this.tenants.getTenant(id).pipe(
                  map((tenant) => {
                    const name = tenant.displayName || tenant.name;
                    this.tenantNameCache.set(id, name);
                    return [id, name];
                  }),
                  catchError(() => of([id, id])),
                ),
          ),
        ).pipe(map((entries) => Object.fromEntries(entries)));
    }
    //#endif
    return of({});
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
    const database = this.database();
    const scope = this.isHost()
      ? this.databaseLabel(view === 'rejections' ? 'host' : database)
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
            : key === 'reason'
              ? this.reasonLabel(this.query()[key])
              : String(this.query()[key]);
        return `${label}: ${value}`;
      })
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
        scope,
        cutoff: formatAppDate(this.cutoff(), 'full', this.displayTimeZone(), this.displayLocale()),
        criteria: mode === 'filter' ? criteria : ids.join(', '),
        scopeLabel: this.t('distributedEvents.confirmation.scope'),
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
                this.t(`distributedEvents.confirmation.result.${action}`, {
                  count: result.affectedCount,
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
}
