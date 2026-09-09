import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import { computed, inject, Injectable, PLATFORM_ID, signal } from '@angular/core';
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
 * 语言服务：管理活动语言并驱动 Transloco 文案。
 *
 * **活动语言有两个来源，本地存储只认其中一个。** 存进 localStorage 的是「这台设备的偏好」
 * ——未登录访客的显式选择，也是主体离开后的回落值。账户设置里的语言只在内存生效：
 * 它属于某个账户，写进设备存储就分不清「这台机器习惯用哪种语言」和「上一个登录的人用哪种」，
 * 于是共享机器上 A 退出后，B 会在登录页看到 A 的语言。
 *
 * 账户语言不落盘也不会有「先英文闪一下再变中文」：外壳只在启动流成功后渲染，
 * 而账户语言在启动流里就应用完了。
 */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  static readonly STORAGE_KEY = 'app_lang';

  private readonly platformId = inject(PLATFORM_ID);
  private readonly transloco = inject(TranslocoService);
  private readonly document = inject(DOCUMENT);

  private readonly activeLangState = signal<Lang>(this.loadDeviceLang());

  readonly activeLang = this.activeLangState.asReadonly();
  readonly options = LANG_OPTIONS;
  readonly currentMeta = computed(
    () => LANG_OPTIONS.find((o) => o.id === this.activeLang()) ?? LANG_OPTIONS[0],
  );

  constructor() {
    this.applyLang(this.activeLang());
  }

  /**
   * 切到指定语言，并记为本设备偏好。
   *
   * 只有「未登录时的显式选择」走这里：已登录的选择属于账户，走
   * {@link applyAccountLang} 并由切换器写回设置。
   */
  setDeviceLang(lang: Lang): void {
    if (isPlatformBrowser(this.platformId)) {
      localStorage.setItem(LanguageService.STORAGE_KEY, lang);
    }
    this.setActive(lang);
  }

  /** 应用账户设置里的语言，只改内存不落盘（理由见类注释）。 */
  applyAccountLang(lang: Lang): void {
    this.setActive(lang);
  }

  /** 回到本设备偏好：主体离开、或新主体的设置没加载上来时用。 */
  resetToDeviceLang(): void {
    this.setActive(this.loadDeviceLang());
  }

  private setActive(lang: Lang): void {
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

  /**
   * 本设备的语言：显式选过的 → 跟随系统 → 回落默认。
   *
   * 中间那一档是关键：没显式选过时按**浏览器语言**走，而不是直接落到 `DEFAULT_LANG`。
   * 直接落默认的话，浏览器是中文的人第一次进来也会看到英文，得自己改一次——
   * 而这份偏好操作系统早就告诉浏览器了。
   */
  private loadDeviceLang(): Lang {
    if (!isPlatformBrowser(this.platformId)) {
      return DEFAULT_LANG;
    }

    let stored: string | null = null;
    try {
      stored = localStorage.getItem(LanguageService.STORAGE_KEY);
    } catch {
      // 隐私模式/配额：读不到就当没选过
    }

    if (SUPPORTED_LANGS.includes(stored as Lang)) {
      return stored as Lang;
    }

    return systemLang() ?? DEFAULT_LANG;
  }
}

/**
 * 浏览器偏好的语言里第一个本端支持的。
 *
 * 按 `navigator.languages` 的偏好顺序逐个匹配：先精确匹配（`zh-CN` → `zh-CN`），
 * 再按主语言子标签匹配（`en-GB` → `en`、`zh-Hans-CN` → `zh-CN`）。只比字符串相等
 * 会让 `en-GB`、`en-US` 这些最常见的取值全部落空。
 *
 * `navigator.languages` 在 Safari（始终）与 Chrome 隐身模式下会被截成一项以降低指纹面，
 * 因此取不到时退回 `navigator.language`（前者的首项，两者同源）。
 */
function systemLang(): Lang | undefined {
  let preferred: readonly string[];
  try {
    preferred = navigator.languages?.length ? navigator.languages : [navigator.language];
  } catch {
    return undefined;
  }

  for (const tag of preferred) {
    if (!tag) {
      continue;
    }

    const exact = SUPPORTED_LANGS.find((lang) => lang.toLowerCase() === tag.toLowerCase());
    if (exact) {
      return exact;
    }

    const primary = primarySubtag(tag);
    const byPrimary = SUPPORTED_LANGS.find((lang) => primarySubtag(lang) === primary);
    if (byPrimary) {
      return byPrimary;
    }
  }

  return undefined;
}

/** 语言标签的主语言子标签（`zh-Hans-CN` → `zh`）；解析不了就取第一段。 */
function primarySubtag(tag: string): string {
  try {
    return new Intl.Locale(tag).language.toLowerCase();
  } catch {
    return tag.split('-')[0]!.toLowerCase();
  }
}
