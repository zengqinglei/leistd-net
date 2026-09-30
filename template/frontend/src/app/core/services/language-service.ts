import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import {
  computed,
  EnvironmentProviders,
  ErrorHandler,
  inject,
  Injectable,
  PLATFORM_ID,
  provideAppInitializer,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  provideTranslocoFallbackStrategy,
  TranslocoFallbackStrategy,
  TranslocoService,
} from '@jsverse/transloco';
import { filter, take } from 'rxjs';

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
 * 而启动流会等账户语言的词条到达并激活（见 {@link applyAccountLang} 的返回值）。
 *
 * **切换先加载词条、成功后才激活。** 结构指令与翻译信号不依赖这一点，但一次性文案（请求回调里的提示、
 * 确认框）用同步 `translate()`，它要求活动语言的词条已加载：若先激活，切换途中返回的请求会取到裸键，
 * 且不会再更新。加载期间界面停在原语言，失败时保持原语言并上报；连续切换只让最后一次生效。
 *
 * **Transloco 的活动语言只由本服务决定。** 它自带两处会自行激活语言的逻辑，都绕过上面的约束：
 * 加载失败时转去加载回落语言并激活——由 {@link provideLanguageFallbackStrategy} 关掉；
 * 有过失败之后，下一次任何语言加载成功都会被它设为活动语言，哪怕那次加载已被取代——由构造函数里的订阅改回。
 * 否则本服务、Transloco、`<html lang>` 会各执一词，同步 `translate()` 取到裸键。
 */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  static readonly STORAGE_KEY = 'app_lang';

  private readonly platformId = inject(PLATFORM_ID);
  private readonly transloco = inject(TranslocoService);
  private readonly document = inject(DOCUMENT);
  private readonly errorHandler = inject(ErrorHandler);

  private readonly activeLangState = signal<Lang>(this.loadDeviceLang());

  /** 在途的切换：目标语言与它的完成信号。相同目标的重复请求复用它。 */
  private pending: { lang: Lang; done: Promise<void> } | undefined;
  /** 每次发起切换加一；加载完成时编号已变，说明被后发起的切换取代，不再激活。 */
  private generation = 0;

  readonly activeLang = this.activeLangState.asReadonly();
  readonly options = LANG_OPTIONS;
  readonly currentMeta = computed(
    () => LANG_OPTIONS.find((o) => o.id === this.activeLang()) ?? LANG_OPTIONS[0],
  );

  /** 初始语言的词条落定；取不到时已退回默认语言。首帧前由 {@link provideLanguageInitializer} 等它。 */
  readonly initialized: Promise<void>;

  constructor() {
    // Transloco 的这次自行激活发生在加载管道里、先于本订阅（它在自己的构造函数里订阅），同一次事件里就改回
    this.transloco.events$
      .pipe(
        filter((event) => event.type === 'translationLoadSuccess' && event.wasFailure),
        takeUntilDestroyed(),
      )
      .subscribe(() => {
        if (this.transloco.getActiveLang() !== this.activeLang()) {
          this.transloco.setActiveLang(this.activeLang());
        }
      });

    // 初始语言没有「原语言」可停留，先激活；首帧由初始化器挡到词条落定，取不到时退回默认语言（见 switchTo）。
    const initial = this.activeLang();
    this.applyLang(initial);
    this.initialized = this.switchTo(initial);
  }

  /**
   * 切到指定语言，并记为本设备偏好。
   *
   * 只有「未登录时的显式选择」走这里：已登录的选择属于账户，走
   * {@link applyAccountLang} 并由切换器写回设置。
   */
  setDeviceLang(lang: Lang): Promise<void> {
    if (isPlatformBrowser(this.platformId)) {
      localStorage.setItem(LanguageService.STORAGE_KEY, lang);
    }
    return this.setActive(lang);
  }

  /** 应用账户设置里的语言，只改内存不落盘（理由见类注释）。 */
  applyAccountLang(lang: Lang): Promise<void> {
    return this.setActive(lang);
  }

  /** 回到本设备偏好：主体离开、或新主体的设置没加载上来时用。 */
  resetToDeviceLang(): Promise<void> {
    return this.setActive(this.loadDeviceLang());
  }

  /**
   * 加载目标语言的词条，成功后激活（理由见类注释）；返回的 Promise 在词条落定时完成，从不 reject。
   *
   * 相同目标正在加载时复用那次的 Promise，不提前完成。目标就是已激活的语言时也走一遍加载，
   * 不按"已激活"短路：它可能是加载失败后仍停在的初始语言，短路就再也不会重新请求；
   * 已加载的取 Transloco 缓存，同时取代在途的切换（用户切走又切回）。
   */
  private setActive(lang: Lang): Promise<void> {
    if (this.pending?.lang === lang) {
      return this.pending.done;
    }
    return this.switchTo(lang);
  }

  /**
   * 加载 `lang`，成功则激活；已被后发起的切换取代时什么都不做。
   *
   * 失败时停在原语言。原语言就是这次没加载上的语言时（初始语言，或在它加载途中切走又切回）
   * 没有词条可停留，退回默认语言；这一步同样受取代约束。
   */
  private switchTo(lang: Lang): Promise<void> {
    const generation = ++this.generation;
    const done = this.load(lang).then((loaded) => {
      if (generation !== this.generation) {
        return undefined;
      }
      this.pending = undefined;
      if (loaded) {
        this.activeLangState.set(lang);
        this.applyLang(lang);
        return undefined;
      }
      return lang === this.activeLang() && lang !== DEFAULT_LANG
        ? this.setActive(DEFAULT_LANG)
        : undefined;
    });
    this.pending = { lang, done };
    return done;
  }

  /**
   * 加载 `lang` 的词条，返回是否成功；失败交给全局错误处理，从不 reject。
   *
   * 失败的加载留在 Transloco 缓存里，但再次订阅会重新请求（它的共享加载在出错后重置），
   * 所以之后再切到同一语言会重新加载，而不是直接拿到上次的失败。
   */
  private load(lang: Lang): Promise<boolean> {
    return new Promise((resolve) => {
      let loaded = false;
      this.transloco
        .load(lang)
        .pipe(take(1))
        .subscribe({
          next: () => (loaded = true),
          error: (error: unknown) => {
            this.errorHandler.handleError(error);
            resolve(false);
          },
          // 不发射就结束只在应用销毁时出现，无需上报
          complete: () => resolve(loaded),
        });
    });
  }

  private applyLang(lang: Lang): void {
    // 已是该语言就不再设置：Transloco 的语言流对同值也会再发一次，结构指令会白白重建一轮上下文
    if (this.transloco.getActiveLang() !== lang) {
      this.transloco.setActiveLang(lang);
    }

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
 * 首帧前等初始语言的词条落定。
 *
 * 结构指令与翻译信号不靠它也不会留下裸键，这里等的是另外两类读者：根组件的启动页不在结构指令里，
 * 词条未到时只能显示空白；渲染后立即发出的一次性提示（如退出模拟后的提示）用同步 `translate()`，
 * 词条未到就取到裸键。取不到时已退回默认语言，这里不 reject——不然应用整个起不来，连启动失败页都没有。
 */
export function provideLanguageInitializer(): EnvironmentProviders {
  return provideAppInitializer(() => inject(LanguageService).initialized);
}

class NoAutomaticFallback implements TranslocoFallbackStrategy {
  getNextLangs(): string[] {
    return [];
  }
}

/**
 * 词条加载失败时 Transloco 不自行回落：默认策略会转去加载回落语言并激活它，不管这次加载是否已被取代。
 * 失败改由 {@link LanguageService} 处理（停在原语言；初始语言失败时显式退回默认语言）。
 *
 * 只管加载失败：缺词条时取回落语言文案（`missingHandler.useFallbackTranslation`）读的是配置里的 `fallbackLang`，
 * 不经过这里。应用与单测装配都要登记，否则测不到应用里的失败路径。
 */
export function provideLanguageFallbackStrategy(): EnvironmentProviders {
  return provideTranslocoFallbackStrategy(NoAutomaticFallback);
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
