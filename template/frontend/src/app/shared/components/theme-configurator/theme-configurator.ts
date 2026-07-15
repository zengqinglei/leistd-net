import { Component, computed, inject } from '@angular/core';
//#if (IncludeLocalization)
import { toSignal } from '@angular/core/rxjs-interop';
//#endif
import { FormsModule } from '@angular/forms';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import type { PaletteDesignToken } from '@primeuix/themes/types';
import { SelectButtonModule } from 'primeng/selectbutton';

import {
  THEME_PRESET_NAMES,
  THEME_PRESETS,
  THEME_PRIMARY_NAMES,
  THEME_SURFACE_NAMES,
  ThemeMode,
  ThemePresetName,
  ThemePrimaryName,
  ThemeService,
  ThemeSurfaceName
} from '../../../core/services/theme-service';

interface PaletteOption<TName extends string> {
  name: TName;
  palette: PaletteDesignToken;
}

@Component({
  selector: 'app-theme-configurator',
  standalone: true,
  imports: [FormsModule, SelectButtonModule],
  template: `
    <div class="flex flex-col gap-4">
      <div class="flex flex-col gap-2">
        <span class="text-sm text-muted-color font-semibold">{{ modeLabel() }}</span>
        <p-selectbutton
          [options]="themeModes()"
          optionLabel="label"
          optionValue="value"
          [ngModel]="themeService.mode()"
          (ngModelChange)="onThemeModeChange($event)"
          [allowEmpty]="false"
        />
      </div>

      <div>
        <span class="text-sm text-muted-color font-semibold">Primary</span>
        <div class="pt-2 flex gap-2 flex-wrap justify-start">
          <button
            type="button"
            [title]="defaultLabel()"
            (click)="onPrimaryChange($event, null)"
            [class.outline-primary]="themeService.preferences().primary === null"
            class="border-none w-5 h-5 rounded-full p-0 cursor-pointer outline-none outline-offset-1"
            style="background-color: var(--p-primary-color)"
          ></button>
          @for (primaryColor of primaryColors(); track primaryColor.name) {
            <button
              type="button"
              [title]="primaryColor.name"
              (click)="onPrimaryChange($event, primaryColor.name)"
              [class.outline-primary]="primaryColor.name === themeService.preferences().primary"
              class="border-none w-5 h-5 rounded-full p-0 cursor-pointer outline-none outline-offset-1"
              [style.background-color]="primaryColor.palette[500]"
            ></button>
          }
        </div>
      </div>

      <div>
        <span class="text-sm text-muted-color font-semibold">Surface</span>
        <div class="pt-2 flex gap-2 flex-wrap justify-start">
          <button
            type="button"
            [title]="defaultLabel()"
            (click)="onSurfaceChange($event, null)"
            [class.outline-primary]="themeService.preferences().surface === null"
            class="border-none w-5 h-5 rounded-full p-0 cursor-pointer outline-none outline-offset-1"
            style="background-color: var(--p-surface-500)"
          ></button>
          @for (surface of surfaces(); track surface.name) {
            <button
              type="button"
              [title]="surface.name"
              (click)="onSurfaceChange($event, surface.name)"
              [class.outline-primary]="surface.name === themeService.preferences().surface"
              class="border-none w-5 h-5 rounded-full p-0 cursor-pointer outline-none outline-offset-1"
              [style.background-color]="surface.palette[500]"
            ></button>
          }
        </div>
      </div>

      <div class="flex flex-col gap-2">
        <span class="text-sm text-muted-color font-semibold">Presets</span>
        <p-selectbutton
          [options]="presetNames"
          [ngModel]="themeService.preferences().preset"
          (ngModelChange)="onPresetChange($event)"
          [allowEmpty]="false"
        />
      </div>
    </div>
  `,
  styles: `
    .outline-primary {
      outline: 2px solid var(--p-primary-color);
      outline-offset: 1px;
    }
  `,
  host: {
    class:
      'hidden absolute top-12 right-0 w-80 max-w-[calc(100vw-2rem)] p-5 bg-surface-0 dark:bg-surface-900 border border-surface rounded-border origin-top shadow-[0px_3px_5px_rgba(0,0,0,0.02),0px_0px_2px_rgba(0,0,0,0.05),0px_1px_4px_rgba(0,0,0,0.08)]'
  }
})
export class ThemeConfigurator {
  readonly themeService = inject(ThemeService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  // 追踪活动语言：切换时该 signal 变化 → 依赖它的 computed 重算，文案随之更新。
  private readonly activeLang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });
  readonly modeLabel = computed(() => {
    this.activeLang();
    return this.transloco.translate('theme.config.mode');
  });
  readonly defaultLabel = computed(() => {
    this.activeLang();
    return this.transloco.translate('theme.config.default');
  });
  //#else
  readonly modeLabel = computed(() => 'Theme Mode');
  readonly defaultLabel = computed(() => 'Default');
  //#endif

  readonly presetNames = [...THEME_PRESET_NAMES];
  //#if (IncludeLocalization)
  readonly themeModes = computed<Array<{ label: string; value: ThemeMode }>>(() => {
    this.activeLang();
    return [
      { label: this.transloco.translate('theme.config.light'), value: 'light' },
      { label: this.transloco.translate('theme.config.system'), value: 'system' },
      { label: this.transloco.translate('theme.config.dark'), value: 'dark' }
    ];
  });
  //#else
  readonly themeModes = computed<Array<{ label: string; value: ThemeMode }>>(() => [
    { label: 'Light', value: 'light' },
    { label: 'System', value: 'system' },
    { label: 'Dark', value: 'dark' }
  ]);
  //#endif

  readonly primaryColors = computed<Array<PaletteOption<ThemePrimaryName>>>(() => {
    const primitive = THEME_PRESETS[this.themeService.preferences().preset].primitive;
    return THEME_PRIMARY_NAMES.map(name => ({ name, palette: (primitive?.[name] ?? {}) as PaletteDesignToken }));
  });

  readonly surfaces = computed<Array<PaletteOption<ThemeSurfaceName>>>(() => {
    const primitive = THEME_PRESETS[this.themeService.preferences().preset].primitive;
    return THEME_SURFACE_NAMES.map(name => ({ name, palette: (primitive?.[name] ?? {}) as PaletteDesignToken }));
  });

  onThemeModeChange(mode: ThemeMode): void {
    this.themeService.setMode(mode);
  }

  onPrimaryChange(event: MouseEvent, primary: ThemePrimaryName | null): void {
    event.stopPropagation();
    this.themeService.updatePreferences({ primary });
  }

  onSurfaceChange(event: MouseEvent, surface: ThemeSurfaceName | null): void {
    event.stopPropagation();
    this.themeService.updatePreferences({ surface });
  }

  onPresetChange(preset: ThemePresetName): void {
    this.themeService.updatePreferences({ preset });
  }
}
