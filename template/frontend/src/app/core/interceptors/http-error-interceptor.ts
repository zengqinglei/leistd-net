import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
//#if (LocalIdentity)
import { inject } from '@angular/core';
import { Router } from '@angular/router';
//#endif
import { catchError, throwError } from 'rxjs';

//#if (LocalIdentity)
import { SILENT_AUTH } from './http-context-tokens';
//#endif
import { ApplicationHttpError } from '../errors/application-http-error';
//#if (LocalIdentity)
import { AuthService } from '../services/auth-service';
//#endif
//#if (LocalIdentity)
import { TenantContextService } from '../services/tenant-context-service';
//#endif

export const httpErrorInterceptor: HttpInterceptorFn = (req, next) => {
  //#if (LocalIdentity)
  const router = inject(Router);
  const authService = inject(AuthService);
  //#endif
  //#if (LocalIdentity)
  const tenantContext = inject(TenantContextService);
  //#endif
  return next(req).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      //#if (LocalIdentity)
      if (error.status === 401) {
        // 租户失效与请求是否静默无关：这个头说的是"会话所属租户已经没了"，
        // 而 SILENT_AUTH 只表达"别为这次后台请求打断用户"。静默请求（/auth/me、
        // /permissions/current）同样会撞上失效租户，不清的话它会留到登录页再次被拒。
        // 反过来普通会话过期不带这个头，租户选择必须留着，否则每次超时都要重选
        if (error.headers.get('X-Tenant-Invalid')) {
          tenantContext.clear();
        }

        if (!req.context.get(SILENT_AUTH)) {
          authService.clearAuthData();
          const currentPath = router.url;
          const returnUrl = currentPath.startsWith('/auth/') ? undefined : currentPath;
          void router.navigate(['/auth/login'], {
            queryParams: returnUrl ? { returnUrl } : undefined,
          });
        }
      }

      //#else
      // This build has no authentication state to clear.
      //#endif
      return throwError(() => ApplicationHttpError.from(error));
    }),
  );
};
