import { CommonModule } from '@angular/common';
//#if (IncludeLocalization)
import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#else
import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
//#endif
import { ButtonModule } from 'primeng/button';
import { Popover, PopoverModule } from 'primeng/popover';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';

import { OpenApplicationOutputDto } from '../../../../models/open-application.dto';

export interface OpenApplicationTableFilterEvent {
  offset: number;
  limit: number;
  sorting?: string;
}

type PopoverMode = 'permissions' | 'redirectUris';

@Component({
  selector: 'app-open-application-table',
  //#if (IncludeLocalization)
  imports: [CommonModule, TableModule, ButtonModule, TagModule, TooltipModule, PopoverModule, TranslocoModule],
  //#else
  imports: [CommonModule, TableModule, ButtonModule, TagModule, TooltipModule, PopoverModule],
  //#endif
  templateUrl: './open-application-table.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class OpenApplicationTable {
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  readonly currentPageReportTemplate = () => this.transloco.translate('openApp.table.currentPageReport');
  readonly editTooltip = () => this.transloco.translate('common.edit');
  readonly resetSecretTooltip = () => this.transloco.translate('openApp.action.resetSecret');
  readonly deleteTooltip = () => this.transloco.translate('common.delete');
  //#else
  readonly currentPageReportTemplate = () => '{totalRecords} total';
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

  first = 0;
  rows = 20;
  sortField = signal('clientId');
  sortOrder = signal(1);
  activeItems = signal<string[]>([]);
  popoverMode = signal<PopoverMode>('permissions');

  popoverTitle = computed(() => {
    switch (this.popoverMode()) {
      case 'redirectUris':
        return 'Redirect URIs';
      default:
        //#if (IncludeLocalization)
        return this.transloco.translate('openApp.section.authorization');
        //#else
        return 'Authorization capabilities';
        //#endif
    }
  });

  onPage(event: TableLazyLoadEvent) {
    this.first = event.first ?? 0;
    this.rows = event.rows ?? 20;
    if (event.sortField) {
      this.sortField.set(Array.isArray(event.sortField) ? event.sortField[0] : event.sortField);
      this.sortOrder.set(event.sortOrder ?? 1);
    }
    this.filterChange.emit({
      offset: this.first,
      limit: this.rows,
      sorting: `${this.sortField()} ${this.sortOrder() === 1 ? 'asc' : 'desc'}`
    });
  }

  openPopover(event: Event, popover: Popover, mode: PopoverMode, items: string[]) {
    this.popoverMode.set(mode);
    this.activeItems.set(items);
    popover.toggle(event);
  }

  getApplicationTypeLabel(value: string) {
    //#if (IncludeLocalization)
    const labels: Record<string, string> = {
      web: 'Web',
      native: this.transloco.translate('openApp.appType.native'),
      service: this.transloco.translate('openApp.appType.service')
    };
    //#else
    const labels: Record<string, string> = {
      web: 'Web',
      native: 'Desktop/Native',
      service: 'Service'
    };
    //#endif
    return labels[value] ?? value;
  }

  getClientTypeSeverity(value: string): 'success' | 'info' | 'warn' | 'secondary' {
    return value === 'public' ? 'success' : 'warn';
  }

  getConsentTypeLabel(value: string) {
    //#if (IncludeLocalization)
    const labels: Record<string, string> = {
      implicit: this.transloco.translate('openApp.consentType.implicit'),
      explicit: this.transloco.translate('openApp.consentType.explicit'),
      external: this.transloco.translate('openApp.consentType.external'),
      systematic: this.transloco.translate('openApp.consentType.systematic')
    };
    //#else
    const labels: Record<string, string> = {
      implicit: 'Implicit consent',
      explicit: 'Explicit consent',
      external: 'External consent',
      systematic: 'Systematic consent'
    };
    //#endif
    return labels[value] ?? value;
  }

  getPermissionSummary(item: OpenApplicationOutputDto) {
    const grants = item.permissions.filter(permission => permission.startsWith('gt:')).map(permission => permission.replace('gt:', ''));
    //#if (IncludeLocalization)
    return grants.length ? grants.join(' / ') : this.transloco.translate('openApp.permission.notConfigured');
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
