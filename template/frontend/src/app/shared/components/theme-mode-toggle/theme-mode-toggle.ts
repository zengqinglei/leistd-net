import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoModule } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideMonitor, lucideMoon, lucideSun } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';

import { ThemeService } from '../../../core/services/theme-service';

/**
 * 主题模式切换按钮：单击在 亮 → 跟随系统 → 暗 三态间循环，图标反映当前模式。
 *
 * 按 Spartan 主题体系（CSS 变量），不提供运行时换主色/表面色；
 * 品牌定制由项目改 styles.css 的 CSS 变量完成。
 * aria-label 用 transloco 管道（异步安全），与相邻的 language-switcher 一致。
 */
@Component({
  selector: 'app-theme-mode-toggle',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [provideIcons({ lucideSun, lucideMonitor, lucideMoon })],
  //#if (IncludeLocalization)
  imports: [HlmButton, NgIcon, TranslocoModule],
  template: `
    <button
      hlmBtn
      variant="outline"
      size="icon"
      [attr.aria-label]="
        'theme.current' | transloco: { mode: 'theme.mode.' + themeService.mode() | transloco }
      "
      (click)="themeService.toggleTheme()"
    >
      <ng-icon [name]="themeService.modeIcon()" />
    </button>
  `,
  //#else
  imports: [HlmButton, NgIcon],
  template: `
    <button
      hlmBtn
      variant="outline"
      size="icon"
      [attr.aria-label]="'Theme: ' + themeService.mode()"
      (click)="themeService.toggleTheme()"
    >
      <ng-icon [name]="themeService.modeIcon()" />
    </button>
  `,
  //#endif
})
export class ThemeModeToggle {
  readonly themeService = inject(ThemeService);
}
