import { BreakpointObserver, BreakpointState } from '@angular/cdk/layout';
import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowUpDown,
  lucideChevronRight,
  lucideEllipsisVertical,
  lucideInbox,
  lucidePencil,
  lucideRefreshCw,
  lucideSearchX,
  lucideShield,
  lucideSortAsc,
  lucideSortDesc,
  lucideTrash2,
} from '@ng-icons/lucide';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import {
  ColumnDef,
  createAngularTable,
  getCoreRowModel,
  PaginationState,
  SortingState,
} from '@tanstack/angular-table';

import {
  TablePaginator,
  TablePaginatorLabels,
} from '../../../../../../shared/components/table-paginator/table-paginator';
import { tableColumnVisibility } from '../../../../../../shared/models/table-column-meta';
import { resolveTableUpdater } from '../../../../../../shared/utils/table-query-state';
import { OpenApplicationOutputDto } from '../../../../models/open-application.dto';

const MEDIUM_VIEWPORT = '(min-width: 768px)';
const LARGE_VIEWPORT = '(min-width: 1024px)';

type PopoverMode = 'permissions' | 'redirectUris';

/** Badge 变体。 */
type BadgeVariant = 'default' | 'secondary' | 'destructive' | 'outline';

@Component({
  selector: 'app-open-application-table',
  imports: [
    DatePipe,
    NgIcon,
    HlmBadge,
    HlmButton,
    HlmSpinner,
    TablePaginator,
    ...HlmDropdownMenuImports,
    ...HlmPopoverImports,
    ...HlmTableImports,
    ...HlmTooltipImports,
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
  ],
  providers: [
    provideIcons({
      lucideArrowUpDown,
      lucideChevronRight,
      lucideEllipsisVertical,
      lucideInbox,
      lucidePencil,
      lucideRefreshCw,
      lucideSearchX,
      lucideShield,
      lucideSortAsc,
      lucideSortDesc,
      lucideTrash2,
    }),
  ],
  templateUrl: './open-application-table.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OpenApplicationTable {
  private readonly breakpointObserver = inject(BreakpointObserver);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif

  readonly applications = input<OpenApplicationOutputDto[]>([]);
  readonly totalCount = input(0);
  readonly pagination = input<PaginationState>({ pageIndex: 0, pageSize: 20 });
  readonly sorting = input<SortingState>([]);
  readonly loading = input(false);
  readonly filtered = input(false);

  readonly paginationChange = output<PaginationState>();
  readonly sortingChange = output<SortingState>();
  readonly edit = output<string>();
  readonly delete = output<string>();
  readonly resetSecret = output<string>();

  // 权限 / Redirect URI 溢出 popover 状态（对齐参考站的现代化布局）。
  readonly activeItems = signal<string[]>([]);
  readonly popoverMode = signal<PopoverMode>('permissions');
  readonly popoverOpen = signal<'open' | 'closed'>('closed');

  private readonly viewport = toSignal(
    this.breakpointObserver.observe([MEDIUM_VIEWPORT, LARGE_VIEWPORT]),
    {
      initialValue: {
        matches: false,
        breakpoints: { [MEDIUM_VIEWPORT]: false, [LARGE_VIEWPORT]: false },
      } satisfies BreakpointState,
    },
  );

  private readonly tableViewport = computed(() => {
    const breakpoints = this.viewport().breakpoints;
    if (breakpoints[LARGE_VIEWPORT] === true) return 'desktop' as const;
    if (breakpoints[MEDIUM_VIEWPORT] === true) return 'tablet' as const;
    return 'mobile' as const;
  });

  protected readonly columns: ColumnDef<OpenApplicationOutputDto>[] = [
    {
      accessorKey: 'clientId',
      id: 'clientId',
      enableHiding: false,
      meta: { priority: 'primary', locked: true },
    },
    {
      accessorKey: 'applicationType',
      id: 'type',
      enableSorting: false,
      enableHiding: false,
      meta: { priority: 'primary', locked: true },
    },
    {
      accessorKey: 'permissions',
      id: 'permissions',
      enableSorting: false,
      meta: { priority: 'secondary' },
    },
    {
      accessorKey: 'redirectUris',
      id: 'redirectUris',
      enableSorting: false,
      meta: { priority: 'tertiary' },
    },
    {
      accessorKey: 'requirements',
      id: 'security',
      enableSorting: false,
      meta: { priority: 'secondary' },
    },
    { accessorKey: 'creationTime', id: 'creationTime', meta: { priority: 'tertiary' } },
    {
      id: 'actions',
      enableSorting: false,
      enableHiding: false,
      meta: {
        priority: 'primary',
        locked: true,
        headClass:
          'w-px px-2 text-right whitespace-nowrap sticky right-0 z-20 bg-card border-l border-border',
        cellClass:
          'w-px px-2 py-2 whitespace-nowrap sticky right-0 z-10 bg-card group-hover:bg-muted/50 border-l border-border',
      },
    },
  ];

  private readonly columnVisibility = computed(() =>
    tableColumnVisibility(this.columns, this.tableViewport()),
  );

  // 移动端/平板端「行展开」补偿：被隐藏的列不会丢数据，点行首箭头即可展开查看。
  private readonly expandedRows = signal<ReadonlySet<string>>(new Set());
  readonly hasCollapsedColumns = computed(() => this.tableViewport() !== 'desktop');

  isRowExpanded(id: string): boolean {
    return this.expandedRows().has(id);
  }

  toggleRow(id: string): void {
    const next = new Set(this.expandedRows());
    if (!next.delete(id)) {
      next.add(id);
    }
    this.expandedRows.set(next);
  }

  isColumnHidden(id: string): boolean {
    return this.table.getColumn(id)?.getIsVisible() === false;
  }

  protected readonly table = createAngularTable<OpenApplicationOutputDto>(() => ({
    data: this.applications(),
    columns: this.columns,
    getCoreRowModel: getCoreRowModel(),
    manualPagination: true,
    manualSorting: true,
    rowCount: this.totalCount(),
    onPaginationChange: (updater) =>
      this.paginationChange.emit(resolveTableUpdater(updater, this.pagination())),
    onSortingChange: (updater) =>
      this.sortingChange.emit(resolveTableUpdater(updater, this.sorting())),
    state: {
      columnVisibility: this.columnVisibility(),
      pagination: this.pagination(),
      sorting: this.sorting(),
    },
  }));

  // 分页派生（供 OURS 分页栏使用）。
  readonly currentPage = computed(() => this.pagination().pageIndex + 1);
  readonly totalPages = computed(() => Math.max(1, this.table.getPageCount()));

  //#if (IncludeLocalization)
  readonly popoverTitle = computed(() => {
    switch (this.popoverMode()) {
      case 'redirectUris':
        return 'Redirect URIs';
      default:
        return this.transloco.translate('openApp.section.authorization');
    }
  });
  //#else
  readonly popoverTitle = computed(() => {
    switch (this.popoverMode()) {
      case 'redirectUris':
        return 'Redirect URIs';
      default:
        return 'Authorization capabilities';
    }
  });
  //#endif

  toggleSort(columnId: string): void {
    const column = this.table.getColumn(columnId);
    column?.toggleSorting(column.getIsSorted() === 'asc');
  }

  sortIcon(columnId: string): string {
    const direction = this.table.getColumn(columnId)?.getIsSorted();
    return direction === 'asc'
      ? 'lucideSortAsc'
      : direction === 'desc'
        ? 'lucideSortDesc'
        : 'lucideArrowUpDown';
  }

  sortAria(columnId: string): 'ascending' | 'descending' | 'none' {
    const direction = this.table.getColumn(columnId)?.getIsSorted();
    return direction === 'asc' ? 'ascending' : direction === 'desc' ? 'descending' : 'none';
  }

  /** 每页条数变化：回到第一页并广播新的分页状态。 */
  changePageSize(pageSize: number): void {
    this.paginationChange.emit({ pageIndex: 0, pageSize });
  }

  /** 打开权限 / Redirect URI 溢出 popover。 */
  openPopover(mode: PopoverMode, items: string[]): void {
    this.popoverMode.set(mode);
    this.activeItems.set(items);
    this.popoverOpen.set('open');
  }

  getVisibleRedirectUris(application: OpenApplicationOutputDto): string[] {
    return application.redirectUris.slice(0, 1);
  }

  getHiddenRedirectUris(application: OpenApplicationOutputDto): string[] {
    return application.redirectUris.slice(1);
  }

  paginatorLabels(): TablePaginatorLabels {
    //#if (IncludeLocalization)
    return {
      currentPageReport: this.transloco.translate('openApp.table.currentPageReport', {
        total: this.totalCount(),
      }),
      rowsPerPage: this.transloco.translate('common.rowsPerPage'),
      page: this.transloco.translate('common.pageOf', {
        page: this.currentPage(),
        total: this.totalPages(),
      }),
      first: this.transloco.translate('common.pagination.first'),
      previous: this.transloco.translate('common.pagination.previous'),
      next: this.transloco.translate('common.pagination.next'),
      last: this.transloco.translate('common.pagination.last'),
    };
    //#else
    return {
      currentPageReport: `${this.totalCount()} total`,
      rowsPerPage: 'Items per page',
      page: `Page ${this.currentPage()} of ${this.totalPages()}`,
      first: 'First page',
      previous: 'Previous page',
      next: 'Next page',
      last: 'Last page',
    };
    //#endif
  }

  actionsLabel(): string {
    //#if (IncludeLocalization)
    return this.transloco.translate('common.actions');
    //#else
    return 'Actions';
    //#endif
  }

  detailsLabel(): string {
    //#if (IncludeLocalization)
    return this.transloco.translate('common.details');
    //#else
    return 'View details';
    //#endif
  }

  detailLabel(field: 'permissions' | 'redirectUris' | 'security' | 'created'): string {
    //#if (IncludeLocalization)
    const keys = {
      permissions: 'openApp.table.colPermissions',
      redirectUris: 'openApp.table.colRedirectUris',
      security: 'openApp.table.colSecurity',
      created: 'openApp.table.colCreatedAt',
    } as const;
    return this.transloco.translate(keys[field]);
    //#else
    return {
      permissions: 'Capabilities',
      redirectUris: 'Redirect URI',
      security: 'Security',
      created: 'Created at',
    }[field];
    //#endif
  }

  pkceBadgeLabel(application: OpenApplicationOutputDto): string {
    if (this.hasPkce(application)) {
      return 'PKCE';
    }
    //#if (IncludeLocalization)
    return this.transloco.translate('openApp.table.noPkce');
    //#else
    return 'No PKCE';
    //#endif
  }

  secretBadgeLabel(application: OpenApplicationOutputDto): string {
    //#if (IncludeLocalization)
    return this.transloco.translate(
      application.hasClientSecret ? 'openApp.table.secretSet' : 'openApp.table.noSecret',
    );
    //#else
    return application.hasClientSecret ? 'Secret set' : 'No secret';
    //#endif
  }

  actionLabel(action: 'details' | 'edit' | 'reset' | 'delete'): string {
    //#if (IncludeLocalization)
    const keys = {
      details: 'common.details',
      edit: 'common.edit',
      reset: 'openApp.action.resetSecret',
      delete: 'common.delete',
    } as const;
    return this.transloco.translate(keys[action]);
    //#else
    return {
      details: 'View details',
      edit: 'Edit',
      reset: 'Reset secret',
      delete: 'Delete',
    }[action];
    //#endif
  }

  getApplicationTypeLabel(value: string): string {
    //#if (IncludeLocalization)
    const labels: Record<string, string> = {
      web: 'Web',
      native: this.transloco.translate('openApp.appType.native'),
      service: this.transloco.translate('openApp.appType.service'),
    };
    //#else
    const labels: Record<string, string> = {
      web: 'Web',
      native: 'Desktop/Native',
      service: 'Service',
    };
    //#endif
    return labels[value] ?? value;
  }

  getClientTypeVariant(value: string): BadgeVariant {
    return value === 'public' ? 'secondary' : 'default';
  }

  getConsentTypeLabel(value: string): string {
    //#if (IncludeLocalization)
    const labels: Record<string, string> = {
      implicit: this.transloco.translate('openApp.consentType.implicit'),
      explicit: this.transloco.translate('openApp.consentType.explicit'),
      external: this.transloco.translate('openApp.consentType.external'),
      systematic: this.transloco.translate('openApp.consentType.systematic'),
    };
    //#else
    const labels: Record<string, string> = {
      implicit: 'Implicit consent',
      explicit: 'Explicit consent',
      external: 'External consent',
      systematic: 'Systematic consent',
    };
    //#endif
    return labels[value] ?? value;
  }

  getPermissionSummary(application: OpenApplicationOutputDto): string {
    const grants = application.permissions
      .filter((permission) => permission.startsWith('gt:'))
      .map((permission) => permission.replace('gt:', ''));
    //#if (IncludeLocalization)
    return grants.length
      ? grants.join(' / ')
      : this.transloco.translate('openApp.permission.notConfigured');
    //#else
    return grants.length ? grants.join(' / ') : 'Not configured';
    //#endif
  }

  hasPkce(application: OpenApplicationOutputDto): boolean {
    return application.requirements.includes('ft:pkce');
  }
}
