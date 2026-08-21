import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { lastValueFrom } from 'rxjs';

import { AuthService } from '../../../../core/services/auth-service';
//#if (LocalAuthorization)
import { AuthorizationService } from '../../../../core/services/authorization-service';
//#endif
import { AccountService } from '../../services/account-service';

/**
 * 外部登录回调组件
 *
 * 处理 GitHub/Google 等第三方登录重定向回来后的流程:
 * 1. 从 URL query params 中获取 code、state、provider
 * 2. 将 code+state 发送到后端换取 Cookie session
 * 3. 加载用户信息并根据角色跳转
 */
@Component({
  selector: 'app-external-auth-callback',
  imports: [HlmSpinner],
  template: `
    <main class="flex min-h-screen items-center justify-center bg-background px-4">
      <section class="text-center">
        @if (error()) {
          <h1 class="mb-3 text-2xl font-semibold text-destructive">{{ failedTitle() }}</h1>
          <p class="text-muted-foreground">{{ error() }}</p>
        } @else {
          <hlm-spinner class="text-4xl" [attr.aria-label]="processingAria()" />
          <h1 class="mt-4 text-2xl font-semibold text-foreground">
            {{ processingTitle() }}
          </h1>
        }
      </section>
    </main>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ExternalAuthCallback implements OnInit {
  private authService = inject(AuthService);
  //#if (LocalAuthorization)
  private readonly authorizationService = inject(AuthorizationService);
  //#endif
  private accountService = inject(AccountService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif

  protected readonly error = signal<string | null>(null);

  // 内联模板里的条件文案：.ts 的模板字符串区不支持 HTML 注释式条件指令，改用 getter 承载。
  //#if (IncludeLocalization)
  protected readonly failedTitle = () => this.transloco.translate('account.login.loginFailed');
  protected readonly processingAria = () =>
    this.transloco.translate('account.externalCallback.processingAria');
  protected readonly processingTitle = () =>
    this.transloco.translate('account.externalCallback.processing');
  //#else
  protected readonly failedTitle = () => 'Sign-in failed';
  protected readonly processingAria = () => 'Completing sign-in';
  protected readonly processingTitle = () => 'Processing third-party sign-in';
  //#endif

  ngOnInit(): void {
    this.processCallback();
  }

  private async processCallback(): Promise<void> {
    const params = this.route.snapshot.queryParamMap;
    const code = params.get('code');
    const state = params.get('state');
    const provider = params.get('provider') ?? this.route.snapshot.paramMap.get('provider');

    if (!code || !provider) {
      //#if (IncludeLocalization)
      this.error.set(this.transloco.translate('account.externalCallback.missingParams'));
      //#else
      this.error.set('Missing required callback parameters');
      //#endif
      return;
    }

    try {
      // 1. 将 code+state 发送到后端建立 Cookie session
      await lastValueFrom(
        this.accountService.externalLoginCallback(provider, { provider, code, state: state ?? '' }),
      );

      // 2. 加载用户信息并按权限跳转
      await lastValueFrom(this.authService.loadUser());
      //#if (LocalAuthorization)
      await lastValueFrom(this.authorizationService.load());
      if (this.authorizationService.canAccessPlatform()) {
        this.router.navigate(['/platform']);
      } else {
        this.router.navigate(['/workspace']);
      }
      //#else
      this.router.navigate(['/workspace']);
      //#endif
    } catch (err) {
      console.error('External login callback processing failed', err);
      //#if (IncludeLocalization)
      this.error.set(this.transloco.translate('account.externalCallback.failed'));
      //#else
      this.error.set('Third-party sign-in failed. Please go back and try again.');
      //#endif
    }
  }
}
