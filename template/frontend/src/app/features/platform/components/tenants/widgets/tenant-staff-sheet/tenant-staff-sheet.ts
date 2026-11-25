//#if (TenantWorkspace && LocalIdentity)
import { NgTemplateOutlet } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideChevronRight,
  lucideCircleAlert,
  lucideEllipsis,
  lucidePencil,
  lucidePlus,
  lucideSearch,
  lucideUserMinus,
  lucideUsers,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmInputGroupImports } from '@spartan-ng/helm/input-group';
import { HlmSheetImports } from '@spartan-ng/helm/sheet';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { ColumnDef } from '@tanstack/angular-table';
import { firstValueFrom, tap } from 'rxjs';

import { applicationErrorMessage } from '../../../../../../core/errors/application-http-error';
import { ConfirmService } from '../../../../../../core/feedback/confirm-service';
import { WorkspaceContextService } from '../../../../../../core/services/workspace-context-service';
import { ReadonlyListPopover } from '../../../../../../shared/components/readonly-list-popover/readonly-list-popover';
import {
  TableLoading,
  TableLoadingBody,
} from '../../../../../../shared/components/table-loading/table-loading';
import { ColumnValue } from '../../../../../../shared/directives/column-value';
import { StatusDot } from '../../../../../../shared/directives/status-dot';
import { TableFit } from '../../../../../../shared/directives/table-fit';
import { AppDate } from '../../../../../../shared/pipes/app-date-pipe';
//#if (!IncludeLocalization)
import { englishText } from '../../../../../../shared/utils/english-text';
//#endif
import {
  ACTIONS_COLUMN_META,
  TITLE_COLUMN_META,
  TITLE_CONTENT_CLASS,
  tableColumnVisibility,
  hiddenDetailColumns,
} from '../../../../../../shared/utils/table-column-meta';
import {
  injectAppTable,
  type AppTableFeatures,
} from '../../../../../../shared/utils/table-features';
import { tableViewportSignal } from '../../../../../../shared/utils/table-viewport';
import { RoleBriefDto } from '../../../../dtos/role.dto';
import { TenantStaffOutputDto } from '../../../../dtos/tenant-staff.dto';
import { TenantOutputDto } from '../../../../dtos/tenant.dto';
import { TenantStaffEditDialog } from './widgets/tenant-staff-edit-dialog/tenant-staff-edit-dialog';

/**
 * 租户的平台人员抽屉：打开状态只来自查询参数 `staff`（租户 id）。列出已授予的宿主用户与其关联账号，
 * 授予、修改角色与移除访问后重新加载；重新加载保留现有行，只标记忙碌。
 */
@Component({
  selector: 'app-tenant-staff-sheet',
  imports: [
    NgIcon,
    NgTemplateOutlet,
    HlmBadge,
    HlmButton,
    ...HlmInputGroupImports,
    ReadonlyListPopover,
    TableLoading,
    TableLoadingBody,
    ColumnValue,
    StatusDot,
    TableFit,
    AppDate,
    TenantStaffEditDialog,
    ...HlmSheetImports,
    ...HlmTableImports,
    ...HlmDropdownMenuImports,
    ...HlmTooltipImports,
    ...HlmEmptyImports,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [
    provideIcons({
      lucideChevronRight,
      lucideCircleAlert,
      lucideEllipsis,
      lucidePencil,
      lucidePlus,
      lucideSearch,
      lucideUserMinus,
      lucideUsers,
    }),
  ],
  templateUrl: './tenant-staff-sheet.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TenantStaffSheet {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly confirm = inject(ConfirmService);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly owner = inject(DestroyRef);
  private readonly query = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });
  readonly tenantId = computed(() => this.query().get('staff'));
  readonly tenant = signal<TenantOutputDto | null>(null);
  readonly rows = signal<TenantStaffOutputDto[]>([]);
  readonly roles = signal<RoleBriefDto[]>([]);
  readonly search = signal('');
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly dialogOpen = signal(false);
  /** 修改角色的对象；为空时对话框是授予。 */
  readonly editing = signal<TenantStaffOutputDto | null>(null);
  readonly grantedIds = computed(() => new Set(this.rows().map((row) => row.homeUserId)));
  /** 已加载的租户：换租户时清空上一个租户的行，同一租户重新加载时保留。 */
  private loadedId: string | null = null;
  private revision = 0;
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif

  private text(key: string, params?: Record<string, string>): string {
    //#if (IncludeLocalization)
    return this.transloco.translate(key, params);
    //#else
    return this.t(key, params);
    //#endif
  }

  readonly filtered = computed(() => {
    const keyword = this.search().trim().toLowerCase();
    return this.rows().filter((row) =>
      `${row.username} ${row.displayName ?? ''} ${row.linkedUsername}`
        .toLowerCase()
        .includes(keyword),
    );
  });
  readonly viewport = tableViewportSignal();
  private readonly fit = viewChild(TableFit);
  private readonly columns: ColumnDef<AppTableFeatures, TenantStaffOutputDto>[] = [
    {
      id: 'person',
      accessorKey: 'username',
      enableSorting: false,
      enableHiding: false,
      meta: TITLE_COLUMN_META,
    },
    {
      id: 'linkedUsername',
      accessorKey: 'linkedUsername',
      enableSorting: false,
      meta: { priority: 'secondary', detail: true },
    },
    {
      id: 'roles',
      accessorKey: 'roles',
      enableSorting: false,
      meta: { priority: 'secondary', detail: true },
    },
    {
      id: 'status',
      accessorKey: 'isActive',
      enableSorting: false,
      meta: { priority: 'secondary', detail: true },
    },
    {
      id: 'creationTime',
      accessorKey: 'creationTime',
      enableSorting: false,
      meta: { priority: 'tertiary', detail: true },
    },
    { id: 'actions', enableSorting: false, enableHiding: false, meta: ACTIONS_COLUMN_META },
  ];
  /** 列标签的词条键：表头与展开详情共用。 */
  protected readonly columnLabelKeys: Readonly<Record<string, string>> = {
    person: 'tenants.staff.person',
    linkedUsername: 'tenants.staff.linkedUsername',
    roles: 'tenants.staff.roles',
    status: 'tenants.staff.status',
    creationTime: 'tenants.staff.creationTime',
  };
  protected readonly titleContentClass = TITLE_CONTENT_CLASS;
  readonly hasCollapsedColumns = computed(() => this.detailColumns().length > 0);
  readonly table = injectAppTable(() => ({
    data: this.filtered(),
    columns: this.columns,
    getRowId: (row) => row.homeUserId,
    state: {
      columnVisibility: tableColumnVisibility(this.columns, this.fit()?.level() ?? this.viewport()),
    },
  }));
  //#if (!IncludeLocalization)
  readonly t = englishText(ENGLISH);
  //#endif

  constructor() {
    effect(() => {
      const id = this.tenantId();
      untracked(() => {
        if (id) void this.load(id);
        else this.revision++;
      });
    });
  }

  /** 展开详情里列出的列：被折叠的数据列，按列定义顺序。 */
  detailColumns() {
    return hiddenDetailColumns(this.table);
  }

  roleItems(row: TenantStaffOutputDto) {
    return row.roles.map((role) => ({ id: role.id, label: role.displayName, detail: role.name }));
  }

  close(): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { staff: null },
      queryParamsHandling: 'merge',
    });
  }

  async load(id = this.tenantId()): Promise<void> {
    if (!id) return;
    const revision = ++this.revision;
    if (id !== this.loadedId) {
      this.loadedId = id;
      this.tenant.set(null);
      this.rows.set([]);
      this.roles.set([]);
      this.search.set('');
    }
    this.loading.set(true);
    this.error.set(null);
    try {
      const [tenant, rows, roles] = await Promise.all([
        firstValueFrom(this.http.get<TenantOutputDto>(`/api/v1/tenants/${id}`)),
        firstValueFrom(this.http.get<TenantStaffOutputDto[]>(`/api/v1/tenants/${id}/staff`)),
        firstValueFrom(this.http.get<RoleBriefDto[]>(`/api/v1/tenants/${id}/staff/roles`)),
      ]);
      if (revision !== this.revision) return;
      this.tenant.set(tenant);
      this.rows.set(rows);
      this.roles.set(roles);
    } catch (error) {
      if (revision !== this.revision) return;
      // 重新加载失败保留已有内容并提示；首次加载失败才换成可重试的空态。
      if (this.tenant()) toast.error(applicationErrorMessage(error));
      else this.error.set(applicationErrorMessage(error));
    } finally {
      if (revision === this.revision) this.loading.set(false);
    }
  }

  openGrant(row: TenantStaffOutputDto | null = null): void {
    this.editing.set(row);
    this.dialogOpen.set(true);
  }

  onSaved(): void {
    this.dialogOpen.set(false);
    void this.load();
  }

  async remove(row: TenantStaffOutputDto): Promise<void> {
    const removed = await this.confirm.open({
      owner: this.owner,
      variant: 'destructive',
      header: row.displayName ?? row.username,
      message: this.text('tenants.staff.removeDescription', {
        person: row.displayName ?? row.username,
        name: this.tenant()?.name ?? '',
      }),
      confirmText: this.text('tenants.staff.remove'),
      action: () =>
        this.http
          .delete(`/api/v1/tenants/${this.tenantId()}/staff/${row.homeUserId}`)
          .pipe(tap(() => void this.load())),
    });
    if (removed) {
      this.workspace.refreshAfterAccessChange(row.homeUserId);
      toast.success(this.text('tenants.staff.removed'));
    }
  }
}
//#if (!IncludeLocalization)

const ENGLISH: Record<string, string> = {
  'tenants.staff.title': 'Platform staff for {{name}}',
  'tenants.staff.summary': '{{name}} · {{count}} granted',
  'tenants.staff.grant': 'Grant access',
  'tenants.staff.search': 'Search staff',
  'tenants.staff.person': 'Person',
  'tenants.staff.linkedUsername': 'Linked account',
  'tenants.staff.roles': 'Roles',
  'tenants.staff.status': 'Account status',
  'tenants.staff.creationTime': 'Granted at',
  'tenants.staff.actions': 'Actions',
  'tenants.staff.edit': 'Edit roles',
  'tenants.staff.remove': 'Remove access',
  'tenants.staff.removed': 'Access removed',
  'tenants.staff.removeDescription':
    '{{person}} will no longer be able to enter {{name}}. Their operation records in {{name}} are kept.',
  'tenants.staff.noMatch': 'No matching staff',
  'tenants.staff.emptyTitle': 'No platform staff yet',
  'tenants.staff.emptyDescription':
    'Grant access so home accounts can enter {{name}} with a linked account and the roles you choose.',
  'tenants.staff.filterRoles': 'Filter roles',
  'tenants.staff.noMatchingRoles': 'No matching roles',
  'tenants.staff.active': 'Enabled',
  'tenants.staff.inactive': 'Disabled',
  'common.close': 'Close',
  'common.details': 'View details',
  'common.loadFailed': "Couldn't load the list",
  'common.retry': 'Retry',
};
//#endif
//#endif
