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
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus, lucideRefreshCw, lucideSearch } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import {
  HlmInputGroup,
  HlmInputGroupInput,
  HlmInputGroupAddon,
} from '@spartan-ng/helm/input-group';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { PaginationState, SortingState } from '@tanstack/angular-table';
import { combineLatest, EMPTY, Subject } from 'rxjs';
import {
  catchError,
  debounceTime,
  distinctUntilChanged,
  finalize,
  startWith,
  switchMap,
  tap,
} from 'rxjs/operators';

import { applicationErrorMessage } from '../../../../core/errors/application-http-error';
import { ConfirmService } from '../../../../core/feedback/confirm-service';
//#if (IncludeLocalization)
import { translationReady } from '../../../../core/i18n/translation-ready';
//#endif
import { LayoutService } from '../../../../layout/services/layout-service';
import { FacetedFilter } from '../../../../shared/components/faceted-filter/faceted-filter';
import { ROLE_LABEL_MAP } from '../../../../shared/models/role.enum';
import {
  paginationFromQuery,
  sortingFromQuery,
  tableStateToQuery,
  toApiSorting,
} from '../../../../shared/utils/table-query-state';
import {
  CreateUserInputDto,
  GetUsersInputDto,
  ResetUserPasswordInputDto,
  UpdateUserInputDto,
  UserManagementOutputDto,
} from '../../models/user-management.dto';
import { UserManagementService } from '../../services/user-management-service';
import { ResetUserPasswordDialog } from './widgets/reset-user-password-dialog/reset-user-password-dialog';
import { UserEditDialog } from './widgets/user-edit-dialog/user-edit-dialog';
import { UserTable } from './widgets/user-table/user-table';

const USER_SORT_COLUMNS = ['username', 'email', 'lastLoginTime', 'creationTime'] as const;
const DEFAULT_USER_SORTING: SortingState = [{ id: 'username', desc: false }];

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
    TranslocoModule,
    //#endif
    UserTable,
    UserEditDialog,
    ResetUserPasswordDialog,
  ],
  providers: [provideIcons({ lucidePlus, lucideRefreshCw, lucideSearch })],
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
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif

  private readonly searchSubject = new Subject<string>();
  private readonly refreshRequests = new Subject<void>();
  private readonly queryParams = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });

  users = signal<UserManagementOutputDto[]>([]);
  totalRecords = signal(0);
  loading = signal(false);

  // 列表状态全部来源于 URL 查询参数（刷新 / 前进后退 / 分享皆可复原）。
  readonly pagination = computed(() => paginationFromQuery(this.queryParams()));
  readonly sorting = computed(() =>
    sortingFromQuery(this.queryParams(), USER_SORT_COLUMNS, DEFAULT_USER_SORTING),
  );

  editDialogVisible = signal(false);
  editDialogLoading = signal(false);
  editDialogSaving = signal(false);
  selectedUser = signal<UserManagementOutputDto | null>(null);

  resetPasswordDialogVisible = signal(false);
  resetPasswordSaving = signal(false);
  resettingUserId = signal<string | null>(null);

  // 搜索框即时值：随 URL 回填，输入时乐观更新，防抖后写回 URL。
  readonly searchQuery = signal(this.route.snapshot.queryParamMap.get('keyword') ?? '');
  // FacetedFilter 的取值由 URL 状态驱动。
  readonly selectedIsActive = computed(() => readBoolean(this.queryParams().get('isActive')));
  readonly selectedIsEmailVerified = computed(() =>
    readBoolean(this.queryParams().get('isEmailVerified')),
  );
  readonly selectedRoles = computed(() => this.queryParams().getAll('roles'));

  //#if (IncludeLocalization)
  // 追踪「翻译就绪」：资源加载完成与语言切换时重算，含首帧避免裸键。
  private readonly translationReady = translationReady(this.transloco);

  // 读 translationReady 建立依赖：资源就绪 / 语言切换时重算并重新翻译。
  readonly activeOptions = computed(() => {
    this.translationReady();
    return [
      { label: this.transloco.translate('users.status.active'), value: true },
      { label: this.transloco.translate('users.status.inactive'), value: false },
    ];
  });

  readonly emailVerifiedOptions = computed(() => {
    this.translationReady();
    return [
      { label: this.transloco.translate('users.status.emailVerified'), value: true },
      { label: this.transloco.translate('users.status.emailUnverified'), value: false },
    ];
  });

  // 本地化模式：ROLE_LABEL_MAP 值是词条键，翻译为显示文案。
  readonly roleOptions = computed(() => {
    this.translationReady();
    return Object.entries(ROLE_LABEL_MAP).map(([value, label]) => ({
      label: this.transloco.translate(label),
      value,
    }));
  });
  //#else
  readonly activeOptions = computed(() => [
    { label: 'Active', value: true },
    { label: 'Disabled', value: false },
  ]);

  readonly emailVerifiedOptions = computed(() => [
    { label: 'Email verified', value: true },
    { label: 'Email not verified', value: false },
  ]);

  readonly roleOptions = computed(() =>
    Object.entries(ROLE_LABEL_MAP).map(([value, label]) => ({ label, value })),
  );
  //#endif

  //#if (IncludeLocalization)
  readonly allStatusPlaceholder = () => this.transloco.translate('users.filter.allStatus');
  readonly allEmailStatusPlaceholder = () =>
    this.transloco.translate('users.filter.allEmailStatus');
  readonly allRolesPlaceholder = () => this.transloco.translate('users.filter.allRoles');
  readonly searchPlaceholder = () => this.transloco.translate('users.filter.searchPlaceholder');
  readonly refreshLabel = () => this.transloco.translate('common.refresh');
  readonly newUserLabel = () => this.transloco.translate('users.actions.newUser');
  readonly statusFilterLabel = () => this.transloco.translate('users.table.colStatus');
  readonly emailFilterLabel = () => this.transloco.translate('users.filter.emailLabel');
  readonly roleFilterLabel = () => this.transloco.translate('users.table.colRole');
  readonly filterClearLabel = () => this.transloco.translate('common.clearFilter');
  readonly filterEmptyLabel = () => this.transloco.translate('common.noResults');
  //#else
  readonly allStatusPlaceholder = () => 'All statuses';
  readonly allEmailStatusPlaceholder = () => 'All email statuses';
  readonly allRolesPlaceholder = () => 'All roles';
  readonly searchPlaceholder = () => 'Search username / email / display name...';
  readonly refreshLabel = () => 'Refresh';
  readonly newUserLabel = () => 'New user';
  readonly statusFilterLabel = () => 'Status';
  readonly emailFilterLabel = () => 'Email status';
  readonly roleFilterLabel = () => 'Role';
  readonly filterClearLabel = () => 'Clear filter';
  readonly filterEmptyLabel = () => 'No results';
  //#endif

  constructor() {
    this.searchSubject
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe((keyword) => this.updateQuery({ keyword: keyword.trim() || null, page: 1 }, true));

    // 搜索框即时值随 URL 回填（前进后退 / 分享链接场景）。
    effect(() => this.searchQuery.set(this.queryParams().get('keyword') ?? ''));

    // URL 变化或显式刷新时重新拉取列表。
    combineLatest([this.route.queryParamMap, this.refreshRequests.pipe(startWith(undefined))])
      .pipe(
        tap(() => this.loading.set(true)),
        switchMap(([params]) =>
          this.service.getUsers(this.queryFromParams(params)).pipe(
            catchError((error: unknown) => {
              this.showRequestError(error);
              return EMPTY;
            }),
            finalize(() => this.loading.set(false)),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((data) => {
        this.users.set(data.items);
        this.totalRecords.set(data.totalCount);
      });

    //#if (IncludeLocalization)
    // 读 translationReady 建立依赖：资源就绪 / 语言切换时标题随之重设。
    effect(() => {
      this.translationReady();
      this.layoutService.title.set(this.transloco.translate('users.page.title'));
    });
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

  onEmailVerifiedChange(value: boolean | null | undefined) {
    this.updateQuery({ isEmailVerified: serializeBoolean(value), page: 1 });
  }

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

  openAddDialog() {
    this.selectedUser.set(null);
    this.editDialogVisible.set(true);
  }

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

  private queryFromParams(params: ParamMap): GetUsersInputDto {
    const pagination = paginationFromQuery(params);
    const roles = params.getAll('roles');
    return {
      offset: pagination.pageIndex * pagination.pageSize,
      limit: pagination.pageSize,
      keyword: params.get('keyword') || undefined,
      isActive: readBoolean(params.get('isActive')) ?? undefined,
      isEmailVerified: readBoolean(params.get('isEmailVerified')) ?? undefined,
      roles: roles.length ? roles : undefined,
      sorting: toApiSorting(sortingFromQuery(params, USER_SORT_COLUMNS, DEFAULT_USER_SORTING)),
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
