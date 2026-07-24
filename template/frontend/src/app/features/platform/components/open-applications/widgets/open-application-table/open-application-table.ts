import { DatePipe } from '@angular/common';
//#if (IncludeLocalization)
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#else
import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowDown,
  lucideArrowUp,
  lucideArrowUpDown,
  lucideChevronLeft,
  lucideChevronRight,
  lucideInbox,
  lucidePencil,
  lucideRefreshCw,
  lucideShield,
  lucideTrash2,
} from '@ng-icons/lucide';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

import { OpenApplicationOutputDto } from '../../../../models/open-application.dto';

export interface OpenApplicationTableFilterEvent {
  offset: number;
  limit: number;
  sorting?: string;
}

type PopoverMode = 'permissions' | 'redirectUris';

/** Spartan badge 变体（替代 PrimeNG severity）。 */
type BadgeVariant = 'default' | 'secondary' | 'destructive' | 'outline';

@Component({
  selector: 'app-open-application-table',
  imports: [
    DatePipe,
    NgIcon,
    HlmBadge,
    HlmButton,
    ...HlmTableImports,
    ...HlmPopoverImports,
    ...HlmTooltipImports,
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
  ],
  providers: [
    provideIcons({
      lucideArrowDown,
      lucideArrowUp,
      lucideArrowUpDown,
      lucideChevronLeft,
      lucideChevronRight,
      lucideInbox,
      lucidePencil,
      lucideRefreshCw,
      lucideShield,
      lucideTrash2,
    }),
  ],
  templateUrl: './open-application-table.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OpenApplicationTable {
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  readonly currentPageReport = () => this.transloco.translate('openApp.table.currentPageReport');
  readonly editTooltip = () => this.transloco.translate('common.edit');
  readonly resetSecretTooltip = () => this.transloco.translate('openApp.action.resetSecret');
  readonly deleteTooltip = () => this.transloco.translate('common.delete');
  //#else
  readonly currentPageReport = () => '{totalRecords} total';
  readonly editTooltip = () => 'Edit';
  readonly resetSecretTooltip = () => 'Reset secret';
  readonly deleteTooltip = () => 'Delete';
  //#endif

  applications = input.required<OpenApplicationOutputDto[]>();
  totalRecords = input.required<number>();
  loading = input<boolean>(false);

  readonly edit = output<string>();
  readonly delete = output<string>();
  readonly resetSecret = output<string>();
  readonly filterChange = output<OpenApplicationTableFilterEvent>();

  readonly rowsPerPageOptions = [10, 20, 50, 100];
  readonly first = signal(0);
  readonly rows = signal(20);
  sortField = signal('clientId');
  sortOrder = signal(1);
  activeItems = signal<string[]>([]);
  popoverMode = signal<PopoverMode>('permissions');
  readonly popoverOpen = signal<'open' | 'closed'>('closed');

  // 可排序列
  readonly sortableColumns = ['clientId', 'creationTime'] as const;

  // 分页派生
  readonly currentPage = computed(() => Math.floor(this.first() / this.rows()) + 1);
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalRecords() / this.rows())));
  readonly canPrev = computed(() => this.first() > 0);
  readonly canNext = computed(() => this.currentPage() < this.totalPages());

  //#if (IncludeLocalization)
  popoverTitle = computed(() => {
    switch (this.popoverMode()) {
      case 'redirectUris':
        return 'Redirect URIs';
      default:
        return this.transloco.translate('openApp.section.authorization');
    }
  });
  //#else
  popoverTitle = computed(() => {
    switch (this.popoverMode()) {
      case 'redirectUris':
        return 'Redirect URIs';
      default:
        return 'Authorization capabilities';
    }
  });
  //#endif

  private emitFilter() {
    this.filterChange.emit({
      offset: this.first(),
      limit: this.rows(),
      sorting: `${this.sortField()} ${this.sortOrder() === 1 ? 'asc' : 'desc'}`,
    });
  }

  /** 点击可排序列头：同列切换升/降序，异列切到该列升序。 */
  onSort(field: string) {
    if (this.sortField() === field) {
      this.sortOrder.set(this.sortOrder() === 1 ? -1 : 1);
    } else {
      this.sortField.set(field);
      this.sortOrder.set(1);
    }
    this.first.set(0);
    this.emitFilter();
  }

  /** 排序图标名（当前列升/降，其它列中性）。 */
  sortIcon(field: string): string {
    if (this.sortField() !== field) {
      return 'lucideArrowUpDown';
    }
    return this.sortOrder() === 1 ? 'lucideArrowUp' : 'lucideArrowDown';
  }

  prevPage() {
    if (!this.canPrev()) return;
    this.first.set(Math.max(0, this.first() - this.rows()));
    this.emitFilter();
  }

  nextPage() {
    if (!this.canNext()) return;
    this.first.set(this.first() + this.rows());
    this.emitFilter();
  }

  onRowsChange(rows: number) {
    this.rows.set(rows);
    this.first.set(0);
    this.emitFilter();
  }

  openPopover(mode: PopoverMode, items: string[]) {
    this.popoverMode.set(mode);
    this.activeItems.set(items);
    this.popoverOpen.set('open');
  }

  getApplicationTypeLabel(value: string) {
    //#if (IncludeLocalization)
    const labels: Record<string, string> = {
      web: 'Web',
      native: this.transloco.translate('openApp.appType.native'),
      service: this.transloco.translate('openApp.appType.service'),
    };
    //#else
    const labels: Record<string, string> = {
      web: 'Web',
      native: 'Desktop/Native',
      service: 'Service',
    };
    //#endif
    return labels[value] ?? value;
  }

  getClientTypeVariant(value: string): BadgeVariant {
    return value === 'public' ? 'default' : 'destructive';
  }

  getConsentTypeLabel(value: string) {
    //#if (IncludeLocalization)
    const labels: Record<string, string> = {
      implicit: this.transloco.translate('openApp.consentType.implicit'),
      explicit: this.transloco.translate('openApp.consentType.explicit'),
      external: this.transloco.translate('openApp.consentType.external'),
      systematic: this.transloco.translate('openApp.consentType.systematic'),
    };
    //#else
    const labels: Record<string, string> = {
      implicit: 'Implicit consent',
      explicit: 'Explicit consent',
      external: 'External consent',
      systematic: 'Systematic consent',
    };
    //#endif
    return labels[value] ?? value;
  }

  getPermissionSummary(item: OpenApplicationOutputDto) {
    const grants = item.permissions
      .filter((permission) => permission.startsWith('gt:'))
      .map((permission) => permission.replace('gt:', ''));
    //#if (IncludeLocalization)
    return grants.length
      ? grants.join(' / ')
      : this.transloco.translate('openApp.permission.notConfigured');
    //#else
    return grants.length ? grants.join(' / ') : 'Not configured';
    //#endif
  }

  getVisibleRedirectUris(item: OpenApplicationOutputDto) {
    return item.redirectUris.slice(0, 1);
  }

  getHiddenRedirectUris(item: OpenApplicationOutputDto) {
    return item.redirectUris.slice(1);
  }

  hasPkce(item: OpenApplicationOutputDto) {
    return item.requirements.includes('ft:pkce');
  }
}
