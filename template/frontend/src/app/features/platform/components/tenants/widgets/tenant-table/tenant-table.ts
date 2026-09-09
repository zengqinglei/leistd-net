import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBan,
  lucideBuilding2,
  lucideChevronRight,
  lucideCircleCheck,
  lucideEllipsis,
  lucideInfo,
  lucidePencil,
  lucideSearchX,
  lucideTrash2,
} from '@ng-icons/lucide';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { ColumnDef, PaginationState } from '@tanstack/angular-table';

//#if (IncludeLocalization)
import { refreshOnLanguageChange } from '../../../../../../core/i18n/translation-ready';
//#endif
import { SettingContextService } from '../../../../../../core/settings/setting-context-service';
import {
  TablePaginator,
  TablePaginatorLabels,
} from '../../../../../../shared/components/table-paginator/table-paginator';
import { TenantOutputDto } from '../../../../../../shared/dtos/tenant.dto';
import {
  ACTIONS_COLUMN_META,
  tableColumnVisibility,
} from '../../../../../../shared/models/table-column-meta';
import {
  injectAppTable,
  type AppTableFeatures,
} from '../../../../../../shared/models/table-features';
import { AppDate } from '../../../../../../shared/pipes/app-date-pipe';
import { createExpandableRows } from '../../../../../../shared/utils/expandable-rows';
import { resolveTableUpdater } from '../../../../../../shared/utils/table-query-state';
import { tableViewportSignal } from '../../../../../../shared/utils/table-viewport';

/**
 * 租户列表表格。
 *
 * 与角色/用户列表同一形态：列优先级驱动响应式收纳、被隐藏的列由行展开补偿、
 * 分页状态由父组件（URL 查询参数）单向下发。
 */
@Component({
  selector: 'app-tenant-table',
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
      lucideBan,
      lucideBuilding2,
      lucideChevronRight,
      lucideCircleCheck,
      lucideEllipsis,
      lucideInfo,
      lucidePencil,
      lucideSearchX,
      lucideTrash2,
    }),
  ],
  templateUrl: './tenant-table.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TenantTable {
  // 时间统一按设置里的展示时区渲染：服务端存 UTC，每处各自用浏览器时区
  // 会让同一时刻在不同页面显示成不同时间。
  protected readonly displayTimeZone = inject(SettingContextService).timeZone;
  protected readonly displayLocale = inject(SettingContextService).displayLocale;
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);

  constructor() {
    // 表头与分页文案走 transloco.translate()，需显式把语言变化接到变更检测上。
    refreshOnLanguageChange(this.transloco);
  }

  //#endif
  readonly tenants = input<TenantOutputDto[]>([]);
  readonly totalCount = input(0);
  readonly pagination = input<PaginationState>({ pageIndex: 0, pageSize: 20 });
  readonly loading = input(false);
  readonly filtered = input(false);

  /** 行操作按权限裁剪；隐藏只影响体验，服务端仍对每个请求独立校验。 */
  readonly canUpdate = input(true);
  readonly canDelete = input(true);

  readonly paginationChange = output<PaginationState>();
  readonly details = output<TenantOutputDto>();
  readonly edit = output<TenantOutputDto>();
  readonly toggleActive = output<TenantOutputDto>();
  readonly delete = output<TenantOutputDto>();

  private readonly tableViewport = tableViewportSignal();

  // 后端租户列表不支持字段排序（契约只有 offset/limit/keyword），全部列不排序。
  protected readonly columns: ColumnDef<AppTableFeatures, TenantOutputDto>[] = [
    {
      accessorKey: 'name',
      id: 'name',
      enableSorting: false,
      enableHiding: false,
      meta: { priority: 'primary', locked: true },
    },
    {
      accessorKey: 'displayName',
      id: 'displayName',
      enableSorting: false,
      meta: { priority: 'secondary' },
    },
    {
      accessorKey: 'description',
      id: 'description',
      enableSorting: false,
      meta: { priority: 'tertiary' },
    },
    {
      accessorKey: 'isActive',
      id: 'status',
      enableSorting: false,
      meta: { priority: 'secondary' },
    },
    {
      accessorKey: 'creationTime',
      id: 'creationTime',
      enableSorting: false,
      meta: { priority: 'tertiary' },
    },
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

  protected readonly table = injectAppTable(() => ({
    data: this.tenants(),
    columns: this.columns,
    manualPagination: true,
    rowCount: this.totalCount(),
    onPaginationChange: (updater) =>
      this.paginationChange.emit(resolveTableUpdater(updater, this.pagination())),
    state: {
      columnVisibility: this.columnVisibility(),
      pagination: this.pagination(),
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

  /** 每页条数变化：回到第一页并广播新的分页状态。 */
  changePageSize(pageSize: number): void {
    this.paginationChange.emit({ pageIndex: 0, pageSize });
  }

  columnLabel(field: 'name' | 'displayName' | 'description' | 'status' | 'created'): string {
    //#if (IncludeLocalization)
    const keys = {
      name: 'tenants.colName',
      displayName: 'tenants.colDisplayName',
      description: 'tenants.colDescription',
      status: 'tenants.colStatus',
      created: 'tenants.colCreatedAt',
    } as const;
    return this.transloco.translate(keys[field]);
    //#else
    const labels = {
      name: 'Name',
      displayName: 'Display name',
      description: 'Description',
      status: 'Status',
      created: 'Created at',
    } as const;
    return labels[field];
    //#endif
  }

  actionLabel(action: 'details' | 'edit' | 'delete'): string {
    //#if (IncludeLocalization)
    const keys = {
      details: 'common.details',
      edit: 'common.edit',
      delete: 'common.delete',
    } as const;
    return this.transloco.translate(keys[action]);
    //#else
    const labels = {
      details: 'View details',
      edit: 'Edit',
      delete: 'Delete',
    } as const;
    return labels[action];
    //#endif
  }

  toggleLabel(isActive: boolean): string {
    //#if (IncludeLocalization)
    return this.transloco.translate(isActive ? 'tenants.deactivate' : 'tenants.activate');
    //#else
    return isActive ? 'Deactivate' : 'Activate';
    //#endif
  }

  actionsLabel(): string {
    //#if (IncludeLocalization)
    return this.transloco.translate('common.actions');
    //#else
    return 'Actions';
    //#endif
  }

  statusLabel(isActive: boolean): string {
    //#if (IncludeLocalization)
    return this.transloco.translate(isActive ? 'tenants.active' : 'tenants.inactive');
    //#else
    return isActive ? 'Active' : 'Inactive';
    //#endif
  }

  paginatorLabels(): TablePaginatorLabels {
    //#if (IncludeLocalization)
    return {
      currentPageReport: this.transloco.translate('tenants.table.currentPageReport', {
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
