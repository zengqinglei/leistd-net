import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideMonitor, lucideMoon, lucideSun } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';

//#if (!IncludeLocalization)
import { englishText } from '../../../shared/utils/english-text';
//#endif
import { ThemeService } from '../../services/theme-service';

/**
 * 主题模式切换按钮：单击在 亮 → 跟随系统 → 暗 三态间循环，图标反映当前模式。
 *
 * 按 Spartan 主题体系（CSS 变量），不提供运行时换主色/表面色；
 * 品牌定制由项目改 styles.css 的 CSS 变量完成。
 * aria-label 经结构指令的 t 取文案，与相邻的 language-switcher 一致。
 */
@Component({
  selector: 'app-theme-mode-toggle',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [provideIcons({ lucideSun, lucideMonitor, lucideMoon })],
  // prettier-ignore
  imports: [
    HlmButton, NgIcon,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  templateUrl: './theme-mode-toggle.html',
})
export class ThemeModeToggle {
  readonly themeService = inject(ThemeService);
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'theme.current': 'Theme: {{mode}}',
  'theme.mode.light': 'Light',
  'theme.mode.system': 'System',
  'theme.mode.dark': 'Dark',
};
//#endif
