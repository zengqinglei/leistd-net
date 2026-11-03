import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

import { TruncatedTooltip } from '../../directives/truncated-tooltip';

/**
 * 托盘卡：灰底外框放标题与右侧操作（投影带 `statPanelAction` 属性的元素），内层白卡放内容。
 * 标题截断时悬停或聚焦提示全文，调用方不必为避免截断缩短标题。
 * 仪表盘这类同屏多块数据用它分组；普通页面区块仍用 Spartan Card。圆角按嵌套规则取：外框
 * `rounded-xl`，内卡 `rounded-lg`（外圆角减去 4px 间距）。
 */
@Component({
  selector: 'app-stat-panel',
  standalone: true,
  imports: [TruncatedTooltip, ...HlmTooltipImports],
  host: { class: 'bg-muted flex min-w-0 flex-col gap-1 rounded-xl p-1', 'data-slot': 'stat-panel' },
  template: `
    <div class="flex min-h-8 items-center gap-2 px-3 py-1">
      <!-- 标题是标签：单行省略，截断时才提示全文并成为 Tab 停靠点 -->
      <h3
        #titleText
        #titleTip="appTruncatedTooltip"
        class="text-muted-foreground focus-visible:ring-ring/50 truncate rounded-sm text-sm font-medium outline-none focus-visible:ring-[3px]"
        [hlmTooltip]="panelTitle()"
        [appTruncatedTooltip]="titleText"
        [attr.tabindex]="titleTip.truncated() ? 0 : null"
      >
        {{ panelTitle() }}
      </h3>
      <div class="ms-auto flex items-center gap-2">
        <ng-content select="[statPanelAction]" />
      </div>
    </div>
    <div class="bg-card text-card-foreground ring-foreground/10 flex-1 rounded-lg shadow-xs ring-1">
      <ng-content />
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatPanel {
  /** 外框标题。 */
  readonly panelTitle = input.required<string>();
}
