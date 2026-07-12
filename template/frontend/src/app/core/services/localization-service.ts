import { DOCUMENT } from '@angular/common';
import { inject, Injectable, LOCALE_ID, signal } from '@angular/core';
import { PrimeNG } from 'primeng/config';

import { PRIMENG_ZH_CN } from '../../localization/primeng.zh-CN';

export type SupportedLocale = 'en-US' | 'zh-CN';

export interface LocaleOption {
  code: SupportedLocale;
  label: string;
}

const DEFAULT_LOCALE: SupportedLocale = 'en-US';
const CULTURE_COOKIE = '.AspNetCore.Culture';
const LOCALE_STORAGE_KEY = 'app_locale';

@Injectable({ providedIn: 'root' })
export class LocalizationService {
  private readonly document = inject(DOCUMENT);
  private readonly primeNG = inject(PrimeNG);
  private readonly angularLocale = inject(LOCALE_ID);

  readonly locales: readonly LocaleOption[] = [
    { code: 'en-US', label: 'English' },
    { code: 'zh-CN', label: '简体中文' }
  ];
  readonly currentLocale = signal<SupportedLocale>(this.normalizeLocale(this.angularLocale));

  initialize(): void {
    const locale = this.currentLocale();
    this.document.documentElement.lang = locale;
    this.persistBackendCulture(locale);

    if (locale === 'zh-CN') {
      this.primeNG.setTranslation(PRIMENG_ZH_CN);
    }
  }

  switchLocale(locale: SupportedLocale): void {
    if (locale === this.currentLocale()) {
      return;
    }

    localStorage.setItem(LOCALE_STORAGE_KEY, locale);
    this.persistBackendCulture(locale);

    const currentUrl = new URL(window.location.href);
    const localePrefix = /^\/(en-US|zh-CN)(?=\/|$)/;
    const pathWithoutLocale = currentUrl.pathname.replace(localePrefix, '') || '/';
    currentUrl.pathname = `/${locale}${pathWithoutLocale}`;
    window.location.assign(currentUrl.toString());
  }

  private persistBackendCulture(locale: SupportedLocale): void {
    const value = encodeURIComponent(`c=${locale}|uic=${locale}`);
    this.document.cookie = `${CULTURE_COOKIE}=${value};path=/;max-age=31536000;samesite=lax`;
  }

  private normalizeLocale(locale: string): SupportedLocale {
    return locale.toLowerCase().startsWith('zh') ? 'zh-CN' : DEFAULT_LOCALE;
  }
}
