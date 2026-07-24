import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import { computed, effect, inject, Injectable, PLATFORM_ID, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';

export const SUPPORTED_LANGS = ['en', 'zh-CN'] as const;
export type Lang = (typeof SUPPORTED_LANGS)[number];

/** 默认语言：英语。 */
export const DEFAULT_LANG: Lang = 'en';

interface LangMeta {
  id: Lang;
  /** 该语言自身的本地名（下拉项文字），不依赖当前 culture。 */
  label: string;
  /** 触发按钮上的短码（如 EN / 中）。 */
  short: string;
}

/** 语言选择器展示元数据。 */
export const LANG_OPTIONS: LangMeta[] = [
  { id: 'en', label: 'English', short: 'EN' },
  { id: 'zh-CN', label: '中文', short: '中' },
];

/**
 * 语言服务：管理活动语言，驱动 Transloco 文案并持久化到 localStorage。
 *
 * 形态镜像 ThemeService：signal 状态 + effect 持久化 + isPlatformBrowser 守卫（SSR 安全）。
 */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  static readonly STORAGE_KEY = 'app_lang';

  private readonly platformId = inject(PLATFORM_ID);
  private readonly transloco = inject(TranslocoService);
  private readonly document = inject(DOCUMENT);

  private readonly activeLangState = signal<Lang>(this.loadLang());

  readonly activeLang = this.activeLangState.asReadonly();
  readonly options = LANG_OPTIONS;
  readonly currentMeta = computed(
    () => LANG_OPTIONS.find((o) => o.id === this.activeLang()) ?? LANG_OPTIONS[0],
  );

  constructor() {
    // 初次即应用一次，并在语言变化时持久化
    this.applyLang(this.activeLang());

    effect(() => {
      const lang = this.activeLang();
      if (isPlatformBrowser(this.platformId)) {
        localStorage.setItem(LanguageService.STORAGE_KEY, lang);
      }
    });
  }

  setActiveLang(lang: Lang): void {
    if (lang === this.activeLang()) {
      return;
    }
    this.activeLangState.set(lang);
    this.applyLang(lang);
  }

  private applyLang(lang: Lang): void {
    this.transloco.setActiveLang(lang);

    // <html lang> 同步：利于可访问性、SEO 与浏览器（拼写检查/字体回退）。
    this.document.documentElement.lang = lang;
  }

  private loadLang(): Lang {
    if (!isPlatformBrowser(this.platformId)) {
      return DEFAULT_LANG;
    }

    const stored = localStorage.getItem(LanguageService.STORAGE_KEY);
    return SUPPORTED_LANGS.includes(stored as Lang) ? (stored as Lang) : DEFAULT_LANG;
  }
}
