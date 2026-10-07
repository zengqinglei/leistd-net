import { HttpInterceptorFn } from '@angular/common/http';

import { environment } from '../environments/environment';
//#if (IncludeLocalization)
import { acceptLanguageInterceptor } from './core/interceptors/accept-language-interceptor';
//#endif
import { httpErrorInterceptor } from './core/interceptors/http-error-interceptor';
//#if (LocalIdentity && IncludeMultiTenancy)
import { tenantInterceptor } from './core/interceptors/tenant-interceptor';
//#endif
import { urlFormatInterceptor } from './core/interceptors/url-format-interceptor';
import { mockInterceptors } from '../../_mock/core/providers';

/** 应用的 HTTP 拦截器链，按顺序执行；单独导出供启动用例走同一条链。 */
export const appInterceptors: HttpInterceptorFn[] = [
  //#if (IncludeLocalization)
  acceptLanguageInterceptor, // 注入 Accept-Language，须在 URL 改写等之前
  //#endif
  //#if (LocalIdentity && IncludeMultiTenancy)
  tenantInterceptor, // 已选租户时为 /api/ 请求附加租户提示头
  //#endif
  urlFormatInterceptor,
  httpErrorInterceptor, // 认证处置，并把错误归一化为 ApplicationHttpError
  ...mockInterceptors(environment.useMock),
];
