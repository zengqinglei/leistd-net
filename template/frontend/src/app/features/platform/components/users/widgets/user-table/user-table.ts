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
  lucideBan,
  lucideChevronRight,
  lucideCircleCheck,
  lucideEllipsis,
  lucideKey,
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
import {
  TablePaginator,
  TablePaginatorLabels,
} from '../../../../../../shared/components/table-paginator/table-paginator';
import { Role } from '../../../../../../shared/models/role.enum';
import { tableColumnVisibility } from '../../../../../../shared/models/table-column-meta';
import { getRoleLabel } from '../../../../../../shared/pipes/role-label-pipe';
import { resolveTableUpdater } from '../../../../../../shared/utils/table-query-state';
import { UserManagementOutputDto } from '../../../../models/user-management.dto';

const MEDIUM_VIEWPORT = '(min-width: 768px)';
const LARGE_VIEWPORT = '(min-width: 1024px)';

/** Badge 变体。 */
type BadgeVariant = 'default' | 'secondary' | 'destructive' | 'outline';

@Component({
  selector: 'app-user-table',
  imports: [
    DatePipe,
    NgIcon,
    HlmBadge,
    HlmButton,
    HlmSpinner,
    TablePaginator,
    ...HlmAvatarImports,
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
      lucideBan,
      lucideChevronRight,
      lucideCircleCheck,
      lucideEllipsis,
      lucideKey,
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
  private readonly breakpointObserver = inject(BreakpointObserver);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif

  readonly users = input<UserManagementOutputDto[]>([]);
  readonly totalCount = input(0);
  readonly pagination = input<PaginationState>({ pageIndex: 0, pageSize: 20 });
  readonly sorting = input<SortingState>([]);
  readonly loading = input(false);
  readonly filtered = input(false);

  readonly paginationChange = output<PaginationState>();
  readonly sortingChange = output<SortingState>();
  readonly edit = output<string>();
  readonly toggleActive = output<UserManagementOutputDto>();
  readonly resetPassword = output<string>();
  readonly delete = output<UserManagementOutputDto>();

  // 角色溢出 popover 状态（对齐参考站的现代化布局）。
  readonly activeRoles = signal<string[]>([]);
  readonly rolePopoverOpen = signal<'open' | 'closed'>('closed');

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
    { accessorKey: 'lastLoginTime', id: 'lastLoginTime', meta: { priority: 'tertiary' } },
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
  readonly rolePopoverTitle = computed(() =>
    this.transloco.translate('users.popover.rolesTitle', { count: this.activeRoles().length }),
  );
  //#else
  readonly rolePopoverTitle = computed(() => `Roles (${this.activeRoles().length})`);
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

  /** 打开角色溢出 popover，展示被折叠的角色。 */
  openRolesPopover(roles: string[]): void {
    this.activeRoles.set(roles);
    this.rolePopoverOpen.set('open');
  }

  getVisibleRoles(user: UserManagementOutputDto): string[] {
    return user.roles.slice(0, 2);
  }

  getHiddenRoles(user: UserManagementOutputDto): string[] {
    return user.roles.slice(2);
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

  emailVerifiedLabel(verified: boolean): string {
    //#if (IncludeLocalization)
    return this.transloco.translate(
      verified ? 'users.status.emailVerified' : 'users.status.emailUnverified',
    );
    //#else
    return verified ? 'Email verified' : 'Email not verified';
    //#endif
  }

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

  getRoleVariant(role: string): BadgeVariant {
    // 角色用中性强弱层级；红色（destructive）专留给危险/删除操作。
    const roleMap: Record<Role, BadgeVariant> = {
      [Role.Admin]: 'default',
      [Role.Operator]: 'secondary',
      [Role.Member]: 'outline',
    };
    return roleMap[role as Role] ?? 'outline';
  }

  getRoleLabel(role: string): string {
    const label = getRoleLabel(role);
    //#if (IncludeLocalization)
    return this.transloco.translate(label);
    //#else
    return label;
    //#endif
  }
}
