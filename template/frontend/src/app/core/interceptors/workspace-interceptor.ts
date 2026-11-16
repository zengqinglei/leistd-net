//#if (TenantWorkspace)
import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';

import { WORKSPACE_SCOPE } from './http-context-tokens';
import { API_ERROR_CODES } from '../errors/api-error-codes';
import { ApplicationHttpError } from '../errors/application-http-error';
import { WorkspaceContextService } from '../services/workspace-context-service';

export const workspaceInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.includes('/api/')) return next(request);
  const own = /\/(?:auth|external-auth)(?:\/|$)|\/connect\//.test(request.url);
  if (own) return next(request);
  const workspace = inject(WorkspaceContextService);
  const scope = request.context.get(WORKSPACE_SCOPE);
  const name = scope === undefined ? workspace.requestScope() : scope;
  const scoped =
    name && !own && request.url.includes('/api/')
      ? request.clone({ setHeaders: { 'X-Workspace': name } })
      : request;
  return next(scoped).pipe(
    catchError((error: unknown) => {
      if (
        error instanceof ApplicationHttpError &&
        error.code === API_ERROR_CODES.workspaceUnavailable &&
        name === workspace.current() &&
        name === workspace.target()
      ) {
        void workspace.recoverUnavailable().catch(() => undefined);
      }
      return throwError(() => error);
    }),
  );
};
//#endif
