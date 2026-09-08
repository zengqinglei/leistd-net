import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowUpDown,
  lucideChevronRight,
  lucideEllipsis,
  lucideKeyRound,
  lucidePencil,
  lucideSearchX,
  lucideShieldCheck,
  lucideSortAsc,
  lucideSortDesc,
  lucideTrash2,
} from '@ng-icons/lucide';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
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

//#if (IncludeLocalization)
import { refreshOnLanguageChange } from '../../../../../../core/i18n/translation-ready';
//#endif
import { SettingContextService } from '../../../../../../core/settings/setting-context-service';
import {
  TablePaginator,
  TablePaginatorLabels,
} from '../../../../../../shared/components/table-paginator/table-paginator';
import {
  ACTIONS_COLUMN_META,
  tableColumnVisibility,
} from '../../../../../../shared/models/table-column-meta';
import { AppDate } from '../../../../../../shared/pipes/app-date-pipe';
import { createExpandableRows } from '../../../../../../shared/utils/expandable-rows';
import { resolveTableUpdater } from '../../../../../../shared/utils/table-query-state';
import {
  tableSortAria,
  tableSortIcon,
  toggleTableSort,
} from '../../../../../../shared/utils/table-sorting';
import { tableViewportSignal } from '../../../../../../shared/utils/table-viewport';
import { RoleOutputDto } from '../../../../models/role.dto';

/**
 * 角色列表表格。
 *
 * 与用户列表同一形态：列优先级驱动响应式收纳、被隐藏的列由行展开补偿、
 * 分页与排序状态由父组件（URL 查询参数）单向下发。
 */
@Component({
  selector: 'app-role-table',
  imports: [
    AppDate,
    NgIcon,
    HlmBadge,
    HlmButton,
    HlmSpinner,
    TablePaginator,
    ...HlmDropdownMenuImports,
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
      lucideKeyRound,
      lucidePencil,
      lucideSearchX,
      lucideShieldCheck,
      lucideSortAsc,
      lucideSortDesc,
      lucideTrash2,
    }),
  ],
  templateUrl: './role-table.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RoleTable {
  // 时间统一按设置里的展示时区渲染：服务端存 UTC，每处各自用浏览器时区
  // 会让同一时刻在不同页面显示成不同时间。
  protected readonly displayTimeZone = inject(SettingContextService).timeZone;
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);

  constructor() {
    // 表头与分页文案走 transloco.translate()，没有任何被渲染的表达式依赖语言，
    // 需要显式把语言变化接到变更检测上，否则切换语言后整张表停在旧语言。
    refreshOnLanguageChange(this.transloco);
  }

  //#endif
  readonly roles = input<RoleOutputDto[]>([]);
  readonly totalCount = input(0);
  readonly pagination = input<PaginationState>({ pageIndex: 0, pageSize: 20 });
  readonly sorting = input<SortingState>([]);
  readonly loading = input(false);
  readonly filtered = input(false);

  /** 行操作按权限裁剪；隐藏只影响体验，服务端仍对每个请求独立校验。 */
  readonly canUpdate = input(true);
  readonly canDelete = input(true);
  readonly canManagePermissions = input(false);

  /** 一个可用操作都没有时不渲染溢出菜单，避免留下点开即空的按钮。 */
  readonly hasRowActions = computed(
    () => this.canUpdate() || this.canDelete() || this.canManagePermissions(),
  );

  readonly paginationChange = output<PaginationState>();
  readonly sortingChange = output<SortingState>();
  readonly edit = output<RoleOutputDto>();
  readonly delete = output<RoleOutputDto>();
  readonly managePermissions = output<RoleOutputDto>();

  private readonly tableViewport = tableViewportSignal();

  protected readonly columns: ColumnDef<RoleOutputDto>[] = [
    {
      accessorKey: 'displayName',
      id: 'displayName',
      enableHiding: false,
      meta: { priority: 'primary', locked: true },
    },
    {
      accessorKey: 'userCount',
      id: 'userCount',
      enableSorting: false,
      meta: { priority: 'secondary' },
    },
    {
      accessorKey: 'permissionCount',
      id: 'permissionCount',
      enableSorting: false,
      meta: { priority: 'secondary' },
    },
    { accessorKey: 'sort', id: 'sort', meta: { priority: 'tertiary' } },
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

  protected readonly table = createAngularTable<RoleOutputDto>(() => ({
    data: this.roles(),
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

  readonly currentPage = computed(() => this.pagination().pageIndex + 1);
  readonly totalPages = computed(() => Math.max(1, this.table.getPageCount()));

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

  columnLabel(field: 'role' | 'users' | 'permissions' | 'sort' | 'created'): string {
    //#if (IncludeLocalization)
    const keys = {
      role: 'roles.colName',
      users: 'roles.colUsers',
      permissions: 'roles.colPermissions',
      sort: 'roles.colSort',
      created: 'roles.colCreatedAt',
    } as const;
    return this.transloco.translate(keys[field]);
    //#else
    const labels = {
      role: 'Role',
      users: 'Users',
      permissions: 'Permissions',
      sort: 'Sort',
      created: 'Created at',
    } as const;
    return labels[field];
    //#endif
  }

  actionLabel(action: 'details' | 'permissions' | 'edit' | 'delete' | 'staticHint'): string {
    //#if (IncludeLocalization)
    const keys = {
      details: 'common.details',
      permissions: 'roles.configurePermissions',
      edit: 'common.edit',
      delete: 'common.delete',
      staticHint: 'roles.staticRoleHint',
    } as const;
    return this.transloco.translate(keys[action]);
    //#else
    const labels = {
      details: 'View details',
      permissions: 'Configure permissions',
      edit: 'Edit',
      delete: 'Delete',
      staticHint: 'Built-in roles cannot be deleted',
    } as const;
    return labels[action];
    //#endif
  }

  actionsLabel(): string {
    //#if (IncludeLocalization)
    return this.transloco.translate('common.actions');
    //#else
    return 'Actions';
    //#endif
  }

  badgeLabel(kind: 'static' | 'default'): string {
    //#if (IncludeLocalization)
    return this.transloco.translate(kind === 'static' ? 'roles.static' : 'roles.default');
    //#else
    return kind === 'static' ? 'Built-in' : 'Default';
    //#endif
  }

  paginatorLabels(): TablePaginatorLabels {
    //#if (IncludeLocalization)
    return {
      currentPageReport: this.transloco.translate('roles.table.currentPageReport', {
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
      currentPageReport: `${this.totalCount()} in total`,
      rowsPerPage: 'Items per page',
      page: `Page ${this.currentPage()} of ${this.totalPages()}`,
      first: 'First page',
      previous: 'Previous page',
      next: 'Next page',
      last: 'Last page',
    };
    //#endif
  }
}
