import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSend } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { finalize } from 'rxjs/operators';

import { applicationErrorMessage } from '../../../core/errors/application-http-error';
//#if (IncludeLocalization)
import { translationReady } from '../../../core/i18n/translation-ready';
//#endif
import { AuthService } from '../../../core/services/auth-service';
import { SettingService } from '../../../core/settings/setting-service';

/**
 * 「邮件发送」面板底部：用当前生效的参数发一封测试邮件。
 *
 * 失败时把服务端给出的原因原样显示（连不上、认证失败、发件地址被拒），管理员据此改参数。
 */
@Component({
  selector: 'app-email-test',
  imports: [NgIcon, HlmButton, HlmInput, HlmSpinner],
  providers: [provideIcons({ lucideSend })],
  template: `
    <section
      class="border-border flex flex-col gap-3 rounded-lg border p-4"
      data-testid="email-test"
    >
      <div class="flex flex-col gap-1">
        <h4 class="text-sm font-medium">{{ t('settings.emailTest.title') }}</h4>
        <p class="text-muted-foreground text-xs">{{ t('settings.emailTest.description') }}</p>
      </div>
      <form
        class="flex flex-col gap-2 sm:flex-row"
        novalidate
        (submit)="$event.preventDefault(); send()"
      >
        <input
          hlmInput
          type="email"
          class="sm:max-w-sm sm:flex-1"
          data-testid="email-test-to"
          [attr.aria-label]="t('settings.emailTest.recipient')"
          [placeholder]="t('settings.emailTest.recipient')"
          [value]="to()"
          (input)="to.set($any($event.target).value)"
        />
        <button hlmBtn type="submit" data-testid="email-test-send" [disabled]="!canSend()">
          @if (sending()) {
            <hlm-spinner class="text-base" data-icon="inline-start" />
          } @else {
            <ng-icon name="lucideSend" data-icon="inline-start" />
          }
          {{ t('settings.emailTest.send') }}
        </button>
      </form>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmailTest {
  private readonly settingService = inject(SettingService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  private readonly translationReady = translationReady(this.transloco);

  protected readonly t = (key: string, params?: Record<string, unknown>) => {
    this.translationReady();
    return this.transloco.translate(key, params);
  };
  //#else
  protected readonly t = (key: string, params?: Record<string, unknown>) =>
    ENGLISH[key]?.replace(/\{\{(\w+)\}\}/g, (_, name: string) => String(params?.[name] ?? '')) ??
    key;
  //#endif

  /** 默认发给自己：最常见的用法就是"发一封给我看看收不收得到"。 */
  protected readonly to = signal(inject(AuthService).currentUser()?.email ?? '');
  protected readonly sending = signal(false);
  protected readonly canSend = computed(() => /^\S+@\S+\.\S+$/.test(this.to()) && !this.sending());

  protected send(): void {
    if (!this.canSend()) {
      return;
    }

    this.sending.set(true);
    this.settingService
      .sendTestEmail(this.to())
      .pipe(finalize(() => this.sending.set(false)))
      .subscribe({
        next: () => toast.success(this.t('settings.emailTest.sent', { to: this.to() })),
        // 服务端的原因里已经带着"测试邮件发送失败"，标题不再重复一遍
        error: (error) =>
          toast.error(this.t('common.requestError'), {
            description: applicationErrorMessage(error),
          }),
      });
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 的 `settings.emailTest` 同步。 */
const ENGLISH: Record<string, string> = {
  'settings.emailTest.title': 'Send a test email',
  'settings.emailTest.description':
    'Uses the settings above as they are now. Save your changes first.',
  'settings.emailTest.recipient': 'Recipient',
  'settings.emailTest.send': 'Send test email',
  'settings.emailTest.sent': 'Test email sent to {{to}}',
  'common.requestError': 'Request failed',
};
//#endif
