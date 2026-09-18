import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideShieldCheck } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { lastValueFrom } from 'rxjs';

import { applicationErrorMessage } from '../../../../core/errors/application-http-error';
//#if (IncludeLocalization)
import { translationReady } from '../../../../core/i18n/translation-ready';
//#endif
import { AuthService } from '../../../../core/services/auth-service';
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { SessionContextService } from '../../../../core/services/session-context-service';
import { RecoveryCodes } from '../recovery-codes/recovery-codes';
import { TwoFactorSetup } from '../two-factor-setup/two-factor-setup';

/**
 * 组织要求两步验证而本人尚未启用时的设置页（受限会话只能到这里）。
 *
 * 单独成页而不是带去个人设置：受限会话调不了设置之外的接口，放进主布局的话，
 * 布局自带的通知、菜单等请求会一路报错。启用成功后服务端换发正常会话，这里重建会话上下文再进入应用。
 */
@Component({
  selector: 'app-two-factor-required',
  imports: [NgIcon, HlmButton, RecoveryCodes, TwoFactorSetup],
  providers: [provideIcons({ lucideShieldCheck })],
  template: `
    <div class="bg-background flex min-h-svh justify-center px-4 py-10 sm:items-center">
      <div class="flex w-full max-w-xl flex-col gap-6" data-testid="two-factor-required">
        <div class="flex flex-col gap-2">
          <h1 class="flex items-center gap-2 text-2xl font-semibold">
            <ng-icon name="lucideShieldCheck" class="text-primary" />
            {{ t('account.twoFactorRequired.title') }}
          </h1>
          <p class="text-muted-foreground text-sm">
            {{ t('account.twoFactorRequired.description') }}
          </p>
        </div>

        <div class="border-border rounded-lg border p-4 sm:p-6">
          @if (codes(); as recoveryCodes) {
            <app-recovery-codes [codes]="recoveryCodes" (done)="continue()" />
          } @else {
            <app-two-factor-setup [cancellable]="false" (enabled)="codes.set($event)" />
          }
        </div>

        <button
          hlmBtn
          variant="link"
          size="sm"
          class="text-muted-foreground self-start px-0"
          type="button"
          (click)="logout()"
        >
          {{ t('account.twoFactorRequired.signOut') }}
        </button>
      </div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TwoFactorRequired {
  private readonly authService = inject(AuthService);
  private readonly authorizationService = inject(AuthorizationService);
  private readonly sessionContext = inject(SessionContextService);
  private readonly router = inject(Router);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  private readonly translationReady = translationReady(this.transloco);

  protected readonly t = (key: string) => {
    this.translationReady();
    return this.transloco.translate(key);
  };
  //#else
  protected readonly t = (key: string) => ENGLISH[key] ?? key;
  //#endif

  /** 启用成功后的恢复码；为 null 时还在设置那一步。 */
  protected readonly codes = signal<string[] | null>(null);

  /** 恢复码已保存：用换发后的正常会话建立会话上下文，按权限进入应用。 */
  protected async continue(): Promise<void> {
    try {
      await lastValueFrom(this.authService.loadUser());
      await this.sessionContext.establish();
      await this.router.navigate([
        this.authorizationService.canAccessPlatform() ? '/platform' : '/workspace',
      ]);
    } catch (error) {
      toast.error(this.t('common.requestError'), { description: applicationErrorMessage(error) });
    }
  }

  protected logout(): void {
    this.authService.logout();
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 的 `account.twoFactorRequired` 同步。 */
const ENGLISH: Record<string, string> = {
  'common.requestError': 'Request failed',
  'account.twoFactorRequired.title': 'Turn on two-factor authentication',
  'account.twoFactorRequired.description':
    'Your organization requires two-factor authentication. Set it up to continue.',
  'account.twoFactorRequired.signOut': 'Sign out',
};
//#endif
