import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';

import { LocalizationService } from '../services/localization-service';

export const acceptLanguageInterceptor: HttpInterceptorFn = (request, next) => {
  const locale = inject(LocalizationService).currentLocale();
  return next(request.clone({ setHeaders: { 'Accept-Language': locale } }));
};
