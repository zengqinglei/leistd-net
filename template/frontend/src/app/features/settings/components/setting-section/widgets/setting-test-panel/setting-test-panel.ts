import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronDown, lucideChevronRight } from '@ng-icons/lucide';
import { HlmItemImports } from '@spartan-ng/helm/item';

let nextId = 0;

/**
 * 设置分组底部的测试区：测的是一组配置，所以放在配置块下方，不挂在某个输入框上。带边框的 Item，
 * 左边图标、标题与说明，右边是投影进来的输入与按钮（不需要输入时一个按钮，一项输入时与按钮同行）；
 * 面板窄时（手机）输入与按钮各占满一行。
 *
 * 多项输入开 `expandable`：默认收起为一行，整行是展开按钮（`aria-expanded`，右侧箭头），
 * 点开后投影内容在同一个容器里展开。
 *
 * 只管布局与展开：请求、等写入落定与结果提示由使用方负责。图标名由使用方给出并注册。
 */
@Component({
  selector: 'app-setting-test-panel',
  imports: [NgIcon, NgTemplateOutlet, ...HlmItemImports],
  providers: [provideIcons({ lucideChevronDown, lucideChevronRight })],
  templateUrl: './setting-test-panel.html',
  // 自身是尺寸容器：横排还是竖排按测试区自己的宽度定，放进对话框或窄面板里同样成立
  host: { class: '@container/test-panel block' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SettingTestPanel {
  /** 图标名（如 `lucideSend`），由使用方注册。 */
  readonly icon = input.required<string>();
  /** 标题；不叫 `title`，免得静态写法在宿主上留下原生悬停提示。 */
  readonly heading = input.required<string>();
  readonly description = input('');
  /** 多项输入：收起为一行，点开在容器内显示投影内容。 */
  readonly expandable = input(false);
  /** 展开状态；使用方可以双向绑定，例如提交成功后收起。 */
  readonly expanded = model(false);

  private readonly id = `setting-test-panel-${nextId++}`;
  protected readonly titleId = `${this.id}-title`;
  protected readonly bodyId = `${this.id}-body`;

  protected toggle(): void {
    this.expanded.update((expanded) => !expanded);
  }
}
