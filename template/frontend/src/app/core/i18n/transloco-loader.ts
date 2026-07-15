import { APP_BASE_HREF, PlatformLocation } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Translation, TranslocoLoader } from '@jsverse/transloco';

/**
 * 运行时词条加载器：从 {baseHref}i18n/{lang}.json 加载翻译。
 *
 * 词条文件位于 public/ 下（Angular public 资源约定），构建后位于站点 baseHref 下的 i18n/。
 * 用 baseHref 前缀而非绝对 /i18n/，以便应用部署在子路径（如 /app/）时仍能正确取词条。
 */
@Injectable({ providedIn: 'root' })
export class TranslocoHttpLoader implements TranslocoLoader {
  private readonly http = inject(HttpClient);

  // APP_BASE_HREF 未显式提供时回落到 PlatformLocation 解析出的 base，最终归一化为以 / 结尾。
  private readonly baseHref = this.resolveBaseHref();

  getTranslation(lang: string) {
    return this.http.get<Translation>(`${this.baseHref}i18n/${lang}.json`);
  }

  private resolveBaseHref(): string {
    const appBaseHref = inject(APP_BASE_HREF, { optional: true });
    const raw = appBaseHref ?? inject(PlatformLocation).getBaseHrefFromDOM() ?? '/';
    return raw.endsWith('/') ? raw : `${raw}/`;
  }
}
