import { CommonModule } from '@angular/common';
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
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
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
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
//#endif
import { FormsModule } from '@angular/forms';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TooltipModule } from 'primeng/tooltip';
import { Subject } from 'rxjs';
import { debounceTime, distinctUntilChanged, finalize } from 'rxjs/operators';

//#if (IncludeLocalization)
import { translationReady } from '../../../../core/i18n/translation-ready';
//#endif
import { LayoutService } from '../../../../layout/services/layout-service';
import { ROLE_LABEL_MAP } from '../../../../shared/models/role.enum';
import { FilterStateService } from '../../../../shared/services/filter-state.service';
import {
  CreateUserInputDto,
  ResetUserPasswordInputDto,
  UpdateUserInputDto,
  UserManagementOutputDto,
} from '../../models/user-management.dto';
import { UserManagementService } from '../../services/user-management-service';
import { ResetUserPasswordDialogComponent } from './widgets/reset-user-password-dialog/reset-user-password-dialog';
import { UserEditDialogComponent } from './widgets/user-edit-dialog/user-edit-dialog';
import { UserTable, UserTableFilterEvent } from './widgets/user-table/user-table';

@Component({
  selector: 'app-users',
  imports: [
    CommonModule,
    FormsModule,
    SelectModule,
    IconFieldModule,
    InputIconModule,
    InputTextModule,
    ButtonModule,
    TooltipModule,
    ConfirmDialogModule,
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
    UserTable,
    UserEditDialogComponent,
    ResetUserPasswordDialogComponent,
  ],
  providers: [ConfirmationService],
  templateUrl: './users.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsersPage implements OnInit {
  private readonly service = inject(UserManagementService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly messageService = inject(MessageService);
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
  readonly confirmHeader = () => this.transloco.translate('common.confirm');
  readonly confirmAcceptLabel = () => this.transloco.translate('common.ok');
  readonly confirmRejectLabel = () => this.transloco.translate('common.cancel');
  //#else
  readonly allStatusPlaceholder = () => 'All statuses';
  readonly allEmailStatusPlaceholder = () => 'All email statuses';
  readonly allRolesPlaceholder = () => 'All roles';
  readonly searchPlaceholder = () => 'Search username / email / display name...';
  readonly refreshLabel = () => 'Refresh';
  readonly newUserLabel = () => 'New user';
  readonly confirmHeader = () => 'Confirm';
  readonly confirmAcceptLabel = () => 'OK';
  readonly confirmRejectLabel = () => 'Cancel';
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

  onActiveChange(value: boolean | null) {
    this.selectedIsActive.set(value);
    this.onFilter();
  }

  onEmailVerifiedChange(value: boolean | null) {
    this.selectedIsEmailVerified.set(value);
    this.onFilter();
  }

  onRoleChange(value: string | null) {
    this.selectedRole.set(value);
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
          this.messageService.add({
            severity: 'success',
            summary: this.transloco.translate('common.success'),
            detail: this.transloco.translate(
              selected ? 'users.toast.updated' : 'users.toast.created',
            ),
          });
          //#else
          this.messageService.add({
            severity: 'success',
            summary: 'Success',
            detail: selected ? 'User updated successfully' : 'User created successfully',
          });
          //#endif
          this.editDialogVisible.set(false);
          this.reloadList();
        },
      });
  }

  handleToggleActive(user: UserManagementOutputDto) {
    this.confirmationService.confirm({
      //#if (IncludeLocalization)
      message: this.transloco.translate(
        user.isActive ? 'users.confirm.disableMessage' : 'users.confirm.enableMessage',
        {
          name: user.username,
        },
      ),
      header: this.transloco.translate(
        user.isActive ? 'users.confirm.disableHeader' : 'users.confirm.enableHeader',
      ),
      //#else
      message: user.isActive
        ? `Are you sure you want to disable user ${user.username}?`
        : `Are you sure you want to enable user ${user.username}?`,
      header: user.isActive ? 'Confirm disable' : 'Confirm enable',
      //#endif
      icon: 'pi pi-exclamation-triangle',
      accept: () => {
        const request = user.isActive
          ? this.service.disableUser(user.id)
          : this.service.enableUser(user.id);
        request.subscribe(() => {
          //#if (IncludeLocalization)
          this.messageService.add({
            severity: 'success',
            summary: this.transloco.translate('common.success'),
            detail: this.transloco.translate(
              user.isActive ? 'users.toast.disabled' : 'users.toast.enabled',
            ),
          });
          //#else
          this.messageService.add({
            severity: 'success',
            summary: 'Success',
            detail: user.isActive ? 'User disabled' : 'User enabled',
          });
          //#endif
          this.reloadList();
        });
      },
    });
  }

  handleDelete(user: UserManagementOutputDto) {
    if (user.isSuperAdmin) {
      //#if (IncludeLocalization)
      this.messageService.add({
        severity: 'warn',
        summary: this.transloco.translate('users.toast.cannotDeleteSummary'),
        detail: this.transloco.translate('users.toast.cannotDeleteSuperAdmin'),
      });
      //#else
      this.messageService.add({
        severity: 'warn',
        summary: 'Cannot delete',
        detail: 'The built-in super administrator cannot be deleted',
      });
      //#endif
      return;
    }

    this.confirmationService.confirm({
      //#if (IncludeLocalization)
      message: this.transloco.translate('users.confirm.deleteMessage', { name: user.username }),
      header: this.transloco.translate('users.confirm.deleteHeader'),
      //#else
      message: `Are you sure you want to delete user ${user.username}? Once deleted, the user will no longer be able to sign in.`,
      header: 'Confirm delete',
      //#endif
      icon: 'pi pi-exclamation-triangle',
      accept: () => {
        this.service.deleteUser(user.id).subscribe(() => {
          //#if (IncludeLocalization)
          this.messageService.add({
            severity: 'success',
            summary: this.transloco.translate('common.success'),
            detail: this.transloco.translate('users.toast.deleted'),
          });
          //#else
          this.messageService.add({
            severity: 'success',
            summary: 'Success',
            detail: 'User deleted',
          });
          //#endif
          this.reloadList();
        });
      },
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
        this.messageService.add({
          severity: 'success',
          summary: this.transloco.translate('common.success'),
          detail: this.transloco.translate('users.toast.passwordReset'),
        });
        //#else
        this.messageService.add({
          severity: 'success',
          summary: 'Success',
          detail: 'Password has been reset',
        });
        //#endif
        this.resetPasswordDialogVisible.set(false);
        this.resettingUserId.set(null);
      });
  }
}
