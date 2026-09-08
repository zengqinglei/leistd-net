import { isPlatformBrowser } from '@angular/common';
import {
  DestroyRef,
  PLATFORM_ID,
  computed,
  effect,
  inject,
  Injectable,
  signal,
} from '@angular/core';

export const THEME_MODES = ['system', 'light', 'dark'] as const;
export type ThemeMode = (typeof THEME_MODES)[number];

export interface ThemePreferences {
  mode: ThemeMode;
}

const DEFAULT_THEME_PREFERENCES: ThemePreferences = {
  mode: 'system',
};

/**
 * 主题服务：管理亮/暗/跟随系统三态，切换 `<html>` 的 `.dark` class 并持久化。
 *
 * Spartan/Tailwind 主题走 CSS 变量（styles.css 的 :root / :root.dark），
 * 不需要运行时换色 API。品牌定制由项目改 CSS 变量完成。
 */
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
        return 'lucideSun';
      case 'dark':
        return 'lucideMoon';
      default:
        return 'lucideMonitor';
    }
  });
  readonly isDarkTheme = computed(() => {
    const mode = this.mode();
    return mode === 'dark' || (mode === 'system' && this.systemDark());
  });

  constructor() {
    this.watchSystemTheme();

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
    this.preferencesState.update((preferences) => ({ ...preferences, mode }));
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
        mode: THEME_MODES.includes(parsed.mode as ThemeMode)
          ? (parsed.mode as ThemeMode)
          : DEFAULT_THEME_PREFERENCES.mode,
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
    const updateSystemTheme = (event: MediaQueryListEvent | MediaQueryList) =>
      this.systemDark.set(event.matches);

    updateSystemTheme(mediaQuery);
    mediaQuery.addEventListener('change', updateSystemTheme);
    this.destroyRef.onDestroy(() => mediaQuery.removeEventListener('change', updateSystemTheme));
  }
}
