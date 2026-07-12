import { isPlatformBrowser } from '@angular/common';
import { afterNextRender, DestroyRef, PLATFORM_ID, computed, effect, inject, Injectable, signal } from '@angular/core';
import { palette, updatePrimaryPalette, updateSurfacePalette, usePreset } from '@primeuix/themes';
import Aura from '@primeuix/themes/aura';
import Lara from '@primeuix/themes/lara';
import Material from '@primeuix/themes/material';
import Nora from '@primeuix/themes/nora';
import type { PaletteDesignToken } from '@primeuix/themes/types';

export const THEME_MODES = ['system', 'light', 'dark'] as const;
export const THEME_PRESET_NAMES = ['Aura', 'Material', 'Lara', 'Nora'] as const;
export const THEME_PRIMARY_NAMES = [
  'emerald',
  'green',
  'lime',
  'red',
  'orange',
  'amber',
  'yellow',
  'teal',
  'cyan',
  'sky',
  'blue',
  'indigo',
  'violet',
  'purple',
  'fuchsia',
  'pink',
  'rose'
] as const;
export const THEME_SURFACE_NAMES = ['slate', 'gray', 'zinc', 'neutral', 'stone'] as const;

export type ThemeMode = (typeof THEME_MODES)[number];
export type ThemePresetName = (typeof THEME_PRESET_NAMES)[number];
export type ThemePrimaryName = (typeof THEME_PRIMARY_NAMES)[number];
export type ThemeSurfaceName = (typeof THEME_SURFACE_NAMES)[number];

export const THEME_PRESETS = { Aura, Material, Lara, Nora } as const satisfies Record<ThemePresetName, unknown>;

export interface ThemePreferences {
  mode: ThemeMode;
  preset: ThemePresetName;
  primary: ThemePrimaryName | null;
  surface: ThemeSurfaceName | null;
}

const DEFAULT_THEME_PREFERENCES: ThemePreferences = {
  mode: 'system',
  preset: 'Aura',
  primary: null,
  surface: null
};

function getPalette(name: ThemePrimaryName | ThemeSurfaceName): PaletteDesignToken {
  return palette(`{${name}}`) as PaletteDesignToken;
}

@Injectable({ providedIn: 'root' })
export class ThemeService {
  static readonly STORAGE_KEY = 'theme_config';

  private readonly platformId = inject(PLATFORM_ID);
  private readonly destroyRef = inject(DestroyRef);
  private readonly systemDark = signal(false);

  private readonly preferencesState = signal<ThemePreferences>(this.loadPreferences());

  readonly preferences = this.preferencesState.asReadonly();
  readonly mode = computed(() => this.preferences().mode);
  readonly modeIcon = computed(() => {
    switch (this.mode()) {
      case 'light':
        return 'pi pi-sun';
      case 'dark':
        return 'pi pi-moon';
      default:
        return 'pi pi-desktop';
    }
  });
  readonly isDarkTheme = computed(() => {
    const mode = this.mode();
    return mode === 'dark' || (mode === 'system' && this.systemDark());
  });

  constructor() {
    this.watchSystemTheme();
    afterNextRender(() => this.applyThemePreferences(this.preferences()));

    effect(() => {
      const preferences = this.preferences();
      const isDark = this.isDarkTheme();

      if (!isPlatformBrowser(this.platformId)) {
        return;
      }

      document.documentElement.classList.toggle('dark', isDark);
      localStorage.setItem(ThemeService.STORAGE_KEY, JSON.stringify(preferences));
    });
  }

  toggleTheme(): void {
    const modes: ThemeMode[] = ['light', 'system', 'dark'];
    const nextMode = modes[(modes.indexOf(this.mode()) + 1) % modes.length];

    if (isPlatformBrowser(this.platformId) && document.startViewTransition) {
      document.startViewTransition(() => this.setMode(nextMode));
      return;
    }

    this.setMode(nextMode);
  }

  setMode(mode: ThemeMode): void {
    this.preferencesState.update(preferences => ({ ...preferences, mode }));
  }

  updatePreferences(preferences: Partial<ThemePreferences>): void {
    this.preferencesState.update(current => ({ ...current, ...preferences }));
    this.applyThemePreferences(this.preferences());
  }

  private applyThemePreferences(preferences: ThemePreferences): void {
    if (!isPlatformBrowser(this.platformId)) {
      return;
    }

    usePreset(THEME_PRESETS[preferences.preset]);

    if (preferences.primary) {
      updatePrimaryPalette(getPalette(preferences.primary));
    }
    if (preferences.surface) {
      updateSurfacePalette(getPalette(preferences.surface));
    }
  }

  private loadPreferences(): ThemePreferences {
    if (!isPlatformBrowser(this.platformId)) {
      return DEFAULT_THEME_PREFERENCES;
    }

    const storedPreferences = localStorage.getItem(ThemeService.STORAGE_KEY);
    if (!storedPreferences) {
      return DEFAULT_THEME_PREFERENCES;
    }

    try {
      const parsed = JSON.parse(storedPreferences) as Partial<ThemePreferences>;
      return {
        mode: THEME_MODES.includes(parsed.mode as ThemeMode) ? (parsed.mode as ThemeMode) : DEFAULT_THEME_PREFERENCES.mode,
        preset: THEME_PRESET_NAMES.includes(parsed.preset as ThemePresetName)
          ? (parsed.preset as ThemePresetName)
          : DEFAULT_THEME_PREFERENCES.preset,
        primary:
          parsed.primary === null || THEME_PRIMARY_NAMES.includes(parsed.primary as ThemePrimaryName)
            ? (parsed.primary ?? null)
            : DEFAULT_THEME_PREFERENCES.primary,
        surface:
          parsed.surface === null || THEME_SURFACE_NAMES.includes(parsed.surface as ThemeSurfaceName)
            ? (parsed.surface ?? null)
            : DEFAULT_THEME_PREFERENCES.surface
      };
    } catch {
      localStorage.removeItem(ThemeService.STORAGE_KEY);
      return DEFAULT_THEME_PREFERENCES;
    }
  }

  private watchSystemTheme(): void {
    if (!isPlatformBrowser(this.platformId)) {
      return;
    }

    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
    const updateSystemTheme = (event: MediaQueryListEvent | MediaQueryList) => this.systemDark.set(event.matches);

    updateSystemTheme(mediaQuery);
    mediaQuery.addEventListener('change', updateSystemTheme);
    this.destroyRef.onDestroy(() => mediaQuery.removeEventListener('change', updateSystemTheme));
  }
}
