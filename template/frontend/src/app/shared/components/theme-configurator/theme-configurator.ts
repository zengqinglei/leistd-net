import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideMonitor, lucideMoon, lucideSun } from '@ng-icons/lucide';
import { HlmToggleGroup, HlmToggleGroupItem } from '@spartan-ng/helm/toggle-group';

//#if (IncludeLocalization)
import { translationReady } from '../../../core/i18n/translation-ready';
//#endif
import { ThemeMode, ThemeService } from '../../../core/services/theme-service';

/**
 * 主题配置面板：亮 / 跟随系统 / 暗 三态切换。
 *
 * 按 Spartan 主题体系（CSS 变量），不提供运行时换主色/表面色；
 * 品牌定制由项目改 styles.css 的 CSS 变量完成。
 */
@Component({
  selector: 'app-theme-configurator',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmToggleGroup, HlmToggleGroupItem, NgIcon],
  providers: [provideIcons({ lucideSun, lucideMonitor, lucideMoon })],
  template: `
    <div class="flex flex-col gap-4">
      <div class="flex flex-col gap-2">
        <span class="text-sm text-muted-foreground font-semibold">{{ modeLabel() }}</span>
        <hlm-toggle-group
          type="single"
          variant="outline"
          [value]="themeService.mode()"
          (valueChange)="onThemeModeChange($event)"
          class="justify-start"
        >
          <button hlmToggleGroupItem value="light" [attr.aria-label]="lightLabel()">
            <ng-icon name="lucideSun" data-icon="inline-start" />
            {{ lightLabel() }}
          </button>
          <button hlmToggleGroupItem value="system" [attr.aria-label]="systemLabel()">
            <ng-icon name="lucideMonitor" data-icon="inline-start" />
            {{ systemLabel() }}
          </button>
          <button hlmToggleGroupItem value="dark" [attr.aria-label]="darkLabel()">
            <ng-icon name="lucideMoon" data-icon="inline-start" />
            {{ darkLabel() }}
          </button>
        </hlm-toggle-group>
      </div>
    </div>
  `,
  host: {
    class:
      'hidden absolute top-12 right-0 w-72 max-w-[calc(100vw-2rem)] p-5 bg-background border border-border rounded-md origin-top shadow-lg',
  },
})
export class ThemeConfigurator {
  readonly themeService = inject(ThemeService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  // 追踪「翻译就绪」：资源加载完成与语言切换时该 signal 变化 → 依赖它的 computed 重算（含首帧，避免裸键）。
  private readonly translationReady = translationReady(this.transloco);
  readonly modeLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('theme.config.mode');
  });
  readonly lightLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('theme.config.light');
  });
  readonly systemLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('theme.config.system');
  });
  readonly darkLabel = computed(() => {
    this.translationReady();
    return this.transloco.translate('theme.config.dark');
  });
  //#else
  readonly modeLabel = computed(() => 'Theme Mode');
  readonly lightLabel = computed(() => 'Light');
  readonly systemLabel = computed(() => 'System');
  readonly darkLabel = computed(() => 'Dark');
  //#endif

  onThemeModeChange(mode: string | string[] | null | undefined): void {
    if (typeof mode === 'string' && mode) {
      this.themeService.setMode(mode as ThemeMode);
    }
  }
}
