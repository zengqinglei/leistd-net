//#if (TenantWorkspace && LocalIdentity)
import { HttpClient, HttpContext } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucidePlus,
  lucideSearch,
  lucidePencil,
  lucideEllipsis,
  lucideUsers,
  lucideChevronRight,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmCommandImports } from '@spartan-ng/helm/command';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputGroupImports } from '@spartan-ng/helm/input-group';
import { HlmSheetImports } from '@spartan-ng/helm/sheet';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { ColumnDef } from '@tanstack/angular-table';
import {
  catchError,
  debounceTime,
  distinctUntilChanged,
  firstValueFrom,
  of,
  Subject,
  switchMap,
  tap,
} from 'rxjs';

import {
  ApplicationHttpError,
  applicationErrorMessage,
} from '../../../../../../core/errors/application-http-error';
import { ConfirmService } from '../../../../../../core/feedback/confirm-service';
import { WORKSPACE_SCOPE } from '../../../../../../core/interceptors/http-context-tokens';
import { ReadonlyListPopover } from '../../../../../../shared/components/readonly-list-popover/readonly-list-popover';
import {
  TableLoading,
  TableLoadingBody,
} from '../../../../../../shared/components/table-loading/table-loading';
import { StatusDot } from '../../../../../../shared/directives/status-dot';
import { TableFit } from '../../../../../../shared/directives/table-fit';
import { AppDate } from '../../../../../../shared/pipes/app-date-pipe';
//#if (!IncludeLocalization)
import { englishText } from '../../../../../../shared/utils/english-text';
//#endif
import {
  ACTIONS_COLUMN_META,
  TITLE_COLUMN_META,
  tableColumnVisibility,
  hiddenDetailColumns,
} from '../../../../../../shared/utils/table-column-meta';
import {
  injectAppTable,
  type AppTableFeatures,
} from '../../../../../../shared/utils/table-features';
import { tableViewportSignal } from '../../../../../../shared/utils/table-viewport';
import { RoleBriefDto } from '../../../../dtos/role.dto';
import {
  TenantStaffCandidateOutputDto,
  TenantStaffOutputDto,
} from '../../../../dtos/tenant-staff.dto';
import { TenantOutputDto } from '../../../../dtos/tenant.dto';

@Component({
  selector: 'app-tenant-staff-sheet',
  imports: [
    NgIcon,
    HlmBadge,
    HlmButton,
    ...HlmInputGroupImports,
    ReadonlyListPopover,
    HlmSpinner,
    TableLoading,
    TableLoadingBody,
    StatusDot,
    TableFit,
    AppDate,
    ...HlmSheetImports,
    ...HlmDialogImports,
    ...HlmCommandImports,
    ...HlmCheckboxImports,
    ...HlmAvatarImports,
    ...HlmFieldImports,
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
      lucideSearch,
      lucidePlus,
      lucidePencil,
      lucideEllipsis,
      lucideUsers,
      lucideChevronRight,
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
  private readonly owner = inject(DestroyRef);
  private readonly query = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });
  readonly tenantId = computed(() => this.query().get('staff'));
  readonly tenant = signal<TenantOutputDto | null>(null);
  readonly rows = signal<TenantStaffOutputDto[]>([]);
  readonly roles = signal<RoleBriefDto[]>([]);
  readonly users = signal<TenantStaffCandidateOutputDto[]>([]);
  readonly peopleSearch = new Subject<string>();
  readonly search = signal('');
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly dialogOpen = signal(false);
  readonly editing = signal<TenantStaffOutputDto | null>(null);
  readonly selectedUserId = signal<string | null>(null);
  readonly selectedRoles = signal<string[]>([]);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly saveErrorField = signal<'person' | 'roles'>('roles');
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

  readonly filtered = computed(() =>
    this.rows().filter((row) =>
      `${row.username} ${row.displayName ?? ''}`
        .toLowerCase()
        .includes(this.search().toLowerCase()),
    ),
  );
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
  readonly hasCollapsedColumns = computed(() => hiddenDetailColumns(this.table).length > 0);
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
    this.peopleSearch
      .pipe(
        debounceTime(200),
        distinctUntilChanged(),
        switchMap((keyword) =>
          this.http
            .get<TenantStaffCandidateOutputDto[]>(
              `/api/v1/tenants/${this.tenantId()}/staff/candidates`,
              {
                params: { keyword },
                context: new HttpContext().set(WORKSPACE_SCOPE, null),
              },
            )
            .pipe(
              catchError((error: unknown) => {
                this.saveErrorField.set('person');
                this.saveError.set(applicationErrorMessage(error));
                return of([]);
              }),
            ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((users) => this.users.set(users));
    effect(() => {
      const id = this.tenantId();
      if (id) void this.load(id);
      else this.revision++;
    });
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
    this.loading.set(true);
    this.error.set(null);
    try {
      const [tenant, rows, roles, users] = await Promise.all([
        firstValueFrom(this.http.get<TenantOutputDto>(`/api/v1/tenants/${id}`)),
        firstValueFrom(this.http.get<TenantStaffOutputDto[]>(`/api/v1/tenants/${id}/staff`)),
        firstValueFrom(this.http.get<RoleBriefDto[]>(`/api/v1/tenants/${id}/staff/roles`)),
        firstValueFrom(
          this.http.get<TenantStaffCandidateOutputDto[]>(`/api/v1/tenants/${id}/staff/candidates`, {
            context: new HttpContext().set(WORKSPACE_SCOPE, null),
          }),
        ),
      ]);
      if (revision !== this.revision) return;
      this.tenant.set(tenant);
      this.rows.set(rows);
      this.roles.set(roles);
      this.users.set(users);
    } catch (error) {
      if (revision === this.revision) this.error.set(applicationErrorMessage(error));
    } finally {
      if (revision === this.revision) this.loading.set(false);
    }
  }

  openGrant(row: TenantStaffOutputDto | null = null): void {
    this.editing.set(row);
    this.selectedUserId.set(row?.homeUserId ?? null);
    this.selectedRoles.set(row?.roles.map((role) => role.id) ?? []);
    this.saveError.set(null);
    this.dialogOpen.set(true);
  }

  isGranted(id: string): boolean {
    return this.rows().some((row) => row.homeUserId === id);
  }
  toggleRole(id: string): void {
    this.selectedRoles.update((ids) =>
      ids.includes(id) ? ids.filter((value) => value !== id) : [...ids, id],
    );
  }

  async save(): Promise<void> {
    if (this.saving() || !this.selectedUserId() || !this.selectedRoles().length) return;
    this.saving.set(true);
    this.saveError.set(null);
    try {
      await firstValueFrom(
        this.http.post(`/api/v1/tenants/${this.tenantId()}/staff`, {
          homeUserId: this.selectedUserId(),
          roleIds: this.selectedRoles(),
        }),
      );
      this.dialogOpen.set(false);
      toast.success(this.text('tenants.staff.saved'));
      await this.load();
    } catch (error) {
      this.saveErrorField.set(
        !this.editing() &&
          error instanceof ApplicationHttpError &&
          ['User:UsernameTaken', 'User:NotFound'].includes(error.code ?? '')
          ? 'person'
          : 'roles',
      );
      this.saveError.set(applicationErrorMessage(error));
    } finally {
      this.saving.set(false);
    }
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
    if (removed) toast.success(this.text('tenants.staff.removed'));
  }
}
//#if (!IncludeLocalization)

const ENGLISH: Record<string, string> = {
  'tenants.staff.grantTitle': 'Grant access to {{name}}',
  'tenants.staff.editTitle': 'Edit roles in {{name}}',
  'tenants.staff.personLabel': 'Platform staff',
  'tenants.staff.rolesLabel': 'Roles in {{name}}',
  'tenants.staff.saved': 'Saved',
  'tenants.staff.removed': 'Access removed',
  'tenants.staff.removeDescription':
    '{{person}} will no longer be able to enter {{name}}. Their operation records in {{name}} are kept.',
  'tenants.staff.title': 'Platform staff for {{name}}',
  'tenants.staff.grantDescription':
    'Each person granted access gets a linked account in {{name}} and acts with the selected roles.',
  'tenants.staff.description': 'These home accounts can enter this tenant with the listed roles.',
  'tenants.staff.grant': 'Grant access',
  'tenants.staff.search': 'Search staff',
  'tenants.staff.person': 'Person',
  'tenants.staff.roles': 'Roles',
  'tenants.staff.status': 'Status',
  'tenants.staff.creationTime': 'Granted at',
  'tenants.staff.actions': 'Actions',
  'tenants.staff.edit': 'Edit roles',
  'tenants.staff.remove': 'Remove access',
  'tenants.staff.granted': 'Already granted',
  'tenants.staff.linkedName': 'Linked username: platform.<username>',
  'tenants.staff.noMatch': 'No matching staff',
  'tenants.staff.builtIn': 'Built in',
  'tenants.staff.filterRoles': 'Filter roles',
  'tenants.staff.noMatchingRoles': 'No matching roles',
  'tenants.staff.empty': 'No platform staff yet',
  'common.close': 'Close',
  'common.cancel': 'Cancel',
  'common.save': 'Save',
  'common.retry': 'Retry',
  'tenants.staff.active': 'Enabled',
  'tenants.staff.inactive': 'Disabled',
};
//#endif
//#endif
