//#if (TenantWorkspace && LocalIdentity)
import { HttpClient, HttpContext } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  input,
  model,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormField, form, required, validate, ValidationError } from '@angular/forms/signals';
//#if (IncludeLocalization)
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronDown } from '@ng-icons/lucide';
import {
  BrnComboboxAnchor,
  BrnComboboxPopoverTrigger,
  BrnComboboxTrigger,
} from '@spartan-ng/brain/combobox';
import { BrnFieldControlDescribedBy } from '@spartan-ng/brain/field';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmComboboxImports } from '@spartan-ng/helm/combobox';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { catchError, defer, firstValueFrom, map, of, Subject, switchMap, timer } from 'rxjs';

import { DialogClose } from '../../../../../../../../core/components/dialog-close/dialog-close';
import {
  ApplicationHttpError,
  applicationErrorMessage,
} from '../../../../../../../../core/errors/application-http-error';
import { WORKSPACE_SCOPE } from '../../../../../../../../core/interceptors/http-context-tokens';
import { injectFormSubmission } from '../../../../../../../../core/validation/form-submission';
import { isSubmittedInvalid } from '../../../../../../../../core/validation/submit-error-state-matcher';
import { ImeGuard } from '../../../../../../../../shared/directives/ime-guard';
import { StatusDot } from '../../../../../../../../shared/directives/status-dot';
import { TruncatedTooltip } from '../../../../../../../../shared/directives/truncated-tooltip';
//#if (!IncludeLocalization)
import { englishText } from '../../../../../../../../shared/utils/english-text';
//#endif
import { RoleBriefDto } from '../../../../../../dtos/role.dto';
import {
  TENANT_STAFF_CANDIDATE_LIMIT,
  TenantStaffCandidateOutputDto,
  TenantStaffOutputDto,
} from '../../../../../../dtos/tenant-staff.dto';

/** 授予时这些业务码指向所选人员（关联账号用户名被占用、宿主用户已不存在），挂在人员字段下。 */
const PERSON_ERROR_CODES = ['User:UsernameTaken', 'User:NotFound'];

interface StaffAccessFormModel {
  homeUserId: string | null;
  roleIds: string[];
}

interface CandidateQuery {
  keyword: string;
  /** 打开与重试立即请求；输入搜索词时防抖。 */
  immediate: boolean;
}

/**
 * 授予进入租户与修改平台人员角色共用的对话框：`member` 为空时授予（选一位宿主用户 + 角色），否则只改角色。
 * 两种形态共用同一个角色列表；成功后自行提示并发出 `saved`，关闭与刷新列表由抽屉处理。
 */
@Component({
  selector: 'app-tenant-staff-edit-dialog',
  // prettier-ignore
  imports: [ImeGuard, DialogClose, FormField, NgIcon, StatusDot, TruncatedTooltip,
    BrnComboboxAnchor,
    BrnComboboxPopoverTrigger,
    BrnComboboxTrigger,
    BrnFieldControlDescribedBy,
    HlmBadge,
    HlmButton,
    HlmSpinner,
    ...HlmAvatarImports,
    ...HlmCheckboxImports,
    ...HlmComboboxImports,
    ...HlmDialogImports,
    ...HlmFieldImports,
    ...HlmTooltipImports,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [provideIcons({ lucideChevronDown })],
  templateUrl: './tenant-staff-edit-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TenantStaffEditDialog {
  private readonly http = inject(HttpClient);
  private readonly formRoot = viewChild.required<ElementRef<HTMLElement>>('accessFormRoot');
  private readonly submitForm = injectFormSubmission();
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#else
  protected readonly t = englishText(ENGLISH);
  //#endif
  protected readonly candidateLimit = TENANT_STAFF_CANDIDATE_LIMIT;

  readonly open = model(false);
  readonly tenantId = input.required<string>();
  readonly tenantName = input('');
  readonly roles = input<RoleBriefDto[]>([]);
  /** 修改角色的对象；为空时是授予。 */
  readonly member = input<TenantStaffOutputDto | null>(null);
  /** 已授予的宿主用户：候选列表里置灰。 */
  readonly grantedIds = input<ReadonlySet<string>>(new Set<string>());
  readonly saved = output<void>();

  readonly candidates = signal<TenantStaffCandidateOutputDto[]>([]);
  readonly candidatesLoading = signal(false);
  readonly candidatesError = signal<string | null>(null);
  readonly keyword = signal('');
  /** 选中的候选人：搜索词变化后列表里可能已没有它，触发器仍要显示。 */
  readonly picked = signal<TenantStaffCandidateOutputDto | null>(null);
  /** 触发器上的全文提示：姓名与用户名。 */
  readonly pickedLabel = computed(() => {
    const person = this.picked();
    if (!person) return '';
    return person.displayName ? `${person.displayName} ${person.username}` : person.username;
  });
  private readonly candidateQueries = new Subject<CandidateQuery>();

  protected readonly formModel = signal<StaffAccessFormModel>({ homeUserId: null, roleIds: [] });
  readonly accessForm = form(this.formModel, (path) => {
    required(path.homeUserId, { when: () => !this.member() });
    validate(path.roleIds, (ctx) => (ctx.value().length === 0 ? { kind: 'required' } : null));
  });
  readonly saving = computed(() => this.accessForm().submitting());
  readonly rolesInvalid = computed(() => isSubmittedInvalid(this.accessForm.roleIds));
  readonly selectedRoleIds = computed(() => this.formModel().roleIds);
  readonly dirty = computed(() => {
    const model = this.formModel();
    const member = this.member();
    if (!member) return model.homeUserId !== null || model.roleIds.length > 0;
    return (
      [...model.roleIds].sort().join(',') !==
      member.roles
        .map((role) => role.id)
        .sort()
        .join(',')
    );
  });
  /** 候选项不在客户端过滤：列表来自服务端搜索。 */
  readonly candidateFilter = () => true;
  readonly candidateToString = (id: string) => {
    const person =
      this.candidates().find((candidate) => candidate.id === id) ??
      (this.picked()?.id === id ? this.picked() : null);
    return person ? (person.displayName ?? person.username) : id;
  };

  constructor() {
    this.candidateQueries
      .pipe(
        switchMap((query) =>
          (query.immediate ? of(0) : timer(200)).pipe(
            switchMap(() =>
              defer(() => {
                this.candidatesLoading.set(true);
                this.candidatesError.set(null);
                return this.http.get<TenantStaffCandidateOutputDto[]>(
                  `/api/v1/tenants/${this.tenantId()}/staff/candidates`,
                  {
                    params: { keyword: query.keyword },
                    context: new HttpContext().set(WORKSPACE_SCOPE, null),
                  },
                );
              }).pipe(
                map((candidates) => ({ candidates, error: null as string | null })),
                catchError((error: unknown) =>
                  of({ candidates: [], error: applicationErrorMessage(error) }),
                ),
              ),
            ),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe(({ candidates, error }) => {
        this.candidates.set(candidates);
        this.candidatesError.set(error);
        this.candidatesLoading.set(false);
      });

    // 每次打开都从对象的当前状态开始：授予为空白，修改为该人现有角色。
    effect(() => {
      const member = this.member();
      if (!this.open()) return;
      untracked(() => {
        this.accessForm().reset();
        this.formModel.set({
          homeUserId: member?.homeUserId ?? null,
          roleIds: member?.roles.map((role) => role.id) ?? [],
        });
        this.picked.set(null);
        this.keyword.set('');
        if (!member) this.candidateQueries.next({ keyword: '', immediate: true });
      });
    });
  }

  initial(text: string): string {
    return [...text][0]?.toUpperCase() ?? '';
  }

  isRoleSelected(id: string): boolean {
    return this.selectedRoleIds().includes(id);
  }

  toggleRole(id: string): void {
    this.formModel.update((model) => ({
      ...model,
      roleIds: model.roleIds.includes(id)
        ? model.roleIds.filter((value) => value !== id)
        : [...model.roleIds, id],
    }));
    this.accessForm.roleIds().markAsTouched();
  }

  pick(id: string | null | undefined): void {
    this.picked.set(this.candidates().find((candidate) => candidate.id === id) ?? null);
  }

  searchCandidates(keyword: string): void {
    this.keyword.set(keyword);
    this.candidateQueries.next({ keyword, immediate: false });
  }

  retryCandidates(): void {
    this.candidateQueries.next({ keyword: this.keyword(), immediate: true });
  }

  async save(): Promise<void> {
    if (this.saving()) return;
    const member = this.member();
    await this.submitForm(this.accessForm, this.formRoot().nativeElement, async () => {
      const { homeUserId, roleIds } = this.formModel();
      try {
        await firstValueFrom(
          this.http.post(`/api/v1/tenants/${this.tenantId()}/staff`, { homeUserId, roleIds }),
          { defaultValue: undefined },
        );
      } catch (error) {
        if (
          !member &&
          error instanceof ApplicationHttpError &&
          PERSON_ERROR_CODES.includes(error.code ?? '')
        ) {
          return [
            {
              kind: 'server',
              message: applicationErrorMessage(error),
              fieldTree: this.accessForm.homeUserId,
            } satisfies ValidationError.WithFieldTree,
          ];
        }
        throw error;
      }
      const person = member
        ? (member.displayName ?? member.username)
        : (this.picked()?.displayName ?? this.picked()?.username ?? '');
      toast.success(
        member
          ? this.text('tenants.staff.rolesSaved', { person })
          : this.text('tenants.staff.grantSucceeded', { person, name: this.tenantName() }),
      );
      this.saved.emit();
      return undefined;
    });
  }

  private text(key: string, params: Record<string, string>): string {
    //#if (IncludeLocalization)
    return this.transloco.translate(key, params);
    //#else
    return this.t(key, params);
    //#endif
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'app.startup.loading': 'Loading...',
  'common.cancel': 'Cancel',
  'common.noData': 'No data',
  'common.retry': 'Retry',
  'common.save': 'Save',
  'tenants.staff.active': 'Enabled',
  'tenants.staff.builtIn': 'Built in',
  'tenants.staff.candidateLimit': 'Showing up to 200 people. Keep typing to narrow the results.',
  'tenants.staff.candidatesLoading': 'Loading people...',
  'tenants.staff.editDescription':
    'Once saved, {{person}} acts in {{name}} with the selected roles.',
  'tenants.staff.editTitle': 'Edit roles',
  'tenants.staff.grant': 'Grant access',
  'tenants.staff.grantDescription': 'Choose a platform staff member and their roles in {{name}}.',
  'tenants.staff.grantTitle': 'Grant access to {{name}}',
  'tenants.staff.grantSucceeded': 'Granted {{person}} access to {{name}}',
  'tenants.staff.granted': 'Already granted',
  'tenants.staff.inactive': 'Disabled',
  'tenants.staff.linkedPreview': 'A new linked account uses {{username}}',
  'tenants.staff.noCandidates': 'No matching people',
  'tenants.staff.personLabel': 'Platform staff',
  'tenants.staff.personPlaceholder': 'Choose a person',
  'tenants.staff.roleCount': '{{count}} selected',
  'tenants.staff.rolesLabel': 'Roles in {{name}}',
  'tenants.staff.rolesRequired': 'Select at least one role.',
  'tenants.staff.rolesSaved': 'Roles updated for {{person}}',
  'tenants.staff.searchPeople': 'Search by name or username',
};
//#endif
//#endif
