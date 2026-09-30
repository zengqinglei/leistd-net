import { HttpInterceptorFn } from '@angular/common/http';
//#if (RemoteTokenAuth)
import { authInterceptor } from 'angular-auth-oidc-client';
//#endif

import { environment } from '../environments/environment';
//#if (IncludeLocalization)
import { acceptLanguageInterceptor } from './core/interceptors/accept-language-interceptor';
//#endif
import { httpErrorInterceptor } from './core/interceptors/http-error-interceptor';
//#if (LocalIdentity)
import { tenantInterceptor } from './core/interceptors/tenant-interceptor';
//#endif
import { urlFormatInterceptor } from './core/interceptors/url-format-interceptor';
import { mockInterceptors } from '../../_mock/core/providers';

/**
 * 应用的 HTTP 拦截器链，按顺序执行。
 *
 * 单独导出是为了让启动用例走与应用相同的整条链：拦截器在请求时才注入依赖，
 * 手抄一份会漏掉后加的那个，构造期循环依赖之类的问题就测不到。
 */
export const appInterceptors: HttpInterceptorFn[] = [
  //#if (IncludeLocalization)
  acceptLanguageInterceptor, // 注入 Accept-Language，须在 URL 改写等之前
  //#endif
  //#if (LocalIdentity)
  tenantInterceptor, // 已选租户时为 /api/ 请求附加租户提示头
  //#endif
  //#if (RemoteTokenAuth)
  authInterceptor(),
  //#endif
  urlFormatInterceptor,
  httpErrorInterceptor, // 捕获所有 HTTP 错误并显示用户提示
  ...mockInterceptors(environment.useMock),
];
