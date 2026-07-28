//#if (IncludeLocalization)
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  OnInit,
  signal,
} from '@angular/core';
//#else
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  OnInit,
  signal,
} from '@angular/core';
//#endif
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCopy, lucidePlus, lucideRefreshCw, lucideSearch } from '@ng-icons/lucide';
import { BrnDialogState } from '@spartan-ng/brain/dialog';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
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
import { FilterStateService } from '../../../../shared/services/filter-state-service';
import {
  CreateOpenApplicationInputDto,
  OpenApplicationClientType,
  OpenApplicationOutputDto,
  OpenApplicationType,
  UpdateOpenApplicationInputDto,
} from '../../models/open-application.dto';
import { OpenApplicationService } from '../../services/open-application-service';
import { OpenApplicationEditDialog } from './widgets/open-application-edit-dialog/open-application-edit-dialog';
import {
  OpenApplicationTable,
  OpenApplicationTableFilterEvent,
} from './widgets/open-application-table/open-application-table';

@Component({
  selector: 'app-open-applications',
  imports: [
    FormsModule,
    NgIcon,
    HlmButton,
    HlmInputGroup,
    HlmInputGroupInput,
    HlmInputGroupAddon,
    ...HlmSelectImports,
    ...HlmTooltipImports,
    ...HlmDialogImports,
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
    OpenApplicationTable,
    OpenApplicationEditDialog,
  ],
  providers: [provideIcons({ lucideCopy, lucidePlus, lucideRefreshCw, lucideSearch })],
  templateUrl: './open-applications.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OpenApplications implements OnInit {
  private readonly service = inject(OpenApplicationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly confirmService = inject(ConfirmService);
  private readonly layoutService = inject(LayoutService);
  private readonly filterStateService = inject(FilterStateService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif

  private readonly FILTER_KEY = 'open-applications';
  private readonly searchSubject = new Subject<string>();

  applications = signal<OpenApplicationOutputDto[]>([]);
  totalRecords = signal(0);
  loading = signal(false);

  editDialogVisible = signal(false);
  editDialogLoading = signal(false);
  editDialogSaving = signal(false);
  selectedApplication = signal<OpenApplicationOutputDto | null>(null);

  resetSecretDialogVisible = signal(false);
  resetSecretValue = signal('');

  createdSecretDialogVisible = signal(false);
  createdSecretValue = signal('');

  searchQuery = signal('');
  selectedApplicationType = signal<OpenApplicationType | null>(null);
  selectedClientType = signal<OpenApplicationClientType | null>(null);

  offset = signal(0);
  limit = signal(10);
  sorting = signal('clientId asc');

  //#if (IncludeLocalization)
  // 追踪「翻译就绪」：资源加载完成与语言切换时重算，含首帧避免裸键。
  private readonly translationReady = translationReady(this.transloco);

  // 读取 translationReady 建立依赖：资源就绪 / 语言切换时本 computed 重算，选项标签重新翻译。
  readonly applicationTypeOptions = computed(() => {
    this.translationReady();
    return [
      { label: this.transloco.translate('openApp.appType.web'), value: 'web' as const },
      { label: this.transloco.translate('openApp.appType.native'), value: 'native' as const },
      { label: this.transloco.translate('openApp.appType.service'), value: 'service' as const },
    ];
  });

  readonly clientTypeOptions = computed(() => {
    this.translationReady();
    return [
      {
        label: this.transloco.translate('openApp.clientType.publicLabel'),
        value: 'public' as const,
      },
      {
        label: this.transloco.translate('openApp.clientType.confidentialLabel'),
        value: 'confidential' as const,
      },
    ];
  });
  //#else
  readonly applicationTypeOptions = computed(() => [
    { label: 'Web', value: 'web' as const },
    { label: 'Desktop/Native', value: 'native' as const },
    { label: 'Service', value: 'service' as const },
  ]);

  readonly clientTypeOptions = computed(() => [
    { label: 'Public', value: 'public' as const },
    { label: 'Confidential', value: 'confidential' as const },
  ]);
  //#endif

  //#if (IncludeLocalization)
  readonly allAppTypesPlaceholder = () => this.transloco.translate('openApp.filter.allAppTypes');
  readonly allClientTypesPlaceholder = () =>
    this.transloco.translate('openApp.filter.allClientTypes');
  readonly searchPlaceholder = () => this.transloco.translate('openApp.filter.searchPlaceholder');
  readonly refreshLabel = () => this.transloco.translate('common.refresh');
  readonly createLabel = () => this.transloco.translate('openApp.action.create');
  readonly resetSecretHeader = () => this.transloco.translate('openApp.secret.resetHeader');
  readonly createdSecretHeader = () => this.transloco.translate('openApp.secret.createdHeader');
  //#else
  readonly allAppTypesPlaceholder = () => 'All application types';
  readonly allClientTypesPlaceholder = () => 'All client types';
  readonly searchPlaceholder = () => 'Search by name / Client ID...';
  readonly refreshLabel = () => 'Refresh';
  readonly createLabel = () => 'New Open Application';
  readonly resetSecretHeader = () => 'Client Secret reset';
  readonly createdSecretHeader = () => 'Client secret';
  //#endif

  constructor() {
    this.searchSubject
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.onFilter());

    //#if (IncludeLocalization)
    // 读取 translationReady 建立依赖：资源就绪 / 语言切换时重设标题，随语言更新。
    effect(() => {
      this.translationReady();
      this.layoutService.title.set(this.transloco.translate('openApp.page.title'));
    });
    //#else
    this.layoutService.title.set('Open Applications');
    //#endif
  }

  ngOnInit() {
    const saved = this.filterStateService.load<{
      searchQuery: string;
      selectedApplicationType: OpenApplicationType | null;
      selectedClientType: OpenApplicationClientType | null;
    }>(this.FILTER_KEY);

    if (saved.searchQuery) this.searchQuery.set(saved.searchQuery);
    if (saved.selectedApplicationType !== undefined)
      this.selectedApplicationType.set(saved.selectedApplicationType ?? null);
    if (saved.selectedClientType !== undefined)
      this.selectedClientType.set(saved.selectedClientType ?? null);

    // 首次进入即加载列表（恢复筛选后），无需手动点刷新。
    this.reloadList();
  }

  reloadList() {
    this.loading.set(true);
    this.service
      .getOpenApplications({
        keyword: this.searchQuery(),
        applicationType: this.selectedApplicationType() ?? undefined,
        clientType: this.selectedClientType() ?? undefined,
        offset: this.offset(),
        limit: this.limit(),
        sorting: this.sorting(),
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe((data) => {
        this.applications.set(data.items);
        this.totalRecords.set(data.totalCount);
      });
  }

  onSearchQueryChange(value: string) {
    this.searchQuery.set(value);
    this.searchSubject.next(value);
  }

  onApplicationTypeChange(value: OpenApplicationType | null | undefined) {
    this.selectedApplicationType.set(value ?? null);
    this.onFilter();
  }

  onClientTypeChange(value: OpenApplicationClientType | null | undefined) {
    this.selectedClientType.set(value ?? null);
    this.onFilter();
  }

  onFilter() {
    this.offset.set(0);
    this.filterStateService.save(this.FILTER_KEY, {
      searchQuery: this.searchQuery(),
      selectedApplicationType: this.selectedApplicationType(),
      selectedClientType: this.selectedClientType(),
    });
    this.reloadList();
  }

  onPageChange(event: OpenApplicationTableFilterEvent) {
    this.offset.set(event.offset);
    this.limit.set(event.limit);
    if (event.sorting) this.sorting.set(event.sorting);
    this.reloadList();
  }

  openAddDialog() {
    this.selectedApplication.set(null);
    this.editDialogVisible.set(true);
  }

  openEditDialog(id: string) {
    this.selectedApplication.set(null);
    this.editDialogVisible.set(true);
    this.editDialogLoading.set(true);

    this.service
      .getOpenApplication(id)
      .pipe(finalize(() => this.editDialogLoading.set(false)))
      .subscribe((application) => this.selectedApplication.set(application));
  }

  handleSave(data: CreateOpenApplicationInputDto | UpdateOpenApplicationInputDto) {
    this.editDialogSaving.set(true);
    const selected = this.selectedApplication();

    const request = selected
      ? this.service.updateOpenApplication(selected.id, data as UpdateOpenApplicationInputDto)
      : this.service.createOpenApplication(data as CreateOpenApplicationInputDto);

    request
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.editDialogSaving.set(false)),
      )
      .subscribe({
        next: (result) => {
          //#if (IncludeLocalization)
          notify.success(this.transloco.translate('common.success'), {
            detail: this.transloco.translate(
              selected ? 'openApp.toast.updated' : 'openApp.toast.created',
            ),
          });
          //#else
          notify.success('Success', {
            detail: selected
              ? 'Open application updated successfully'
              : 'Open application created successfully',
          });
          //#endif
          this.editDialogVisible.set(false);

          // 创建 Confidential 客户端后显示自动生成的 Secret
          if (!selected && result.clientSecret) {
            this.createdSecretValue.set(result.clientSecret);
            this.createdSecretDialogVisible.set(true);
          }

          this.reloadList();
        },
      });
  }

  async handleDelete(id: string) {
    const confirmed = await this.confirmService.open({
      //#if (IncludeLocalization)
      message: this.transloco.translate('openApp.confirm.deleteMessage'),
      header: this.transloco.translate('openApp.confirm.deleteHeader'),
      confirmText: this.transloco.translate('common.ok'),
      cancelText: this.transloco.translate('common.cancel'),
      //#else
      message:
        'Are you sure you want to delete this open application? Clients using this Client ID will no longer be able to sign in.',
      header: 'Confirm deletion',
      //#endif
      variant: 'destructive',
    });
    if (!confirmed) {
      return;
    }

    this.service.deleteOpenApplication(id).subscribe(() => {
      //#if (IncludeLocalization)
      notify.success(this.transloco.translate('common.success'), {
        detail: this.transloco.translate('openApp.toast.deleted'),
      });
      //#else
      notify.success('Success', { detail: 'Open application deleted' });
      //#endif
      this.reloadList();
    });
  }

  async handleResetSecret(id: string) {
    const confirmed = await this.confirmService.open({
      //#if (IncludeLocalization)
      message: this.transloco.translate('openApp.confirm.resetSecretMessage'),
      header: this.transloco.translate('openApp.confirm.resetSecretHeader'),
      confirmText: this.transloco.translate('common.ok'),
      cancelText: this.transloco.translate('common.cancel'),
      //#else
      message:
        "Are you sure you want to reset this open application's Client Secret? The old secret will be invalidated immediately.",
      header: 'Confirm secret reset',
      //#endif
      variant: 'destructive',
    });
    if (!confirmed) {
      return;
    }

    this.service.resetSecret(id).subscribe((result) => {
      this.resetSecretValue.set(result.clientSecret);
      this.resetSecretDialogVisible.set(true);
      this.reloadList();
    });
  }

  /** 桥接 hlm-dialog 声明式 state 到重置密钥弹窗可见性。 */
  onResetSecretDialogStateChange(state: BrnDialogState): void {
    this.resetSecretDialogVisible.set(state === 'open');
  }

  /** 桥接 hlm-dialog 声明式 state 到新建密钥弹窗可见性。 */
  onCreatedSecretDialogStateChange(state: BrnDialogState): void {
    this.createdSecretDialogVisible.set(state === 'open');
  }

  copyResetSecret() {
    const value = this.resetSecretValue();
    if (!value) {
      return;
    }

    navigator.clipboard?.writeText(value).then(() => {
      //#if (IncludeLocalization)
      notify.success(this.transloco.translate('common.success'), {
        detail: this.transloco.translate('openApp.toast.secretCopied'),
      });
      //#else
      notify.success('Success', { detail: 'Secret copied' });
      //#endif
    });
  }

  copyCreatedSecret() {
    const value = this.createdSecretValue();
    if (!value) {
      return;
    }

    navigator.clipboard?.writeText(value).then(() => {
      //#if (IncludeLocalization)
      notify.success(this.transloco.translate('common.success'), {
        detail: this.transloco.translate('openApp.toast.secretCopied'),
      });
      //#else
      notify.success('Success', { detail: 'Secret copied' });
      //#endif
    });
  }
}
