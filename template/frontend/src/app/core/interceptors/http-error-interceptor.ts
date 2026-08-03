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

export const httpErrorInterceptor: HttpInterceptorFn = (req, next) => {
  //#if (IncludeIdentity)
  const router = inject(Router);
  const authService = inject(AuthService);
  //#endif
  return next(req).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      //#if (IncludeIdentity)
      if (error.status === 401 && !req.context.get(SILENT_AUTH)) {
        authService.clearAuthData();
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
