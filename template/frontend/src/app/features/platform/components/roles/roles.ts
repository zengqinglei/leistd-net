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
  HlmInputGroupAddon,
  HlmInputGroupInput,
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

import { RoleEditDialog } from './widgets/role-edit-dialog/role-edit-dialog';
import { RoleTable } from './widgets/role-table/role-table';
import { applicationErrorMessage } from '../../../../core/errors/application-http-error';
import { ConfirmService } from '../../../../core/feedback/confirm-service';
//#if (IncludeLocalization)
import { refreshOnLanguageChange, translationReady } from '../../../../core/i18n/translation-ready';
//#endif
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { LayoutService } from '../../../../layout/services/layout-service';
import { PERMISSIONS } from '../../../../shared/models/permission';
import {
  paginationFromQuery,
  sortingFromQuery,
  tableStateToQuery,
  toApiSorting,
} from '../../../../shared/utils/table-query-state';
import {
  CreateRoleInputDto,
  GetRolesInputDto,
  RoleOutputDto,
  UpdateRoleInputDto,
} from '../../models/role.dto';
import { RoleService } from '../../services/role-service';
import { PermissionGrantDialog } from '../../widgets/permission-grant-dialog/permission-grant-dialog';

// 只列实体自身的列：userCount / permissionCount 是聚合出来的派生值，后端无法据其排序。
const ROLE_SORT_COLUMNS = ['displayName', 'sort', 'creationTime'] as const;
const DEFAULT_ROLE_SORTING: SortingState = [{ id: 'sort', desc: false }];

/**
 * 角色管理页。
 *
 * 角色数据全部来自 API：前端不再保留任何硬编码角色列表，新建的角色立即可用于用户分配。
 * 列表状态（分页、排序、关键字）落在 URL 查询参数上，刷新与前进后退均可复原。
 */
@Component({
  selector: 'app-roles',
  imports: [
    NgIcon,
    HlmButton,
    HlmInputGroup,
    HlmInputGroupAddon,
    HlmInputGroupInput,
    ...HlmTooltipImports,
    RoleTable,
    RoleEditDialog,
    PermissionGrantDialog,
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
  ],
  providers: [provideIcons({ lucidePlus, lucideRefreshCw, lucideSearch })],
  templateUrl: './roles.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Roles {
  private readonly roleService = inject(RoleService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly confirmService = inject(ConfirmService);
  private readonly authorizationService = inject(AuthorizationService);
  private readonly layoutService = inject(LayoutService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  private readonly translationReady = translationReady(this.transloco);
  //#endif

  private readonly searchSubject = new Subject<string>();
  private readonly refreshRequests = new Subject<void>();
  private readonly queryParams = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });

  readonly roles = signal<RoleOutputDto[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(false);

  // 列表状态全部来源于 URL 查询参数（刷新 / 前进后退 / 分享皆可复原）。
  readonly pagination = computed(() => paginationFromQuery(this.queryParams()));
  readonly sorting = computed(() =>
    sortingFromQuery(this.queryParams(), ROLE_SORT_COLUMNS, DEFAULT_ROLE_SORTING),
  );

  // 搜索框即时值：随 URL 回填，输入时乐观更新，防抖后写回 URL。
  readonly searchQuery = signal(this.route.snapshot.queryParamMap.get('keyword') ?? '');
  // 是否处于筛选/搜索态：用于区分「暂无数据」与「无匹配结果」的空状态。
  readonly hasActiveFilters = computed(() => this.searchQuery().trim().length > 0);

  readonly editDialogOpen = signal(false);
  readonly editingRole = signal<RoleOutputDto | null>(null);
  readonly permissionDialogOpen = signal(false);
  readonly permissionRole = signal<RoleOutputDto | null>(null);

  // 操作入口按权限裁剪；前端隐藏只影响体验，后端仍逐个请求校验。
  readonly canCreate = computed(() => this.authorizationService.has(PERMISSIONS.roles.create));
  readonly canUpdate = computed(() => this.authorizationService.has(PERMISSIONS.roles.update));
  readonly canDelete = computed(() => this.authorizationService.has(PERMISSIONS.roles.delete));
  readonly canManagePermissions = computed(() =>
    this.authorizationService.has(PERMISSIONS.roles.managePermissions),
  );

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
          this.roleService.getRoles(this.queryFromParams(params)).pipe(
            catchError((error: unknown) => {
              toast.error(applicationErrorMessage(error));
              return EMPTY;
            }),
            finalize(() => this.loading.set(false)),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        this.roles.set([...result.items]);
        this.totalCount.set(result.totalCount);
      });

    //#if (IncludeLocalization)
    // 页面按钮文案走 transloco.translate()，本页没有筛选下拉这类读了 translationReady 的
    // computed 被渲染，语言变化不会把视图标脏，需显式接上。
    refreshOnLanguageChange(this.transloco);

    //#endif
    // 面包屑末级文案由页面自行设置，与其他平台页保持同一约定。
    //#if (IncludeLocalization)
    effect(() => {
      this.translationReady();
      this.layoutService.title.set(this.transloco.translate('roles.title'));
    });
    //#else
    this.layoutService.title.set('Role Management');
    //#endif
  }

  reload(): void {
    this.refreshRequests.next();
  }

  onSearchQueryChange(value: string): void {
    this.searchQuery.set(value);
    this.searchSubject.next(value);
  }

  onPaginationChange(pagination: PaginationState): void {
    this.updateQuery(tableStateToQuery(pagination, this.sorting()));
  }

  onSortingChange(sorting: SortingState): void {
    this.updateQuery({
      ...tableStateToQuery({ ...this.pagination(), pageIndex: 0 }, sorting),
      page: 1,
    });
  }

  openCreate(): void {
    this.editingRole.set(null);
    this.editDialogOpen.set(true);
  }

  openEdit(role: RoleOutputDto): void {
    this.editingRole.set(role);
    this.editDialogOpen.set(true);
  }

  openPermissions(role: RoleOutputDto): void {
    this.permissionRole.set(role);
    this.permissionDialogOpen.set(true);
  }

  onSave(payload: CreateRoleInputDto | UpdateRoleInputDto): void {
    const editing = this.editingRole();
    const request$ = editing
      ? this.roleService.updateRole(editing.id, payload as UpdateRoleInputDto)
      : this.roleService.createRole(payload as CreateRoleInputDto);

    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.editDialogOpen.set(false);
        this.reload();
        toast.success(this.savedMessage());
      },
      error: (error) => toast.error(applicationErrorMessage(error)),
    });
  }

  async onDelete(role: RoleOutputDto): Promise<void> {
    const confirmed = await this.confirmService.open({
      header: this.deleteTitle(),
      message: this.deleteDescription(role),
      confirmText: this.deleteConfirmLabel(),
      variant: 'destructive',
    });

    if (!confirmed) {
      return;
    }

    this.roleService
      .deleteRole(role.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.reload();
          toast.success(this.deletedMessage());
        },
        error: (error) => toast.error(applicationErrorMessage(error)),
      });
  }

  /** 权限保存后刷新列表与当前用户权限：撤销自己的权限应当立即反映到菜单上。 */
  onPermissionsSaved(): void {
    this.permissionDialogOpen.set(false);
    this.reload();
    this.authorizationService.reload().subscribe({ error: () => undefined });
  }

  //#if (IncludeLocalization)
  readonly searchPlaceholder = () => this.transloco.translate('roles.searchPlaceholder');
  readonly refreshLabel = () => this.transloco.translate('common.refresh');
  readonly newRoleLabel = () => this.transloco.translate('roles.create');
  private savedMessage = () => this.transloco.translate('roles.saved');
  private deletedMessage = () => this.transloco.translate('roles.deleted');
  private deleteTitle = () => this.transloco.translate('roles.deleteTitle');
  private deleteDescription = (role: RoleOutputDto) =>
    this.transloco.translate('roles.deleteDescription', { name: role.displayName });
  private deleteConfirmLabel = () => this.transloco.translate('common.delete');
  //#else
  readonly searchPlaceholder = () => 'Search roles';
  readonly refreshLabel = () => 'Refresh';
  readonly newRoleLabel = () => 'New role';
  private savedMessage = () => 'Role saved';
  private deletedMessage = () => 'Role deleted';
  private deleteTitle = () => 'Delete role';
  private deleteDescription = (role: RoleOutputDto) =>
    `Delete "${role.displayName}"? Its permission grants will be removed as well.`;
  private deleteConfirmLabel = () => 'Delete';
  //#endif

  private queryFromParams(params: ParamMap): GetRolesInputDto {
    const pagination = paginationFromQuery(params);
    return {
      offset: pagination.pageIndex * pagination.pageSize,
      limit: pagination.pageSize,
      keyword: params.get('keyword') || undefined,
      sorting: toApiSorting(sortingFromQuery(params, ROLE_SORT_COLUMNS, DEFAULT_ROLE_SORTING)),
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
}
