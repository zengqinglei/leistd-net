import { CommonModule } from '@angular/common';
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
import { AvatarModule } from 'primeng/avatar';
import { ButtonModule } from 'primeng/button';
import { Popover, PopoverModule } from 'primeng/popover';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';

import { AuthService } from '../../../../../../core/services/auth-service';
import { Role } from '../../../../../../shared/models/role.enum';
import { getRoleLabel } from '../../../../../../shared/pipes/role-label.pipe';
import { UserManagementOutputDto } from '../../../../models/user-management.dto';

export interface UserTableFilterEvent {
  offset: number;
  limit: number;
  sorting?: string;
}

@Component({
  selector: 'app-user-table',
  //#if (IncludeLocalization)
  imports: [
    CommonModule,
    TableModule,
    ButtonModule,
    TagModule,
    TooltipModule,
    PopoverModule,
    AvatarModule,
    TranslocoModule,
  ],
  //#else
  imports: [
    CommonModule,
    TableModule,
    ButtonModule,
    TagModule,
    TooltipModule,
    PopoverModule,
    AvatarModule,
  ],
  //#endif
  templateUrl: './user-table.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserTable {
  private readonly authService = inject(AuthService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);

  readonly currentPageReport = () => this.transloco.translate('users.table.currentPageReport');

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
  //#else
  readonly currentPageReport = () => '{totalRecords} in total';

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
  //#endif

  users = input.required<UserManagementOutputDto[]>();
  totalRecords = input.required<number>();
  loading = input<boolean>(false);

  readonly edit = output<string>();
  readonly toggleActive = output<UserManagementOutputDto>();
  readonly resetPassword = output<string>();
  readonly delete = output<UserManagementOutputDto>();
  readonly filterChange = output<UserTableFilterEvent>();

  first = 0;
  rows = 20;
  sortField = signal('username');
  sortOrder = signal(1);
  activeRoles = signal<string[]>([]);
  //#if (IncludeLocalization)
  rolePopoverTitle = computed(() =>
    this.transloco.translate('users.popover.rolesTitle', { count: this.activeRoles().length }),
  );
  //#else
  rolePopoverTitle = computed(() => `Roles (${this.activeRoles().length})`);
  //#endif

  onPage(event: TableLazyLoadEvent) {
    this.first = event.first ?? 0;
    this.rows = event.rows ?? 20;
    if (event.sortField) {
      this.sortField.set(Array.isArray(event.sortField) ? event.sortField[0] : event.sortField);
      this.sortOrder.set(event.sortOrder ?? 1);
    }
    this.filterChange.emit({
      offset: this.first,
      limit: this.rows,
      sorting: `${this.sortField()} ${this.sortOrder() === 1 ? 'asc' : 'desc'}`,
    });
  }

  openRolesPopover(event: Event, popover: Popover, roles: string[]) {
    this.activeRoles.set(roles);
    popover.toggle(event);
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

  getRoleSeverity(role: string): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
    const roleMap: Record<Role, 'success' | 'info' | 'warn' | 'danger' | 'secondary'> = {
      [Role.Admin]: 'danger',
      [Role.Operator]: 'warn',
      [Role.Member]: 'info',
    };
    return roleMap[role as Role] ?? 'secondary';
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
