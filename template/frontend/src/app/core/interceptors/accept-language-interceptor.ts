import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';

import { LanguageService } from '../services/language-service';

/**
 * 为每个请求注入 Accept-Language 头，使后端错误/校验消息按当前活动语言本地化。
 *
 * 置于拦截器数组首位，确保 header 在 URL 改写等其他拦截器之前挂上。
 *
 * 例外：词条静态资源（/i18n/*.json）本身就是分语言的文件，不需要 Accept-Language；
 * 且这些请求发生在启动早期，若在此注入 LanguageService 会与其构造期的 Transloco 加载
 * 形成重入，导致"Unable to load translation"。故对词条请求直接放行、不注入 LanguageService。
 */
export const acceptLanguageInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.url.includes('/i18n/')) {
    return next(req);
  }

  const languageService = inject(LanguageService);
  return next(req.clone({ setHeaders: { 'Accept-Language': languageService.activeLang() } }));
};
