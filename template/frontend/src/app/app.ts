import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideRefreshCw } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmToaster } from '@spartan-ng/helm/sonner';
import { HlmSpinner } from '@spartan-ng/helm/spinner';

import { StartupService } from './core/services/startup-service';
import { ThemeService } from './core/services/theme-service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, HlmToaster, HlmSpinner, HlmButton, NgIcon, ...HlmCardImports],
  providers: [provideIcons({ lucideRefreshCw })],
  template: `
    <!-- 全局 toast 宿主 -->
    <hlm-toaster />

    @switch (startupService.status()) {
      @case ('success') {
        <router-outlet></router-outlet>
      }
      @case ('loading') {
        <div class="h-screen w-full flex items-center justify-center bg-background">
          <hlm-spinner class="text-4xl" [attr.aria-label]="loadingLabel()" />
        </div>
      }
      @case ('failed') {
        <div class="h-screen w-full flex items-center justify-center bg-background">
          @if (startupService.error(); as error) {
            <section hlmCard class="w-[360px] text-center">
              <div hlmCardHeader>
                <h3 hlmCardTitle>{{ failedHeader() }}</h3>
              </div>
              <div hlmCardContent>
                <p>{{ formatHttpError(error) }}</p>
              </div>
              <div hlmCardFooter class="justify-center">
                <button hlmBtn (click)="onRetryClick()" [disabled]="isRetrying()">
                  @if (isRetrying()) {
                    <hlm-spinner class="text-base" data-icon="inline-start" />
                  } @else {
                    <ng-icon name="lucideRefreshCw" data-icon="inline-start" />
                  }
                  {{ retryLabel() }}
                </button>
              </div>
            </section>
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
