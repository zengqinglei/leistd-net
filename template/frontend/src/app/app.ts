import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
// 导入所需的 PrimeNG 模块
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { ToastModule } from 'primeng/toast';

import { StartupService } from './core/services/startup-service';
import { ThemeService } from './core/services/theme-service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, ProgressSpinnerModule, CardModule, ButtonModule, ToastModule],
  template: `
    <!-- Toast 消息组件 - 用于显示全局错误和通知 -->
    <p-toast />

    @switch (startupService.status()) {
      @case ('success') {
        <router-outlet></router-outlet>
      }
      @case ('loading') {
        <div
          class="h-screen w-full flex items-center justify-center bg-surface-50 dark:bg-surface-950"
        >
          <p-progressSpinner [ariaLabel]="loadingLabel()"></p-progressSpinner>
        </div>
      }
      @case ('failed') {
        <div
          class="h-screen w-full flex items-center justify-center bg-surface-50 dark:bg-surface-950"
        >
          @if (startupService.error(); as error) {
            <p-card [header]="failedHeader()" [style]="{ width: '360px', textAlign: 'center' }">
              <p>{{ formatHttpError(error) }}</p>
              <ng-template pTemplate="footer">
                <p-button
                  [label]="retryLabel()"
                  icon="pi pi-refresh"
                  (click)="onRetryClick()"
                  [loading]="isRetrying()"
                  [disabled]="isRetrying()"
                >
                </p-button>
              </ng-template>
            </p-card>
          }
        </div>
      }
    }
  `,
})
export class App {
  protected readonly startupService = inject(StartupService);
  // 注入 ThemeService 确保主题在应用启动时生效 (通过构造函数中的 effect)
  protected readonly themeService = inject(ThemeService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  protected readonly loadingLabel = () => this.transloco.translate('app.startup.loading');
  protected readonly failedHeader = () => this.transloco.translate('app.startup.failed');
  protected readonly retryLabel = () => this.transloco.translate('common.retry');
  //#else
  protected readonly loadingLabel = () => 'Loading...';
  protected readonly failedHeader = () => 'Application Failed to Load';
  protected readonly retryLabel = () => 'Retry';
  //#endif

  private _isRetrying = signal(false);
  public readonly isRetrying = this._isRetrying.asReadonly();

  async onRetryClick(): Promise<void> {
    this._isRetrying.set(true);
    await this.startupService.retry();
    this._isRetrying.set(false);
  }

  protected formatHttpError(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.error instanceof ErrorEvent) {
        // 客户端或网络错误
        //#if (IncludeLocalization)
        return this.transloco.translate('app.startup.clientError', {
          message: error.error.message,
        });
        //#else
        return `Client error: ${error.error.message}`;
        //#endif
      } else {
        // 后端返回的错误
        const contentType = error.headers.get('Content-Type');
        if (contentType?.includes('application/json') && error.error?.message) {
          //#if (IncludeLocalization)
          return this.transloco.translate('app.startup.requestFailed', {
            message: error.error.message,
            code: error.error.code,
          });
          //#else
          return `Request failed: ${error.error.message} (code: ${error.error.code})`;
          //#endif
        }
        //#if (IncludeLocalization)
        return this.transloco.translate('app.startup.serverError', {
          status: error.status,
          statusText: error.statusText,
        });
        //#else
        return `Unknown server error: ${error.status} - ${error.statusText}`;
        //#endif
      }
    }

    // 处理非 HttpErrorResponse 的其他未知错误
    if (error instanceof Error) {
      //#if (IncludeLocalization)
      return this.transloco.translate('app.startup.unknownErrorDetail', { message: error.message });
      //#else
      return `An unknown error occurred: ${error.message}`;
      //#endif
    }

    //#if (IncludeLocalization)
    return this.transloco.translate('app.startup.unknownError');
    //#else
    return `An unknown error occurred. Please try again later.`;
    //#endif
  }
}
