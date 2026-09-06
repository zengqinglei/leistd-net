import { DatePipe } from '@angular/common';
//#if (IncludeLocalization)
import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
//#else
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
//#endif
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowUpDown,
  lucideChevronRight,
  lucideEllipsis,
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
import { PopoverAria } from '../../../../../../shared/directives/popover-aria';
import {
  ACTIONS_COLUMN_META,
  tableColumnVisibility,
} from '../../../../../../shared/models/table-column-meta';
import { createExpandableRows } from '../../../../../../shared/utils/expandable-rows';
import { resolveTableUpdater } from '../../../../../../shared/utils/table-query-state';
import {
  tableSortAria,
  tableSortIcon,
  toggleTableSort,
} from '../../../../../../shared/utils/table-sorting';
import { tableViewportSignal } from '../../../../../../shared/utils/table-viewport';
import { OpenApplicationOutputDto } from '../../../../models/open-application.dto';

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
    PopoverAria,
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
      lucideEllipsis,
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
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);

  //#endif
  readonly applications = input<OpenApplicationOutputDto[]>([]);
  readonly totalCount = input(0);
  readonly pagination = input<PaginationState>({ pageIndex: 0, pageSize: 20 });
  readonly sorting = input<SortingState>([]);
  readonly loading = input(false);
  readonly filtered = input(false);

  /**
   * 行操作按权限裁剪。默认全开，未启用权限模块的生成物行为不变；
   * 隐藏只影响体验，服务端仍对每个请求独立校验。
   */
  readonly canUpdate = input(true);
  readonly canDelete = input(true);
  readonly canResetSecret = input(true);

  /** 一个可用操作都没有时不渲染溢出菜单，避免留下点开即空的按钮。 */
  readonly hasRowActions = computed(
    () => this.canUpdate() || this.canDelete() || this.canResetSecret(),
  );

  readonly paginationChange = output<PaginationState>();
  readonly sortingChange = output<SortingState>();
  readonly edit = output<string>();
  readonly delete = output<string>();
  readonly resetSecret = output<string>();

  private readonly tableViewport = tableViewportSignal();

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
      meta: ACTIONS_COLUMN_META,
    },
  ];

  private readonly columnVisibility = computed(() =>
    tableColumnVisibility(this.columns, this.tableViewport()),
  );

  // 移动端/平板端「行展开」补偿：被隐藏的列不会丢数据，点行首箭头即可展开查看。
  private readonly expandableRows = createExpandableRows();

  readonly hasCollapsedColumns = computed(() => this.tableViewport() !== 'desktop');

  isRowExpanded(id: string): boolean {
    return this.expandableRows.isExpanded(id);
  }

  toggleRow(id: string): void {
    this.expandableRows.toggle(id);
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
  popoverTitle(mode: PopoverMode): string {
    return mode === 'redirectUris'
      ? 'Redirect URIs'
      : this.transloco.translate('openApp.section.authorization');
  }
  //#else
  popoverTitle(mode: PopoverMode): string {
    return mode === 'redirectUris' ? 'Redirect URIs' : 'Authorization capabilities';
  }
  //#endif

  toggleSort(columnId: string): void {
    toggleTableSort(this.table, columnId);
  }

  sortIcon(columnId: string): string {
    return tableSortIcon(this.table, columnId);
  }

  sortAria(columnId: string): 'ascending' | 'descending' | 'none' {
    return tableSortAria(this.table, columnId);
  }

  /** 每页条数变化：回到第一页并广播新的分页状态。 */
  changePageSize(pageSize: number): void {
    this.paginationChange.emit({ pageIndex: 0, pageSize });
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
