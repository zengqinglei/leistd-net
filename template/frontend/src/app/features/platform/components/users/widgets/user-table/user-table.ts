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
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowUpDown,
  lucideBan,
  lucideCircleCheck,
  lucideEllipsisVertical,
  lucideKey,
  lucidePencil,
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
import { HlmTableImports } from '@spartan-ng/helm/table';

import { AuthService } from '../../../../../../core/services/auth-service';
import { TablePaginator } from '../../../../../../shared/components/table-paginator/table-paginator';
import { Role } from '../../../../../../shared/models/role.enum';
import { getRoleLabel } from '../../../../../../shared/pipes/role-label-pipe';
import { UserManagementOutputDto } from '../../../../models/user-management.dto';

export interface UserTableFilterEvent {
  offset: number;
  limit: number;
  sorting?: string;
}

/** Badge 变体。 */
type BadgeVariant = 'default' | 'secondary' | 'destructive' | 'outline';

@Component({
  selector: 'app-user-table',
  imports: [
    DatePipe,
    NgIcon,
    HlmButton,
    HlmBadge,
    ...HlmAvatarImports,
    ...HlmTableImports,
    ...HlmPopoverImports,
    ...HlmDropdownMenuImports,
    TablePaginator,
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
  ],
  providers: [
    provideIcons({
      lucideArrowUpDown,
      lucideBan,
      lucideCircleCheck,
      lucideEllipsisVertical,
      lucideKey,
      lucidePencil,
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
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);

  readonly currentPageReport = () =>
    this.transloco.translate('users.table.currentPageReport', { total: this.totalRecords() });
  readonly rowsPerPageLabel = () => this.transloco.translate('common.rowsPerPage');
  readonly pageLabel = () =>
    this.transloco.translate('common.pageOf', {
      page: this.currentPage(),
      total: this.totalPages(),
    });
  readonly firstPageLabel = () => this.transloco.translate('common.pagination.first');
  readonly prevPageLabel = () => this.transloco.translate('common.pagination.previous');
  readonly nextPageLabel = () => this.transloco.translate('common.pagination.next');
  readonly lastPageLabel = () => this.transloco.translate('common.pagination.last');

  readonly statusLabel = (isActive: boolean) =>
    this.transloco.translate(isActive ? 'users.status.active' : 'users.status.inactive');

  readonly emailVerifiedLabel = (verified: boolean) =>
    this.transloco.translate(
      verified ? 'users.status.emailVerified' : 'users.status.emailUnverified',
    );

  readonly editTooltip = (otherSuperAdmin: boolean) =>
    this.transloco.translate(otherSuperAdmin ? 'users.tooltip.superAdminNoEdit' : 'common.edit');

  readonly toggleActiveTooltip = (item: UserManagementOutputDto) =>
    this.transloco.translate(
      this.isOtherSuperAdmin(item)
        ? 'users.tooltip.superAdminNoDisable'
        : this.isSelfSuperAdmin(item)
          ? 'users.tooltip.superAdminNoDisableSelf'
          : item.isActive
            ? 'users.tooltip.disable'
            : 'users.tooltip.enable',
    );

  readonly resetPasswordTooltip = (otherSuperAdmin: boolean) =>
    this.transloco.translate(
      otherSuperAdmin ? 'users.tooltip.superAdminNoReset' : 'users.tooltip.resetPassword',
    );

  readonly deleteTooltip = (superAdmin: boolean) =>
    this.transloco.translate(superAdmin ? 'users.tooltip.superAdminNoDelete' : 'common.delete');

  // 操作下拉菜单标签
  readonly actionsLabel = () => this.transloco.translate('common.actions');
  readonly editLabel = () => this.transloco.translate('common.edit');
  readonly toggleLabel = (item: UserManagementOutputDto) =>
    this.transloco.translate(item.isActive ? 'users.tooltip.disable' : 'users.tooltip.enable');
  readonly resetLabel = () => this.transloco.translate('users.tooltip.resetPassword');
  readonly deleteLabel = () => this.transloco.translate('common.delete');
  //#else
  readonly currentPageReport = () => `${this.totalRecords()} in total`;
  readonly rowsPerPageLabel = () => 'Items per page';
  readonly pageLabel = () => `Page ${this.currentPage()} of ${this.totalPages()}`;
  readonly firstPageLabel = () => 'First page';
  readonly prevPageLabel = () => 'Previous page';
  readonly nextPageLabel = () => 'Next page';
  readonly lastPageLabel = () => 'Last page';

  readonly statusLabel = (isActive: boolean) => (isActive ? 'Active' : 'Disabled');

  readonly emailVerifiedLabel = (verified: boolean) =>
    verified ? 'Email verified' : 'Email not verified';

  readonly editTooltip = (otherSuperAdmin: boolean) =>
    otherSuperAdmin
      ? 'The built-in super administrator cannot be updated by other administrators'
      : 'Edit';

  readonly toggleActiveTooltip = (item: UserManagementOutputDto) =>
    this.isOtherSuperAdmin(item)
      ? 'The built-in super administrator cannot be disabled by other administrators'
      : this.isSelfSuperAdmin(item)
        ? 'The built-in super administrator cannot disable itself'
        : item.isActive
          ? 'Disable'
          : 'Enable';

  readonly resetPasswordTooltip = (otherSuperAdmin: boolean) =>
    otherSuperAdmin
      ? "The built-in super administrator's password cannot be reset by other administrators"
      : 'Reset password';

  readonly deleteTooltip = (superAdmin: boolean) =>
    superAdmin ? 'The built-in super administrator cannot be deleted' : 'Delete';

  // 操作下拉菜单标签
  readonly actionsLabel = () => 'Actions';
  readonly editLabel = () => 'Edit';
  readonly toggleLabel = (item: UserManagementOutputDto) => (item.isActive ? 'Disable' : 'Enable');
  readonly resetLabel = () => 'Reset password';
  readonly deleteLabel = () => 'Delete';
  //#endif

  readonly users = input.required<UserManagementOutputDto[]>();
  readonly totalRecords = input.required<number>();
  readonly loading = input<boolean>(false);

  readonly edit = output<string>();
  readonly toggleActive = output<UserManagementOutputDto>();
  readonly resetPassword = output<string>();
  readonly deleteRequested = output<UserManagementOutputDto>();
  readonly filterChange = output<UserTableFilterEvent>();

  readonly first = signal(0);
  readonly rows = signal(20);
  sortField = signal('username');
  sortOrder = signal(1);
  activeRoles = signal<string[]>([]);
  readonly rolePopoverOpen = signal<'open' | 'closed'>('closed');

  // 分页派生
  readonly currentPage = computed(() => Math.floor(this.first() / this.rows()) + 1);
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalRecords() / this.rows())));
  readonly canPrev = computed(() => this.first() > 0);
  readonly canNext = computed(() => this.currentPage() < this.totalPages());

  //#if (IncludeLocalization)
  rolePopoverTitle = computed(() =>
    this.transloco.translate('users.popover.rolesTitle', { count: this.activeRoles().length }),
  );
  //#else
  rolePopoverTitle = computed(() => `Roles (${this.activeRoles().length})`);
  //#endif

  private emitFilter() {
    this.filterChange.emit({
      offset: this.first(),
      limit: this.rows(),
      sorting: `${this.sortField()} ${this.sortOrder() === 1 ? 'asc' : 'desc'}`,
    });
  }

  /** 点击可排序列头：同列切换升/降序，异列切到该列升序。 */
  onSort(field: string) {
    if (this.sortField() === field) {
      this.sortOrder.set(this.sortOrder() === 1 ? -1 : 1);
    } else {
      this.sortField.set(field);
      this.sortOrder.set(1);
    }
    this.first.set(0);
    this.emitFilter();
  }

  /** 排序图标名（当前列升/降，其它列中性）。 */
  sortIcon(field: string): string {
    if (this.sortField() !== field) {
      return 'lucideArrowUpDown';
    }
    return this.sortOrder() === 1 ? 'lucideSortAsc' : 'lucideSortDesc';
  }

  firstPage() {
    if (!this.canPrev()) return;
    this.first.set(0);
    this.emitFilter();
  }

  prevPage() {
    if (!this.canPrev()) return;
    this.first.set(Math.max(0, this.first() - this.rows()));
    this.emitFilter();
  }

  nextPage() {
    if (!this.canNext()) return;
    this.first.set(this.first() + this.rows());
    this.emitFilter();
  }

  lastPage() {
    if (!this.canNext()) return;
    this.first.set((this.totalPages() - 1) * this.rows());
    this.emitFilter();
  }

  onRowsChange(value: number | null | undefined) {
    if (value == null) return;
    this.rows.set(value);
    this.first.set(0);
    this.emitFilter();
  }

  openRolesPopover(roles: string[]) {
    this.activeRoles.set(roles);
    this.rolePopoverOpen.set('open');
  }

  getVisibleRoles(user: UserManagementOutputDto) {
    return user.roles.slice(0, 2);
  }

  getHiddenRoles(user: UserManagementOutputDto) {
    return user.roles.slice(2);
  }

  isSuperAdmin(user: UserManagementOutputDto) {
    return user.isSuperAdmin;
  }

  isOtherSuperAdmin(user: UserManagementOutputDto) {
    return user.isSuperAdmin && user.id !== this.authService.currentUser()?.id;
  }

  isSelfSuperAdmin(user: UserManagementOutputDto) {
    return user.isSuperAdmin && user.id === this.authService.currentUser()?.id;
  }

  getRoleVariant(role: string): BadgeVariant {
    // 角色用中性强弱层级（primary→secondary→outline）；红色（destructive）专留给危险/删除操作。
    const roleMap: Record<Role, BadgeVariant> = {
      [Role.Admin]: 'default',
      [Role.Operator]: 'secondary',
      [Role.Member]: 'outline',
    };
    return roleMap[role as Role] ?? 'outline';
  }

  //#if (IncludeLocalization)
  // 本地化模式：getRoleLabel 返回词条键，这里翻译为当前语言的显示文案。
  getRoleLabel(role: string): string {
    const key = getRoleLabel(role);
    return this.transloco.translate(key);
  }
  //#else
  getRoleLabel = getRoleLabel;
  //#endif
}
