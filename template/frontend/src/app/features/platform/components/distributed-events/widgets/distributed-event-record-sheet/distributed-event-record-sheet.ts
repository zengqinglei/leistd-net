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
import { lucideCopy, lucideCheck } from '@ng-icons/lucide';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmSheetImports } from '@spartan-ng/helm/sheet';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { Observable, catchError, finalize, of, switchMap, tap } from 'rxjs';

import { applicationErrorMessage } from '../../../../../../core/errors/application-http-error';
import { AccountPreferencesService } from '../../../../../../core/services/account-preferences-service';
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
  STATUS_VARIANTS,
  canRecover,
  recoveryAction,
  reasonKey,
} from '../../distributed-event-display';

/** 记录与目录抽屉；按输入取消旧详情及载荷请求，载荷仅在 Manage 用户点开后读取。 */
@Component({
  selector: 'app-distributed-event-record-sheet',
  imports: [
    NgIcon,
    HlmBadge,
    HlmButton,
    ...HlmSheetImports,
    HlmSpinner,
    ...HlmTooltipImports,
    AppDate,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [provideIcons({ lucideCopy, lucideCheck })],
  templateUrl: './distributed-event-record-sheet.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DistributedEventRecordSheet {
  readonly recordId = input<string | null>(null);
  readonly view = input<EventView>('outbox');
  readonly database = input<string | null>(null);
  readonly canManage = input(false);
  readonly payloadEnabled = input(true);
  readonly busy = input(false);
  readonly tenantNames = input<Record<string, string>>({});
  readonly refreshVersion = input(0);
  readonly catalogDetail = input<Dto.DistributedEventCatalogOutputDto | null>(null);
  readonly closed = output<void>();
  readonly recover = output<{ record: EventRecord; discard: boolean }>();
  private readonly service = inject(DistributedEventService);
  private readonly owner = inject(DestroyRef);
  private readonly preferences = inject(AccountPreferencesService);
  protected readonly displayTimeZone = this.preferences.timeZone;
  protected readonly displayLocale = this.preferences.displayLocale;
  protected readonly clipboard = injectCopyToClipboard();
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  protected readonly t = (key: string) => this.transloco.translate(key);
  //#else
  protected readonly t = englishText(ENGLISH);
  //#endif
  readonly detail = signal<EventDetail | null>(null);
  protected readonly detailError = signal<string | null>(null);
  protected readonly detailLoading = signal(false);
  readonly payload = signal<string | null>(null);
  protected readonly payloadError = signal<string | null>(null);
  readonly payloadLoading = signal(false);
  private payloadSubscription?: { unsubscribe(): void };
  protected readonly detailFields = computed(() =>
    this.detail() ? Object.entries(this.detail()!.record) : [],
  );
  protected readonly detailHeaders = computed(() => Object.entries(this.detail()?.headers ?? {}));
  protected readonly detailCoordinates = computed(() => {
    const detail = this.detail();
    return detail && 'coordinates' in detail ? Object.entries(detail.coordinates) : [];
  });
  protected readonly catalogFields = computed(() =>
    this.catalogDetail()
      ? Object.entries(this.catalogDetail()!).filter(
          ([key]) => key !== 'outgoing' && key !== 'incoming',
        )
      : [],
  );

  constructor() {
    toObservable(
      computed(() => ({
        id: this.recordId(),
        view: this.view(),
        database: this.database(),
        refresh: this.refreshVersion(),
      })),
    )
      .pipe(
        switchMap(({ id, view, database }) => {
          this.payloadSubscription?.unsubscribe();
          this.payload.set(null);
          this.payloadError.set(null);
          this.payloadLoading.set(false);
          this.detail.set(null);
          this.detailError.set(null);
          if (!id || view === 'catalog') return of(null);
          this.detailLoading.set(true);
          const request: Observable<EventDetail> =
            view === 'outbox'
              ? this.service.getOutbox(id, database)
              : view === 'inbox'
                ? this.service.getInbox(id, database)
                : this.service.getRejection(id, database);
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
  protected closeDetail(): void {
    this.closed.emit();
  }
  protected recoverable(record: EventRecord): boolean {
    return canRecover(record);
  }
  protected stateLabel(record: EventRecord): string {
    return record.status === 'pending' && 'attemptCount' in record && record.attemptCount > 0
      ? 'retrying'
      : record.status;
  }
  protected action(): string {
    return recoveryAction(this.view());
  }
  protected badge(status: string): 'warning' | 'success' | 'destructive' | 'secondary' {
    return STATUS_VARIANTS[status as keyof typeof STATUS_VARIANTS] ?? 'secondary';
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
}
