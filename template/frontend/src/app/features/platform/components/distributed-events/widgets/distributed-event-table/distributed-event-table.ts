import { NgTemplateOutlet } from '@angular/common';
import { Directive, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
//#if (IncludeLocalization)
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePanelRight, lucideRotateCw } from '@ng-icons/lucide';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCheckbox } from '@spartan-ng/helm/checkbox';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { ColumnDef } from '@tanstack/angular-table';
import { interval } from 'rxjs';

import { AccountPreferencesService } from '../../../../../../core/services/account-preferences-service';
import { RelativeTimeLabel } from '../../../../../../shared/components/relative-time-label/relative-time-label';
import {
  TableLoading,
  TableLoadingBody,
} from '../../../../../../shared/components/table-loading/table-loading';
import { TablePaginator } from '../../../../../../shared/components/table-paginator/table-paginator';
import { TableFit } from '../../../../../../shared/directives/table-fit';
import { TruncatedTooltip } from '../../../../../../shared/directives/truncated-tooltip';
import { AppDate } from '../../../../../../shared/pipes/app-date-pipe';
import { AppNumber } from '../../../../../../shared/pipes/app-number-pipe';
//#if (!IncludeLocalization)
import { englishText } from '../../../../../../shared/utils/english-text';
//#endif
import {
  ACTIONS_COLUMN_META,
  TITLE_COLUMN_META,
  TITLE_CONTENT_CLASS,
  tableColumnVisibility,
} from '../../../../../../shared/utils/table-column-meta';
import { AppTableFeatures, injectAppTable } from '../../../../../../shared/utils/table-features';
import { tableViewportSignal } from '../../../../../../shared/utils/table-viewport';
import { resolveDisplayTimeZone } from '../../../../../../shared/utils/zoned-time';
import {
  //#if (!IncludeLocalization)
  ENGLISH,
  //#endif
  EventRecord,
  EventRow,
  EventView,
  STATUS_VARIANTS,
  canRecover,
  recoveryAction,
  reasonKey,
} from '../../distributed-event-display';

export const EVENT_TABLE_IMPORTS = [
  NgTemplateOutlet,
  TableLoading,
  TableLoadingBody,
  NgIcon,
  HlmBadge,
  HlmButton,
  HlmCheckbox,
  ...HlmTableImports,
  ...HlmTooltipImports,
  RelativeTimeLabel,
  TablePaginator,
  TableFit,
  TruncatedTooltip,
  AppDate,
  AppNumber,
  //#if (IncludeLocalization)
  TranslocoDirective,
  //#endif
];
export const EVENT_TABLE_ICONS = provideIcons({ lucidePanelRight, lucideRotateCw });

/** 四张表共用的呈现与选择行为；视图由具体表固定，分页与 URL 由页面编排。 */
@Directive()
export abstract class DistributedEventTable {
  protected abstract view(): EventView;
  readonly rows = input<EventRow[]>([]);
  readonly totalCount = input(0);
  readonly page = input(0);
  readonly pageSize = input(10);
  readonly isHost = input(true);
  readonly canManage = input(false);
  readonly selected = input<string[]>([]);
  readonly recordId = input<string | null>(null);
  readonly busy = input(false);
  readonly loading = input(false);
  readonly tenantNames = input<Record<string, string>>({});
  readonly open = output<string>();
  readonly recover = output<EventRecord>();
  readonly selectionChange = output<string[]>();
  readonly pageChange = output<number>();
  readonly pageSizeChange = output<number>();
  readonly jumpTo = output<{ view: EventView; status: string; eventName?: string }>();
  private readonly preferences = inject(AccountPreferencesService);
  protected readonly displayTimeZone = this.preferences.timeZone;
  protected readonly displayLocale = this.preferences.displayLocale;
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  protected readonly t = (key: string) => this.transloco.translate(key);
  //#else
  protected readonly t = englishText(ENGLISH);
  //#endif
  protected readonly now = signal(Date.now());
  protected readonly rowHeight = computed(() => (this.view() === 'catalog' ? 85 : 53));
  protected readonly titleClass = `${TITLE_CONTENT_CLASS} max-w-52`;
  protected readonly tableViewport = tableViewportSignal();
  protected readonly relativeTimeZone = computed(() =>
    resolveDisplayTimeZone(this.displayTimeZone()),
  );
  private readonly tableFit = viewChild(TableFit);
  protected readonly allChecked = computed(() => {
    const eligible = this.rows().filter((row) => row.record && canRecover(row.record));
    return eligible.length > 0 && eligible.every((row) => this.selected().includes(row.id));
  });
  protected abstract columnsForView(): ColumnDef<AppTableFeatures, EventRow>[];
  protected readonly columns = computed(() => this.columnsForView());
  protected dataColumn(
    id: string,
    priority: 'primary' | 'secondary' | 'tertiary' = 'tertiary',
  ): ColumnDef<AppTableFeatures, EventRow> {
    return { id, enableSorting: false, meta: { priority } };
  }
  protected selectionColumns(): ColumnDef<AppTableFeatures, EventRow>[] {
    return this.canManage()
      ? [{ id: 'select', enableSorting: false, meta: { priority: 'primary', locked: true } }]
      : [];
  }
  protected titleColumn(): ColumnDef<AppTableFeatures, EventRow> {
    return { id: 'event', enableSorting: false, meta: TITLE_COLUMN_META };
  }
  protected statusColumn(): ColumnDef<AppTableFeatures, EventRow> {
    return {
      id: 'status',
      enableSorting: false,
      enableHiding: false,
      meta: { priority: 'primary', locked: true },
    };
  }
  protected actionsColumn(): ColumnDef<AppTableFeatures, EventRow> {
    return { id: 'actions', enableSorting: false, enableHiding: false, meta: ACTIONS_COLUMN_META };
  }
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

  constructor() {
    interval(60_000)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.now.set(Date.now()));
  }
  protected lastPage(): number {
    return Math.max(0, Math.ceil(this.totalCount() / this.pageSize()) - 1);
  }
  protected changePage(page: number): void {
    this.pageChange.emit(page);
  }
  protected openRecord(id: string): void {
    this.open.emit(id);
  }
  protected recoverable(record: EventRecord): boolean {
    return canRecover(record);
  }
  protected badge(status: string): 'warning' | 'success' | 'destructive' | 'secondary' {
    return STATUS_VARIANTS[status as keyof typeof STATUS_VARIANTS] ?? 'secondary';
  }
  protected statusLabel(row: EventRow): string {
    return row.status === 'pending' && row.attempts > 0 && this.view() !== 'rejections'
      ? 'retrying'
      : row.status;
  }
  protected isRejection(): boolean {
    return this.view() === 'rejections';
  }
  protected action(): string {
    return recoveryAction(this.view());
  }
  protected reasonLabel(reason: string): string {
    const key = reasonKey(reason);
    return key ? this.t(key) : reason;
  }
  protected toggle(id: string, checked: boolean): void {
    this.selectionChange.emit(
      checked
        ? [...new Set([...this.selected(), id])].slice(0, 100)
        : this.selected().filter((value) => value !== id),
    );
  }
  protected toggleAll(checked: boolean): void {
    this.selectionChange.emit(
      checked
        ? this.rows()
            .filter((row) => row.record && canRecover(row.record))
            .map((row) => row.id)
            .slice(0, 100)
        : [],
    );
  }
  protected onRowClick(event: MouseEvent, row: EventRow): void {
    if (!(event.target as HTMLElement).closest('button,a,hlm-checkbox')) this.open.emit(row.id);
  }
  protected jump(view: EventView, status: string, eventName?: string): void {
    this.jumpTo.emit({ view, status, eventName });
  }
}
