import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
//#if (IncludeLocalization)
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCheck,
  lucideCircleAlert,
  lucideCopy,
  lucideEllipsis,
  lucideRotateCw,
  lucideTrash2,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmSheetImports } from '@spartan-ng/helm/sheet';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { Observable, catchError, finalize, of, switchMap, tap } from 'rxjs';

import { applicationErrorMessage } from '../../../../../../core/errors/application-http-error';
import { AccountPreferencesService } from '../../../../../../core/services/account-preferences-service';
import { DialogLoading } from '../../../../../../shared/components/dialog-loading/dialog-loading';
import { StatusDot } from '../../../../../../shared/directives/status-dot';
import { AppDate } from '../../../../../../shared/pipes/app-date-pipe';
import { injectCopyToClipboard } from '../../../../../../shared/utils/clipboard';
//#if (!IncludeLocalization)
import { englishText } from '../../../../../../shared/utils/english-text';
//#endif
import * as Dto from '../../../../dtos/distributed-event.dto';
import { DistributedEventService } from '../../../../services/distributed-event-service';
import {
  //#if (!IncludeLocalization)
  ENGLISH,
  //#endif
  EventDetail,
  EventRecord,
  EventView,
  StateBadgeVariant,
  canRecover,
  canDiscard,
  displayState,
  recoveryAction,
  reasonKey,
  stateLabelKey,
  stateVariant,
} from '../../distributed-event-display';

/** 需要人工处理的记录在正文顶部给出原因与下一步。 */
interface RecordAttention {
  variant: 'default' | 'destructive';
  state: string;
  code: string;
  diagnostic: string | null;
  nextKey: string;
}

/** 目录详情里跳到相关记录的一行；只列该事件适用的方向。 */
interface CatalogLink {
  labelKey: string;
  count: number;
  view: EventView;
  status: string;
}

/** 已用跳转行展示的计数，不在概要里重复。 */
const CATALOG_LINK_FIELDS = ['pendingOutbox', 'pendingInbox', 'deadLettered', 'failedInbox'];

/** 记录与目录抽屉；按输入取消旧详情及载荷请求，载荷仅在 Manage 用户点开后读取。 */
@Component({
  selector: 'app-distributed-event-record-sheet',
  imports: [
    NgIcon,
    ...HlmAlertImports,
    HlmBadge,
    HlmButton,
    ...HlmDropdownMenuImports,
    ...HlmSheetImports,
    HlmSpinner,
    ...HlmTooltipImports,
    DialogLoading,
    StatusDot,
    AppDate,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [
    provideIcons({
      lucideCheck,
      lucideCircleAlert,
      lucideCopy,
      lucideEllipsis,
      lucideRotateCw,
      lucideTrash2,
    }),
  ],
  templateUrl: './distributed-event-record-sheet.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DistributedEventRecordSheet {
  readonly recordId = input<string | null>(null);
  readonly view = input<EventView>('outbox');
  readonly canManage = input(false);
  readonly payloadEnabled = input(true);
  readonly busy = input(false);
  readonly refreshVersion = input(0);
  readonly catalogDetail = input<Dto.DistributedEventCatalogOutputDto | null>(null);
  readonly closed = output<void>();
  readonly recover = output<{ record: EventRecord; discard: boolean }>();
  private readonly service = inject(DistributedEventService);
  private readonly owner = inject(DestroyRef);
  private readonly preferences = inject(AccountPreferencesService);
  protected readonly displayTimeZone = this.preferences.timeZone;
  protected readonly displayLocale = this.preferences.displayLocale;
  /** 事件 ID、载荷与记录 ID 各用一份复制状态，"已复制"反馈互不串到别的按钮上。 */
  protected readonly clipboard = injectCopyToClipboard();
  protected readonly payloadClipboard = injectCopyToClipboard();
  private readonly recordClipboard = injectCopyToClipboard();
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  protected readonly t = (key: string, params?: Record<string, unknown>) =>
    this.transloco.translate(key, params);
  //#else
  protected readonly t = englishText(ENGLISH);
  //#endif
  readonly detail = signal<EventDetail | null>(null);
  protected readonly detailError = signal<string | null>(null);
  protected readonly detailLoading = signal(false);
  /** 正文当前对应的记录（视图 + ID）；重取同一条时保留已显示的详情与载荷，换记录才清掉。 */
  private shownRecord: string | null = null;
  /** 详情加载失败后就地重试：只重发当前记录的详情请求。 */
  private readonly retryVersion = signal(0);
  protected retryDetail(): void {
    this.retryVersion.update((version) => version + 1);
  }
  readonly payload = signal<string | null>(null);
  protected readonly payloadError = signal<string | null>(null);
  readonly payloadLoading = signal(false);
  private payloadSubscription?: { unsubscribe(): void };
  protected readonly detailFields = computed(() =>
    this.detail()
      ? // 传输坐标另有分区，不在概要里重复
        Object.entries(this.detail()!.record).filter(
          ([key]) => key !== 'tenantId' && key !== 'coordinates',
        )
      : [],
  );
  protected readonly detailHeaders = computed(() => Object.entries(this.detail()?.headers ?? {}));
  protected readonly detailCoordinates = computed(() => {
    const detail = this.detail();
    return detail && 'coordinates' in detail ? Object.entries(detail.coordinates) : [];
  });
  protected readonly catalogFields = computed(() =>
    this.catalogDetail()
      ? Object.entries(this.catalogDetail()!).filter(
          ([key]) => key !== 'outgoing' && key !== 'incoming' && !CATALOG_LINK_FIELDS.includes(key),
        )
      : [],
  );
  protected readonly catalogLinks = computed<CatalogLink[]>(() => {
    const item = this.catalogDetail();
    if (!item) return [];
    return [
      ...(item.outgoing
        ? [
            {
              labelKey: 'distributedEvents.field.pendingOutbox',
              count: item.pendingOutbox,
              view: 'outbox' as const,
              status: 'pending',
            },
            {
              labelKey: 'distributedEvents.field.deadLettered',
              count: item.deadLettered,
              view: 'outbox' as const,
              status: 'deadLettered',
            },
          ]
        : []),
      ...(item.incoming
        ? [
            {
              labelKey: 'distributedEvents.field.pendingInbox',
              count: item.pendingInbox,
              view: 'inbox' as const,
              status: 'pending',
            },
            {
              labelKey: 'distributedEvents.field.failedInbox',
              count: item.failedInbox,
              view: 'inbox' as const,
              status: 'failed',
            },
          ]
        : []),
    ];
  });
  protected readonly attention = computed<RecordAttention | null>(() => {
    const record = this.detail()?.record;
    if (!record) return null;
    if ('redecodeCount' in record) {
      return record.status === 'pending' || record.status === 'redecodePending'
        ? {
            variant: 'default',
            state: record.status,
            code: record.reason,
            diagnostic: record.error || null,
            nextKey: `distributedEvents.nextStep.${record.status === 'pending' ? 'redecode' : 'redecodePending'}`,
          }
        : null;
    }
    const failed =
      'redeliverCount' in record ? record.status === 'deadLettered' : record.status === 'failed';
    return failed
      ? {
          variant: 'destructive',
          state: record.status,
          code: record.lastErrorCode ?? '—',
          diagnostic: record.lastError ?? null,
          nextKey: `distributedEvents.nextStep.${recoveryAction(this.view())}`,
        }
      : null;
  });

  constructor() {
    toObservable(
      computed(() => ({
        id: this.recordId(),
        view: this.view(),
        refresh: this.refreshVersion(),
        retry: this.retryVersion(),
      })),
    )
      .pipe(
        switchMap(({ id, view }) => {
          const record = id && view !== 'catalog' ? `${view}:${id}` : null;
          if (record === null || record !== this.shownRecord) {
            this.payloadSubscription?.unsubscribe();
            this.payload.set(null);
            this.payloadError.set(null);
            this.payloadLoading.set(false);
            this.detail.set(null);
          }
          this.shownRecord = record;
          this.detailError.set(null);
          if (!id || view === 'catalog') return of(null);
          this.detailLoading.set(true);
          const request: Observable<EventDetail> =
            view === 'outbox'
              ? this.service.getOutbox(id)
              : view === 'inbox'
                ? this.service.getInbox(id)
                : this.service.getRejection(id);
          return request.pipe(
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
      if (!this.canManage() || !this.payloadEnabled()) {
        this.payloadSubscription?.unsubscribe();
        this.payload.set(null);
        this.payloadError.set(null);
      }
    });
    this.owner.onDestroy(() => this.payloadSubscription?.unsubscribe());
  }
  /** 菜单项点击后菜单即关闭、看不到图标变化，成功时改用 toast 反馈；剪贴板不可用时不提示成功。 */
  protected async copyRecordId(id: string): Promise<void> {
    if (await this.recordClipboard.copy(id)) toast.success(this.t('distributedEvents.copied'));
  }
  protected closeDetail(): void {
    this.closed.emit();
  }
  protected discardable(record: EventRecord): boolean {
    return canDiscard(record);
  }
  protected recoverable(record: EventRecord): boolean {
    return canRecover(record);
  }
  protected stateLabel(record: EventRecord): string {
    return stateLabelKey(this.view(), this.state(record));
  }
  protected stateText(state: string): string {
    return this.t(stateLabelKey(this.view(), state));
  }
  protected action(): string {
    return recoveryAction(this.view());
  }
  protected badge(record: EventRecord): StateBadgeVariant {
    return stateVariant(this.view(), this.state(record));
  }
  private state(record: EventRecord): string {
    return displayState(
      this.view(),
      record.status,
      'attemptCount' in record ? record.attemptCount : record.redecodeCount,
    );
  }
  protected reasonLabel(reason: string): string {
    const key = reasonKey(reason);
    return key ? this.t(key) : reason;
  }
  protected jump(view: EventView, status: string, eventName?: string): void {
    this.navigate.emit({ view, status, eventName });
  }
  readonly navigate = output<{ view: EventView; status: string; eventName?: string }>();
  protected fieldValue(value: unknown): string {
    return value === null || value === undefined
      ? '—'
      : Array.isArray(value)
        ? value.join(', ')
        : String(value);
  }
  protected isDate(key: string): boolean {
    return /At$|Time$/.test(key);
  }
  loadPayload(): void {
    if (!this.canManage() || !this.payloadEnabled() || !this.recordId() || this.payloadLoading())
      return;
    this.payloadLoading.set(true);
    this.payloadError.set(null);
    const id = this.recordId()!;
    const request: Observable<
      Dto.DistributedEventPayloadOutputDto | Dto.DistributedEventRawMessageOutputDto
    > =
      this.view() === 'outbox'
        ? this.service.getOutboxPayload(id)
        : this.view() === 'inbox'
          ? this.service.getInboxPayload(id)
          : this.service.getRejectionPayload(id);
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
}
