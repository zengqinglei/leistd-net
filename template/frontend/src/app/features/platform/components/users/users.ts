import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Params, Router } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoDirective, TranslocoService, translateSignal } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBan,
  lucideBriefcase,
  lucideCircleCheck,
  lucideMail,
  lucideMailCheck,
  lucidePlus,
  lucideRefreshCw,
  lucideSearch,
  lucideShieldCheck,
  lucideUser,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import {
  HlmInputGroup,
  HlmInputGroupInput,
  HlmInputGroupAddon,
} from '@spartan-ng/helm/input-group';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { PaginationState, SortingState } from '@tanstack/angular-table';
import { combineLatest, defer, EMPTY, Subject } from 'rxjs';
import {
  catchError,
  debounceTime,
  distinctUntilChanged,
  finalize,
  startWith,
  switchMap,
} from 'rxjs/operators';

import { applicationErrorMessage } from '../../../../core/errors/application-http-error';
import { ConfirmService } from '../../../../core/feedback/confirm-service';
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { LayoutService } from '../../../../core/services/layout-service';
import { FacetedFilter } from '../../../../shared/components/faceted-filter/faceted-filter';
import { PERMISSIONS } from '../../../../shared/constants/permission.constants';
//#if (!IncludeLocalization)
import { englishText } from '../../../../shared/utils/english-text';
//#endif
import {
  paginationFromQuery,
  sortingFromQuery,
  tableStateToQuery,
  toApiSorting,
} from '../../../../shared/utils/table-query-state';
import { RoleBriefDto } from '../../dtos/role.dto';
// 裁掉这几个 DTO 之后剩余项能并成一行，而 prettier 会要求那样写；
// 条件块不能随形态换折行方式，因此在这里固定住
// prettier-ignore
import {
  //#if (LocalIdentity)
  CreateUserInputDto,
  //#endif
  GetUsersInputDto,
  //#if (LocalIdentity)
  ResetUserPasswordInputDto,
  //#endif
  //#if (LocalIdentity)
  UpdateUserInputDto,
  //#endif
  UserManagementOutputDto,
} from '../../dtos/user-management.dto';
import { RoleService } from '../../services/role-service';
import { UserManagementService } from '../../services/user-management-service';
//#if (LocalIdentity)
import { ResetUserPasswordDialog } from './widgets/reset-user-password-dialog/reset-user-password-dialog';
//#endif
//#if (LocalIdentity)
import { UserEditDialog } from './widgets/user-edit-dialog/user-edit-dialog';
//#endif
import { UserRolesDialog } from './widgets/user-roles-dialog/user-roles-dialog';
import { UserTable } from './widgets/user-table/user-table';

//#if (LocalIdentity)
const USER_SORT_COLUMNS = ['username', 'email', 'lastLoginTime', 'creationTime'] as const;
//#else
const USER_SORT_COLUMNS = ['username', 'email', 'creationTime'] as const;
//#endif
const DEFAULT_USER_SORTING: SortingState = [{ id: 'creationTime', desc: true }];

@Component({
  selector: 'app-users',
  imports: [
    NgIcon,
    HlmButton,
    HlmInputGroup,
    HlmInputGroupInput,
    HlmInputGroupAddon,
    ...HlmTooltipImports,
    FacetedFilter,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
    UserTable,
    UserRolesDialog,
    //#if (LocalIdentity)
    UserEditDialog,
    //#endif
    //#if (LocalIdentity)
    ResetUserPasswordDialog,
    //#endif
  ],
  providers: [
    provideIcons({
      lucidePlus,
      lucideRefreshCw,
      lucideSearch,
      lucideCircleCheck,
      lucideBan,
      lucideMailCheck,
      lucideMail,
      lucideShieldCheck,
      lucideBriefcase,
      lucideUser,
    }),
  ],
  templateUrl: './users.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Users {
  private readonly service = inject(UserManagementService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly confirmService = inject(ConfirmService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly layoutService = inject(LayoutService);
  private readonly roleService = inject(RoleService);
  private readonly authorizationService = inject(AuthorizationService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#else
  protected readonly t = englishText(ENGLISH);
  //#endif

  private readonly searchSubject = new Subject<string>();
  private readonly refreshRequests = new Subject<void>();
  private readonly queryParams = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });

  users = signal<UserManagementOutputDto[]>([]);
  totalRecords = signal(0);
  loading = signal(false);
  /** 列表加载失败且没有旧行可保留时的原因：有值时表格显示错误态与重试，不显示"暂无数据"。 */
  loadError = signal<string | null>(null);

  // 列表状态全部来源于 URL 查询参数（刷新 / 前进后退 / 分享皆可复原）。
  readonly pagination = computed(() => paginationFromQuery(this.queryParams()));
  readonly sorting = computed(() =>
    sortingFromQuery(this.queryParams(), USER_SORT_COLUMNS, DEFAULT_USER_SORTING),
  );

  editDialogVisible = signal(false);
  editDialogLoading = signal(false);
  editDialogSaving = signal(false);
  selectedUser = signal<UserManagementOutputDto | null>(null);
  //#if (LocalIdentity)
  resetPasswordDialogVisible = signal(false);
  resetPasswordSaving = signal(false);
  resettingUserId = signal<string | null>(null);
  //#endif

  // 搜索框即时值：随 URL 回填，输入时乐观更新，防抖后写回 URL。
  readonly searchQuery = signal(this.route.snapshot.queryParamMap.get('keyword') ?? '');
  // FacetedFilter 的取值由 URL 状态驱动。
  readonly selectedIsActive = computed(() => readBoolean(this.queryParams().get('isActive')));
  //#if (LocalIdentity)
  readonly selectedIsEmailVerified = computed(() =>
    readBoolean(this.queryParams().get('isEmailVerified')),
  );
  //#endif
  readonly selectedRoles = computed(() => this.queryParams().getAll('roles'));
  // 是否处于筛选/搜索态：用于区分「暂无数据」与「无匹配结果」的空状态。
  readonly hasActiveFilters = computed(() => {
    const commonFilters = this.searchQuery().trim().length > 0 || this.selectedIsActive() !== null;
    const authorizationFilters = this.selectedRoles().length > 0;
    //#if (LocalIdentity)
    return commonFilters || authorizationFilters || this.selectedIsEmailVerified() !== null;
    //#else
    return commonFilters || authorizationFilters;
    //#endif
  });
  //#if (IncludeLocalization)
  private readonly activeOptionsTexts = {
    active: translateSignal('users.status.active', {}, { scope: 'users' }),
    inactive: translateSignal('users.status.inactive', {}, { scope: 'users' }),
  };
  readonly activeOptions = computed(() => [
    {
      label: this.activeOptionsTexts.active(),
      value: true,
      icon: 'lucideCircleCheck',
    },
    { label: this.activeOptionsTexts.inactive(), value: false, icon: 'lucideBan' },
  ]);

  //#if (LocalIdentity)
  private readonly emailVerifiedOptionsTexts = {
    emailVerified: translateSignal('users.status.emailVerified', {}, { scope: 'users' }),
    emailUnverified: translateSignal('users.status.emailUnverified', {}, { scope: 'users' }),
  };
  readonly emailVerifiedOptions = computed(() => [
    {
      label: this.emailVerifiedOptionsTexts.emailVerified(),
      value: true,
      icon: 'lucideMailCheck',
    },
    {
      label: this.emailVerifiedOptionsTexts.emailUnverified(),
      value: false,
      icon: 'lucideMail',
    },
  ]);
  //#endif
  //#else
  readonly activeOptions = computed(() => [
    { label: 'Active', value: true, icon: 'lucideCircleCheck' },
    { label: 'Disabled', value: false, icon: 'lucideBan' },
  ]);

  //#if (LocalIdentity)
  readonly emailVerifiedOptions = computed(() => [
    { label: 'Email verified', value: true, icon: 'lucideMailCheck' },
    { label: 'Email not verified', value: false, icon: 'lucideMail' },
  ]);
  //#endif
  //#endif
  /** 角色筛选项来自角色 API，新建的角色立即出现在筛选器里。 */
  readonly availableRoles = signal<RoleBriefDto[]>([]);
  readonly roleOptions = computed(() =>
    this.availableRoles().map((role) => ({
      label: role.displayName,
      value: role.name,
      icon: 'lucideUser',
    })),
  );
  // 操作入口按权限裁剪。
  readonly canCreateUser = computed(() => this.authorizationService.has(PERMISSIONS.users.create));
  readonly canUpdateUser = computed(() => this.authorizationService.has(PERMISSIONS.users.update));
  readonly canDeleteUser = computed(() => this.authorizationService.has(PERMISSIONS.users.delete));
  readonly canManageUserRoles = computed(() =>
    this.authorizationService.has(PERMISSIONS.users.manageRoles),
  );
  readonly rolesDialogVisible = signal(false);
  readonly rolesDialogUser = signal<UserManagementOutputDto | null>(null);

  openRolesDialog(user: UserManagementOutputDto): void {
    this.rolesDialogUser.set(user);
    this.rolesDialogVisible.set(true);
  }

  /** 角色变更会改变有效权限，保存后刷新列表与当前用户权限。 */
  onRolesSaved(): void {
    this.rolesDialogVisible.set(false);
    this.refreshRequests.next();
    this.authorizationService.reload().subscribe({ error: () => undefined });
  }

  constructor() {
    // 角色选项端点要求 ManageRoles；无该权限时不请求，避免制造必然 403 的噪声。
    if (this.authorizationService.has(PERMISSIONS.users.manageRoles)) {
      this.roleService.getOptions().subscribe({
        next: (roles) => this.availableRoles.set(roles),
        error: () => this.availableRoles.set([]),
      });
    }

    this.searchSubject
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe((keyword) => this.updateQuery({ keyword: keyword.trim() || null, page: 1 }, true));

    // 搜索框即时值随 URL 回填（前进后退 / 分享链接场景）。
    effect(() => this.searchQuery.set(this.queryParams().get('keyword') ?? ''));

    // URL 变化或显式刷新时重新拉取列表。
    combineLatest([this.route.queryParamMap, this.refreshRequests.pipe(startWith(undefined))])
      .pipe(
        // 开始状态放进当前请求的订阅里：switchMap 先取消旧请求（其 finalize 置 false）再订阅新请求，
        // 写在 switchMap 外的话，旧请求的 finalize 会在新请求还没返回时把 loading 关掉。
        switchMap(([params]) =>
          defer(() => {
            this.loading.set(true);
            return this.service.getUsers(this.queryFromParams(params)).pipe(
              catchError((error: unknown) => {
                // 已有行时刷新失败：保留旧行，只做提示
                if (this.users().length > 0) {
                  this.showRequestError(error);
                } else {
                  this.loadError.set(applicationErrorMessage(error));
                }
                return EMPTY;
              }),
              finalize(() => this.loading.set(false)),
            );
          }),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((data) => {
        this.loadError.set(null);
        this.users.set(data.items);
        this.totalRecords.set(data.totalCount);
      });
    //#if (IncludeLocalization)

    const title = translateSignal('users.page.title', {}, { scope: 'users' });
    effect(() => this.layoutService.title.set(title()));
    //#else

    this.layoutService.title.set('User management');
    //#endif
  }

  onSearchQueryChange(value: string) {
    this.searchQuery.set(value);
    this.searchSubject.next(value);
  }

  onActiveChange(value: boolean | null | undefined) {
    this.updateQuery({ isActive: serializeBoolean(value), page: 1 });
  }

  //#if (LocalIdentity)
  onEmailVerifiedChange(value: boolean | null | undefined) {
    this.updateQuery({ isEmailVerified: serializeBoolean(value), page: 1 });
  }
  //#endif
  onRolesChange(values: string[]) {
    this.updateQuery({ roles: values.length ? values : null, page: 1 });
  }

  onPaginationChange(pagination: PaginationState) {
    this.updateQuery(tableStateToQuery(pagination, this.sorting()));
  }

  onSortingChange(sorting: SortingState) {
    this.updateQuery({
      ...tableStateToQuery({ ...this.pagination(), pageIndex: 0 }, sorting),
      page: 1,
    });
  }

  reloadList() {
    this.refreshRequests.next();
  }

  //#if (LocalIdentity)
  openAddDialog() {
    this.selectedUser.set(null);
    this.editDialogVisible.set(true);
  }

  //#endif
  //#if (LocalIdentity)
  openEditDialog(id: string) {
    this.selectedUser.set(null);
    this.editDialogVisible.set(true);
    this.editDialogLoading.set(true);

    this.service
      .getUser(id)
      .pipe(
        finalize(() => this.editDialogLoading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (user) => this.selectedUser.set(user),
        error: (error) => this.showRequestError(error),
      });
  }

  //#endif
  //#if (LocalIdentity)
  handleSave(data: CreateUserInputDto | UpdateUserInputDto) {
    this.editDialogSaving.set(true);
    const selected = this.selectedUser();
    const request = selected
      ? this.service.updateUser(selected.id, data as UpdateUserInputDto)
      : this.service.createUser(data as CreateUserInputDto);

    request
      .pipe(
        finalize(() => this.editDialogSaving.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          //#if (IncludeLocalization)
          toast.success(this.transloco.translate('common.success'), {
            description: this.transloco.translate(
              selected ? 'users.toast.updated' : 'users.toast.created',
            ),
          });
          //#else
          toast.success('Success', {
            description: selected ? 'User updated successfully' : 'User created successfully',
          });
          //#endif
          this.editDialogVisible.set(false);
          this.reloadList();
        },
        error: (error) => this.showRequestError(error),
      });
  }

  //#endif
  async handleToggleActive(user: UserManagementOutputDto) {
    const confirmed = await this.confirmService.open({
      //#if (IncludeLocalization)
      message: this.transloco.translate(
        user.isActive ? 'users.confirm.disableMessage' : 'users.confirm.enableMessage',
        { name: user.username },
      ),
      header: this.transloco.translate(
        user.isActive ? 'users.confirm.disableHeader' : 'users.confirm.enableHeader',
      ),
      confirmText: this.transloco.translate('common.ok'),
      cancelText: this.transloco.translate('common.cancel'),
      //#else
      message: user.isActive
        ? `Are you sure you want to disable user ${user.username}?`
        : `Are you sure you want to enable user ${user.username}?`,
      header: user.isActive ? 'Confirm disable' : 'Confirm enable',
      //#endif
    });
    if (!confirmed) {
      return;
    }

    const request = user.isActive
      ? this.service.disableUser(user.id)
      : this.service.enableUser(user.id);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        //#if (IncludeLocalization)
        toast.success(this.transloco.translate('common.success'), {
          description: this.transloco.translate(
            user.isActive ? 'users.toast.disabled' : 'users.toast.enabled',
          ),
        });
        //#else
        toast.success('Success', {
          description: user.isActive ? 'User disabled' : 'User enabled',
        });
        //#endif
        this.reloadList();
      },
      error: (error) => this.showRequestError(error),
    });
  }

  //#if (LocalIdentity)
  async handleDelete(user: UserManagementOutputDto) {
    if (user.isSuperAdmin) {
      //#if (IncludeLocalization)
      toast.warning(this.transloco.translate('users.toast.cannotDeleteSummary'), {
        description: this.transloco.translate('users.toast.cannotDeleteSuperAdmin'),
      });
      //#else
      toast.warning('Cannot delete', {
        description: 'The built-in super administrator cannot be deleted',
      });
      //#endif
      return;
    }

    const confirmed = await this.confirmService.open({
      //#if (IncludeLocalization)
      message: this.transloco.translate('users.confirm.deleteMessage', { name: user.username }),
      header: this.transloco.translate('users.confirm.deleteHeader'),
      confirmText: this.transloco.translate('common.ok'),
      cancelText: this.transloco.translate('common.cancel'),
      //#else
      message: `Are you sure you want to delete user ${user.username}? Once deleted, the user will no longer be able to sign in.`,
      header: 'Confirm delete',
      //#endif
      variant: 'destructive',
    });
    if (!confirmed) {
      return;
    }

    this.service
      .deleteUser(user.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          //#if (IncludeLocalization)
          toast.success(this.transloco.translate('common.success'), {
            description: this.transloco.translate('users.toast.deleted'),
          });
          //#else
          toast.success('Success', { description: 'User deleted' });
          //#endif
          this.reloadList();
        },
        error: (error) => this.showRequestError(error),
      });
  }
  //#endif
  //#if (LocalIdentity)
  async handleUnlock(user: UserManagementOutputDto) {
    const confirmed = await this.confirmService.open({
      //#if (IncludeLocalization)
      message: this.transloco.translate('users.confirm.unlockMessage', { name: user.username }),
      header: this.transloco.translate('users.confirm.unlockHeader'),
      confirmText: this.transloco.translate('common.ok'),
      cancelText: this.transloco.translate('common.cancel'),
      //#else
      message: `Unlock user ${user.username}? The failed sign-in count will also be reset.`,
      header: 'Confirm unlock',
      //#endif
    });
    if (!confirmed) {
      return;
    }

    this.service
      .unlockUser(user.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          //#if (IncludeLocalization)
          toast.success(this.transloco.translate('common.success'), {
            description: this.transloco.translate('users.toast.unlocked'),
          });
          //#else
          toast.success('Success', { description: 'User unlocked' });
          //#endif
          this.reloadList();
        },
        error: (error) => this.showRequestError(error),
      });
  }

  async handleResetTwoFactor(user: UserManagementOutputDto) {
    const confirmed = await this.confirmService.open({
      //#if (IncludeLocalization)
      message: this.transloco.translate('users.confirm.resetTwoFactorMessage', {
        name: user.username,
      }),
      header: this.transloco.translate('users.confirm.resetTwoFactorHeader'),
      confirmText: this.transloco.translate('common.ok'),
      cancelText: this.transloco.translate('common.cancel'),
      //#else
      message: `Reset two-factor authentication for ${user.username}? They will be signed out everywhere and can sign in with just their password.`,
      header: 'Confirm reset',
      //#endif
      variant: 'destructive',
    });
    if (!confirmed) {
      return;
    }

    this.service
      .resetTwoFactor(user.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          //#if (IncludeLocalization)
          toast.success(this.transloco.translate('common.success'), {
            description: this.transloco.translate('users.toast.twoFactorReset'),
          });
          //#else
          toast.success('Success', { description: 'Two-factor authentication reset' });
          //#endif
          this.reloadList();
        },
        error: (error) => this.showRequestError(error),
      });
  }

  openResetPasswordDialog(id: string) {
    this.resettingUserId.set(id);
    this.resetPasswordDialogVisible.set(true);
  }

  handleResetPassword(data: ResetUserPasswordInputDto) {
    const id = this.resettingUserId();
    if (!id) {
      return;
    }

    this.resetPasswordSaving.set(true);
    this.service
      .resetPassword(id, data)
      .pipe(
        finalize(() => this.resetPasswordSaving.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          //#if (IncludeLocalization)
          toast.success(this.transloco.translate('common.success'), {
            description: this.transloco.translate('users.toast.passwordReset'),
          });
          //#else
          toast.success('Success', { description: 'Password has been reset' });
          //#endif
          this.resetPasswordDialogVisible.set(false);
          this.resettingUserId.set(null);
        },
        error: (error) => this.showRequestError(error),
      });
  }

  //#endif
  private queryFromParams(params: ParamMap): GetUsersInputDto {
    const pagination = paginationFromQuery(params);
    const roles = params.getAll('roles');
    return {
      offset: pagination.pageIndex * pagination.pageSize,
      limit: pagination.pageSize,
      keyword: params.get('keyword') || undefined,
      isActive: readBoolean(params.get('isActive')) ?? undefined,
      //#if (LocalIdentity)
      isEmailVerified: readBoolean(params.get('isEmailVerified')) ?? undefined,
      //#endif
      roles: roles.length ? roles : undefined,
      // prettier-ignore
      sorting: toApiSorting(
        sortingFromQuery(params, USER_SORT_COLUMNS, DEFAULT_USER_SORTING)
          //#if (LocalIdentity)
          .map((item) => ({ ...item, id: item.id === 'lastLoginTime' ? 'lastLogin.Time' : item.id }))
          //#endif
      ),
    };
  }

  private updateQuery(queryParams: Params, replaceUrl = false): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      queryParamsHandling: 'merge',
      replaceUrl,
    });
  }

  private showRequestError(error: unknown): void {
    //#if (IncludeLocalization)
    toast.error(this.transloco.translate('common.requestError'), {
      description: applicationErrorMessage(error),
    });
    //#else
    toast.error('Request failed', { description: applicationErrorMessage(error) });
    //#endif
  }
}

function readBoolean(value: string | null): boolean | null {
  return value === 'true' ? true : value === 'false' ? false : null;
}

function serializeBoolean(value: boolean | null | undefined): string | null {
  return value === true ? 'true' : value === false ? 'false' : null;
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'users.filter.searchPlaceholder': 'Search username / email / display name...',
  'users.table.colStatus': 'Status',
  'common.clearFilter': 'Clear filter',
  'common.noResults': 'No results',
  'users.filter.emailLabel': 'Email status',
  'users.table.colRole': 'Role',
  'common.refresh': 'Refresh',
  'users.actions.newUser': 'New user',
};
//#endif
