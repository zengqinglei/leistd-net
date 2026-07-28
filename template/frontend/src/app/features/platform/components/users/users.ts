//#if (IncludeLocalization)
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
//#else
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
//#endif
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus, lucideRefreshCw, lucideSearch } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import {
  HlmInputGroup,
  HlmInputGroupInput,
  HlmInputGroupAddon,
} from '@spartan-ng/helm/input-group';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { Subject } from 'rxjs';
import { debounceTime, distinctUntilChanged, finalize } from 'rxjs/operators';

//#if (IncludeLocalization)
import { translationReady } from '../../../../core/i18n/translation-ready';
//#endif
import { ConfirmService } from '../../../../core/notifications/confirm-service';
import { notify } from '../../../../core/notifications/notify';
import { LayoutService } from '../../../../layout/services/layout-service';
import { ROLE_LABEL_MAP } from '../../../../shared/models/role.enum';
import { FilterStateService } from '../../../../shared/services/filter-state-service';
import {
  CreateUserInputDto,
  ResetUserPasswordInputDto,
  UpdateUserInputDto,
  UserManagementOutputDto,
} from '../../models/user-management.dto';
import { UserManagementService } from '../../services/user-management-service';
import { ResetUserPasswordDialog } from './widgets/reset-user-password-dialog/reset-user-password-dialog';
import { UserEditDialog } from './widgets/user-edit-dialog/user-edit-dialog';
import { UserTable, UserTableFilterEvent } from './widgets/user-table/user-table';

@Component({
  selector: 'app-users',
  imports: [
    FormsModule,
    NgIcon,
    HlmButton,
    HlmInputGroup,
    HlmInputGroupInput,
    HlmInputGroupAddon,
    ...HlmSelectImports,
    ...HlmTooltipImports,
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
export class Users implements OnInit {
  private readonly service = inject(UserManagementService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly confirmService = inject(ConfirmService);
  private readonly layoutService = inject(LayoutService);
  private readonly filterStateService = inject(FilterStateService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif

  private readonly FILTER_KEY = 'users';
  private readonly searchSubject = new Subject<string>();

  users = signal<UserManagementOutputDto[]>([]);
  totalRecords = signal(0);
  loading = signal(false);

  editDialogVisible = signal(false);
  editDialogLoading = signal(false);
  editDialogSaving = signal(false);
  selectedUser = signal<UserManagementOutputDto | null>(null);

  resetPasswordDialogVisible = signal(false);
  resetPasswordSaving = signal(false);
  resettingUserId = signal<string | null>(null);

  searchQuery = signal('');
  selectedIsActive = signal<boolean | null>(null);
  selectedIsEmailVerified = signal<boolean | null>(null);
  selectedRole = signal<string | null>(null);

  offset = signal(0);
  limit = signal(10);
  sorting = signal('username asc');

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
  //#else
  readonly allStatusPlaceholder = () => 'All statuses';
  readonly allEmailStatusPlaceholder = () => 'All email statuses';
  readonly allRolesPlaceholder = () => 'All roles';
  readonly searchPlaceholder = () => 'Search username / email / display name...';
  readonly refreshLabel = () => 'Refresh';
  readonly newUserLabel = () => 'New user';
  //#endif

  constructor() {
    this.searchSubject
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.onFilter());

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

  ngOnInit() {
    const saved = this.filterStateService.load<{
      searchQuery: string;
      selectedIsActive: boolean | null;
      selectedIsEmailVerified: boolean | null;
      selectedRole: string | null;
    }>(this.FILTER_KEY);

    if (saved.searchQuery) this.searchQuery.set(saved.searchQuery);
    if (saved.selectedIsActive !== undefined)
      this.selectedIsActive.set(saved.selectedIsActive ?? null);
    if (saved.selectedIsEmailVerified !== undefined)
      this.selectedIsEmailVerified.set(saved.selectedIsEmailVerified ?? null);
    if (saved.selectedRole !== undefined) this.selectedRole.set(saved.selectedRole ?? null);

    // 首次进入即加载列表（恢复筛选后），无需手动点刷新。
    this.reloadList();
  }

  reloadList() {
    this.loading.set(true);
    this.service
      .getUsers({
        keyword: this.searchQuery(),
        isActive: this.selectedIsActive() ?? undefined,
        isEmailVerified: this.selectedIsEmailVerified() ?? undefined,
        role: this.selectedRole() ?? undefined,
        offset: this.offset(),
        limit: this.limit(),
        sorting: this.sorting(),
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe((data) => {
        this.users.set(data.items);
        this.totalRecords.set(data.totalCount);
      });
  }

  onSearchQueryChange(value: string) {
    this.searchQuery.set(value);
    this.searchSubject.next(value);
  }

  onActiveChange(value: boolean | null | undefined) {
    this.selectedIsActive.set(value ?? null);
    this.onFilter();
  }

  onEmailVerifiedChange(value: boolean | null | undefined) {
    this.selectedIsEmailVerified.set(value ?? null);
    this.onFilter();
  }

  onRoleChange(value: string | null | undefined) {
    this.selectedRole.set(value ?? null);
    this.onFilter();
  }

  onFilter() {
    this.offset.set(0);
    this.filterStateService.save(this.FILTER_KEY, {
      searchQuery: this.searchQuery(),
      selectedIsActive: this.selectedIsActive(),
      selectedIsEmailVerified: this.selectedIsEmailVerified(),
      selectedRole: this.selectedRole(),
    });
    this.reloadList();
  }

  onPageChange(event: UserTableFilterEvent) {
    this.offset.set(event.offset);
    this.limit.set(event.limit);
    if (event.sorting) this.sorting.set(event.sorting);
    this.reloadList();
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
      .pipe(finalize(() => this.editDialogLoading.set(false)))
      .subscribe((user) => this.selectedUser.set(user));
  }

  handleSave(data: CreateUserInputDto | UpdateUserInputDto) {
    this.editDialogSaving.set(true);
    const selected = this.selectedUser();
    const request = selected
      ? this.service.updateUser(selected.id, data as UpdateUserInputDto)
      : this.service.createUser(data as CreateUserInputDto);

    request
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.editDialogSaving.set(false)),
      )
      .subscribe({
        next: () => {
          //#if (IncludeLocalization)
          notify.success(this.transloco.translate('common.success'), {
            detail: this.transloco.translate(
              selected ? 'users.toast.updated' : 'users.toast.created',
            ),
          });
          //#else
          notify.success('Success', {
            detail: selected ? 'User updated successfully' : 'User created successfully',
          });
          //#endif
          this.editDialogVisible.set(false);
          this.reloadList();
        },
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
    request.subscribe(() => {
      //#if (IncludeLocalization)
      notify.success(this.transloco.translate('common.success'), {
        detail: this.transloco.translate(
          user.isActive ? 'users.toast.disabled' : 'users.toast.enabled',
        ),
      });
      //#else
      notify.success('Success', { detail: user.isActive ? 'User disabled' : 'User enabled' });
      //#endif
      this.reloadList();
    });
  }

  async handleDelete(user: UserManagementOutputDto) {
    if (user.isSuperAdmin) {
      //#if (IncludeLocalization)
      notify.warn(this.transloco.translate('users.toast.cannotDeleteSummary'), {
        detail: this.transloco.translate('users.toast.cannotDeleteSuperAdmin'),
      });
      //#else
      notify.warn('Cannot delete', {
        detail: 'The built-in super administrator cannot be deleted',
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

    this.service.deleteUser(user.id).subscribe(() => {
      //#if (IncludeLocalization)
      notify.success(this.transloco.translate('common.success'), {
        detail: this.transloco.translate('users.toast.deleted'),
      });
      //#else
      notify.success('Success', { detail: 'User deleted' });
      //#endif
      this.reloadList();
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
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.resetPasswordSaving.set(false)),
      )
      .subscribe(() => {
        //#if (IncludeLocalization)
        notify.success(this.transloco.translate('common.success'), {
          detail: this.transloco.translate('users.toast.passwordReset'),
        });
        //#else
        notify.success('Success', { detail: 'Password has been reset' });
        //#endif
        this.resetPasswordDialogVisible.set(false);
        this.resettingUserId.set(null);
      });
  }
}
