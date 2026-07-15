import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { MessageService } from 'primeng/api';
import { catchError, throwError } from 'rxjs';

import { SILENT_AUTH } from './http-context-tokens';
//#if (IncludeIdentity)
import { AuthService } from '../services/auth-service';
//#endif

/**
 * HTTP 错误拦截器
 *
 * 职责：
 * - 捕获所有 HTTP 请求的错误
 * - 显示用户友好的错误提示
 * - 处理 401 未授权跳转
 * - 重新抛出错误给调用方处理
 */
export const httpErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const messageService = inject(MessageService);
  const router = inject(Router);
//#if (IncludeIdentity)
  const authService = inject(AuthService);
//#endif
//#if (IncludeLocalization)
  const transloco = inject(TranslocoService);
  // 纯客户端兜底文案（后端不可达/无消息体时）；后端可达时优先用其已本地化的 message。
  const codeMessage = (status: number): string | undefined => {
    const value = transloco.translate(`httpError.${status}`);
    return value === `httpError.${status}` ? undefined : value;
  };
  const requestErrorSummary = () => transloco.translate('common.requestError');
//#else
  const CODE_MESSAGES: Record<number, string> = {
    400: 'The request was malformed; the server did not create or modify data.',
    401: 'Authentication failed. Please sign in again.',
    403: 'Insufficient permissions to access this resource.',
    404: 'The requested record does not exist; the server took no action.',
    406: 'The requested format is not available.',
    410: 'The requested resource has been permanently deleted.',
    422: 'A validation error occurred while creating the object.',
    500: 'A server error occurred. Please check the server.',
    502: 'Bad gateway.',
    503: 'Service unavailable; the server is temporarily overloaded or under maintenance.',
    504: 'Gateway timeout.'
  };
  const codeMessage = (status: number): string | undefined => CODE_MESSAGES[status];
  const requestErrorSummary = () => 'Request error';
//#endif

  return next(req).pipe(
    catchError((error: unknown) => {
      // 只处理 HTTP 错误
      if (error instanceof HttpErrorResponse) {
        // 静默请求：不显示错误提示，不处理 401 跳转
        if (req.context.get(SILENT_AUTH)) {
          return throwError(() => error);
        }

        console.error('HTTP Error Interceptor caught error:', error.url, error.status, error.message);

        const contentType = error.headers?.get('Content-Type');

        // 支持 application/json 和 application/problem+json (RFC 7807)
        if ((contentType?.includes('application/json') || contentType?.includes('application/problem+json')) && error.error) {
          const code = error.error.code || '';
          // 优先使用后端返回的 message（启用多语言时后端已按 Accept-Language 本地化）
          const message = error.error.message || error.error.detail;

          if (message) {
            messageService.add({
              severity: 'error',
              summary: `${requestErrorSummary()}（${error.status} - ${code}）`,
              detail: message
            });
          } else {
            messageService.add({
              severity: 'error',
              summary: `${requestErrorSummary()}（${error.status}）`,
              detail: codeMessage(error.status)
            });
          }
        } else {
          const errorText = codeMessage(error.status) || error.statusText;
          messageService.add({
            severity: 'error',
            summary: `${requestErrorSummary()}（${error.status}）`,
            detail: errorText
          });
        }

//#if (IncludeIdentity)
        // 处理 401 未授权情况：保留当前 URL 作为 returnUrl
        if (error.status === 401) {
          authService.clearAuthData();
          const currentReturnUrl =
            router.getCurrentNavigation()?.finalUrl?.queryParamMap.get('returnUrl') ??
            new URL(window.location.href).searchParams.get('returnUrl');
          const currentPath = window.location.pathname + window.location.search;
          const returnUrl = currentReturnUrl || (currentPath.startsWith('/auth/') ? undefined : currentPath);
          router.navigate(['/auth/login'], {
            queryParams: returnUrl ? { returnUrl } : undefined
          });
        }
//#endif

        // 重新抛出错误，避免下游（如 lastValueFrom）收不到数据直接 complete 导致 EmptyError
        return throwError(() => error);
      }

      // 非 HTTP 错误，继续传播到 GlobalErrorHandler
      throw error;
    })
  );
};
