import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { HlmBreadcrumbImports } from '@spartan-ng/helm/breadcrumb';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

import { LayoutService } from '../../../core/services/layout-service';
import { TruncatedTooltip } from '../../../shared/directives/truncated-tooltip';
//#if (!IncludeLocalization)
import { englishText } from '../../../shared/utils/english-text';
//#endif

/**
 * 页面面包屑：「首页 > 上级… > 当前页」，上级取 `LayoutService.trail`，当前页取 `LayoutService.title`。
 * 上级有两级及以上时只留最近一级，更远的收进省略号下拉（Spartan breadcrumb 的折叠用法）；
 * 窄屏只留最近一级上级与当前页；上级与当前页放不下时截断，悬停或聚焦给全文。侧栏布局放在页头，顶栏布局在有上级时放在正文上方。
 */
@Component({
  selector: 'app-page-breadcrumb',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  // prettier-ignore
  imports: [
    RouterLink,
    ...HlmBreadcrumbImports,
    ...HlmDropdownMenuImports,
    ...HlmTooltipImports,
    TruncatedTooltip,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  host: { class: 'flex min-w-0' },
  templateUrl: './page-breadcrumb.html',
})
export class PageBreadcrumb {
  protected readonly layoutService = inject(LayoutService);
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif

  /** 最近一级上级，窄屏也保留，作为返回入口。 */
  protected readonly parent = computed(() => this.layoutService.trail().at(-1));
  /** 收进省略号下拉的更远上级。 */
  protected readonly collapsed = computed(() => this.layoutService.trail().slice(0, -1));
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'layout.breadcrumb.label': 'Breadcrumb',
  'layout.breadcrumb.more': 'More parent pages',
  'menu.platform': 'Admin platform',
  'menu.workspace': 'Workspace',
};
//#endif
