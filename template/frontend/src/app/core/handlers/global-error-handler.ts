import { HttpErrorResponse } from '@angular/common/http';
import { ErrorHandler, Injectable, inject } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { MessageService } from 'primeng/api';

/**
 * 全局错误处理器
 *
 * 职责：
 * - 捕获所有未被处理的错误（作为最后的兜底）
 * - 处理非 HTTP 错误（JavaScript 运行时错误、Promise rejection 等）
 * - HTTP 错误已由 httpErrorInterceptor 处理，这里会忽略
 *
 * 注意：
 * - HTTP 错误应该已经被 httpErrorInterceptor 拦截并终止传播
 * - 如果 HTTP 错误到达这里，说明拦截器配置有问题
 */
@Injectable()
export class GlobalErrorHandler implements ErrorHandler {
  private readonly messageService = inject(MessageService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif

  handleError(error: unknown): void {
    console.error('Global error caught:', error);

    // HTTP 错误应该已经被 httpErrorInterceptor 处理
    // 如果到达这里，记录警告但不重复显示
    if (error instanceof HttpErrorResponse) {
      console.warn(
        'HTTP error reached GlobalErrorHandler, this should not happen. Check interceptor configuration.',
      );
      return;
    }

    // 处理 JavaScript 运行时错误
    if (error instanceof Error) {
      this.messageService.add({
        severity: 'error',
        //#if (IncludeLocalization)
        summary: this.transloco.translate('common.appError'),
        //#else
        summary: 'Application error',
        //#endif
        detail: error.message,
      });
      return;
    }

    // 处理未知类型的错误
    this.messageService.add({
      severity: 'error',
      //#if (IncludeLocalization)
      summary: this.transloco.translate('common.unknownError'),
      detail: this.transloco.translate('common.unexpectedError'),
      //#else
      summary: 'Unknown error',
      detail: 'An unexpected error occurred. Please refresh the page and try again.',
      //#endif
    });
  }
}
