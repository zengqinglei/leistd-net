import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
//#if (IncludeIdentity)
import { inject } from '@angular/core';
import { Router } from '@angular/router';
//#endif
import { catchError, throwError } from 'rxjs';

//#if (IncludeIdentity)
import { SILENT_AUTH } from './http-context-tokens';
//#endif
import { ApplicationHttpError } from '../errors/application-http-error';
//#if (IncludeIdentity)
import { AuthService } from '../services/auth-service';
//#endif
//#if (TenancyEnabled)
import { TenantContextService } from '../services/tenant-context-service';
//#endif

export const httpErrorInterceptor: HttpInterceptorFn = (req, next) => {
  //#if (IncludeIdentity)
  const router = inject(Router);
  const authService = inject(AuthService);
  //#endif
  //#if (TenancyEnabled)
  const tenantContext = inject(TenantContextService);
  //#endif
  return next(req).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      //#if (IncludeIdentity)
      if (error.status === 401 && !req.context.get(SILENT_AUTH)) {
        authService.clearAuthData();
        //#if (TenancyEnabled)
        // 只有"会话所属租户已不可用"才清租户选择：普通会话过期清掉的话，
        // 用户每次超时都要重选租户；而租户真的失效时不清，登录页会带着这个
        // 已死的租户再次被拒——服务端用这个头把两者区分开
        if (error.headers.get('X-Tenant-Invalid')) {
          tenantContext.clear();
        }
        //#endif
        const currentPath = router.url;
        const returnUrl = currentPath.startsWith('/auth/') ? undefined : currentPath;
        void router.navigate(['/auth/login'], {
          queryParams: returnUrl ? { returnUrl } : undefined,
        });
      }

      //#else
      // This build has no authentication state to clear.
      //#endif
      return throwError(() => ApplicationHttpError.from(error));
    }),
  );
};
