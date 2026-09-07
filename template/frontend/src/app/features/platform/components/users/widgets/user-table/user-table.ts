import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowUpDown,
  lucideBan,
  lucideChevronRight,
  lucideCircleCheck,
  lucideEllipsis,
  lucideKey,
  lucideShieldCheck,
  lucidePencil,
  lucideSearchX,
  lucideSortAsc,
  lucideSortDesc,
  lucideTrash2,
  lucideUsers,
} from '@ng-icons/lucide';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';
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

import { AuthService } from '../../../../../../core/services/auth-service';
import { SettingContextService } from '../../../../../../core/settings/setting-context-service';
import {
  TablePaginator,
  TablePaginatorLabels,
} from '../../../../../../shared/components/table-paginator/table-paginator';
import { PopoverAria } from '../../../../../../shared/directives/popover-aria';
import {
  ACTIONS_COLUMN_META,
  tableColumnVisibility,
} from '../../../../../../shared/models/table-column-meta';
import { AppDate } from '../../../../../../shared/pipes/app-date.pipe';
import { createExpandableRows } from '../../../../../../shared/utils/expandable-rows';
import { resolveTableUpdater } from '../../../../../../shared/utils/table-query-state';
import {
  tableSortAria,
  tableSortIcon,
  toggleTableSort,
} from '../../../../../../shared/utils/table-sorting';
import { tableViewportSignal } from '../../../../../../shared/utils/table-viewport';
import { RoleBriefDto } from '../../../../models/role.dto';
import { UserManagementOutputDto } from '../../../../models/user-management.dto';

/** Badge 变体。 */
type BadgeVariant = 'default' | 'secondary' | 'destructive' | 'outline';

@Component({
  selector: 'app-user-table',
  imports: [
    AppDate,
    NgIcon,
    HlmBadge,
    HlmButton,
    HlmSpinner,
    TablePaginator,
    ...HlmAvatarImports,
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
      lucideBan,
      lucideChevronRight,
      lucideCircleCheck,
      lucideEllipsis,
      lucideKey,
      lucideShieldCheck,
      lucidePencil,
      lucideSearchX,
      lucideSortAsc,
      lucideSortDesc,
      lucideTrash2,
      lucideUsers,
    }),
  ],
  templateUrl: './user-table.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserTable {
  private readonly authService = inject(AuthService);
  // 时间统一按设置里的展示时区渲染：服务端存 UTC，每处各自用浏览器时区
  // 会让同一时刻在不同页面显示成不同时间。
  protected readonly displayTimeZone = inject(SettingContextService).timeZone;
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif

  readonly users = input<UserManagementOutputDto[]>([]);
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
  readonly canManageRoles = input(false);

  /** 一个可用操作都没有时不渲染溢出菜单，避免留下点开即空的按钮。 */
  // prettier-ignore
  readonly hasRowActions = computed(
    () =>
      this.canUpdate() ||
      this.canDelete() ||
      this.canManageRoles() ||
      false,
  );

  readonly paginationChange = output<PaginationState>();
  readonly sortingChange = output<SortingState>();
  readonly edit = output<string>();
  readonly toggleActive = output<UserManagementOutputDto>();
  //#if (LocalIdentity)
  readonly resetPassword = output<string>();
  //#endif
  readonly delete = output<UserManagementOutputDto>();
  /** 角色分配是独立命令，与资料编辑分开触发。 */
  readonly manageRoles = output<UserManagementOutputDto>();

  private readonly tableViewport = tableViewportSignal();

  protected readonly columns: ColumnDef<UserManagementOutputDto>[] = [
    {
      accessorKey: 'username',
      id: 'username',
      enableHiding: false,
      meta: { priority: 'primary', locked: true },
    },
    { accessorKey: 'email', id: 'email', meta: { priority: 'secondary' } },
    { accessorKey: 'roles', id: 'roles', enableSorting: false, meta: { priority: 'secondary' } },
    {
      accessorKey: 'isActive',
      id: 'status',
      enableSorting: false,
      enableHiding: false,
      meta: { priority: 'primary', locked: true },
    },
    //#if (LocalIdentity)
    { accessorKey: 'lastLoginTime', id: 'lastLoginTime', meta: { priority: 'tertiary' } },
    //#endif
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
  detailLabel(field: 'email' | 'roles' | 'lastLogin' | 'created'): string {
    //#if (IncludeLocalization)
    const keys = {
      email: 'users.table.colEmail',
      roles: 'users.table.colRole',
      lastLogin: 'users.table.colLastLogin',
      created: 'users.table.colCreatedAt',
    } as const;
    return this.transloco.translate(keys[field]);
    //#else
    const labels = {
      email: 'Email',
      roles: 'Role',
      lastLogin: 'Last sign-in',
      created: 'Created at',
    } as const;
    return labels[field];
    //#endif
  }

  detailsLabel(): string {
    //#if (IncludeLocalization)
    return this.transloco.translate('common.details');
    //#else
    return 'View details';
    //#endif
  }

  protected readonly table = createAngularTable<UserManagementOutputDto>(() => ({
    data: this.users(),
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
  rolesPopoverTitle(count: number): string {
    return this.transloco.translate('users.popover.rolesTitle', { count });
  }
  //#else
  rolesPopoverTitle(count: number): string {
    return `Roles (${count})`;
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
  //#if (IncludeLocalization)
  rolesActionLabel(): string {
    return this.transloco.translate('users.actions.manageRoles');
  }

  //#else
  rolesActionLabel(): string {
    return 'Assign roles';
  }

  //#endif
  getVisibleRoles(user: UserManagementOutputDto): RoleBriefDto[] {
    return (user.roles ?? []).slice(0, 2);
  }

  getHiddenRoles(user: UserManagementOutputDto): RoleBriefDto[] {
    return (user.roles ?? []).slice(2);
  }

  paginatorLabels(): TablePaginatorLabels {
    //#if (IncludeLocalization)
    return {
      currentPageReport: this.transloco.translate('users.table.currentPageReport', {
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

  actionsLabel(): string {
    //#if (IncludeLocalization)
    return this.transloco.translate('common.actions');
    //#else
    return 'Actions';
    //#endif
  }

  statusLabel(isActive: boolean): string {
    //#if (IncludeLocalization)
    return this.transloco.translate(isActive ? 'users.status.active' : 'users.status.inactive');
    //#else
    return isActive ? 'Active' : 'Disabled';
    //#endif
  }
  //#if (LocalIdentity)
  emailVerifiedLabel(verified: boolean): string {
    //#if (IncludeLocalization)
    return this.transloco.translate(
      verified ? 'users.status.emailVerified' : 'users.status.emailUnverified',
    );
    //#else
    return verified ? 'Email verified' : 'Email not verified';
    //#endif
  }
  //#endif

  actionLabel(
    action: 'details' | 'edit' | 'toggle' | 'reset' | 'delete',
    user: UserManagementOutputDto,
  ): string {
    //#if (IncludeLocalization)
    const keys = {
      details: 'common.details',
      edit: 'common.edit',
      toggle: user.isActive ? 'users.tooltip.disable' : 'users.tooltip.enable',
      reset: 'users.tooltip.resetPassword',
      delete: 'common.delete',
    } as const;
    return this.transloco.translate(keys[action]);
    //#else
    const labels = {
      details: 'View details',
      edit: 'Edit',
      toggle: user.isActive ? 'Disable' : 'Enable',
      reset: 'Reset password',
      delete: 'Delete',
    } as const;
    return labels[action];
    //#endif
  }

  isSuperAdmin(user: UserManagementOutputDto): boolean {
    return user.isSuperAdmin;
  }

  isOtherSuperAdmin(user: UserManagementOutputDto): boolean {
    return user.isSuperAdmin && user.id !== this.authService.currentUser()?.id;
  }

  isSelfSuperAdmin(user: UserManagementOutputDto): boolean {
    return user.isSuperAdmin && user.id === this.authService.currentUser()?.id;
  }

  /**
   * 角色徽章样式。
   *
   * 角色由管理员自由创建，前端无法也不应预知有哪些角色，因此统一使用中性样式，
   * 只用「是否默认角色」这类结构信息做弱区分；红色（destructive）专留给危险/删除操作。
   */
  getRoleVariant(): BadgeVariant {
    return 'outline';
  }
}
