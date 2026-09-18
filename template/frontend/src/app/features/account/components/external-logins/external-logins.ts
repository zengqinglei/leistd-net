import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideLink } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { lastValueFrom } from 'rxjs';
import { finalize } from 'rxjs/operators';

import { applicationErrorMessage } from '../../../../core/errors/application-http-error';
import { ConfirmService } from '../../../../core/feedback/confirm-service';
//#if (IncludeLocalization)
import { translationReady } from '../../../../core/i18n/translation-ready';
//#endif
import { SettingContextService } from '../../../../core/settings/setting-context-service';
import { formatAppDate } from '../../../../shared/pipes/app-date-pipe';
import { ExternalLoginsOutputDto } from '../../models/account.dto';
import { AccountService } from '../../services/account-service';

/** 外部授权回来时据它判断这是一次"绑定"而不是登录（授权地址只能带 state，带不了别的）。 */
export const EXTERNAL_LINK_PENDING_KEY = 'app.auth.externalLinkProvider';

const PROVIDER_LABELS: Record<string, string> = { github: 'GitHub', google: 'Google' };

/**
 * 「账户与安全」面板的一节：已绑定的外部账号，可以绑定或解绑。
 *
 * 绑定走一次完整的外部授权：跳到提供商、回到回调页，回调页据 {@link EXTERNAL_LINK_PENDING_KEY}
 * 走绑定端点。至少要留一种登录方式，服务端会拒绝解绑最后一个（没有密码时）。
 */
@Component({
  selector: 'app-external-logins',
  imports: [NgIcon, HlmButton, HlmSpinner],
  providers: [provideIcons({ lucideLink })],
  templateUrl: './external-logins.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ExternalLogins {
  private readonly accountService = inject(AccountService);
  private readonly confirmService = inject(ConfirmService);
  private readonly settingContext = inject(SettingContextService);
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

  protected readonly data = signal<ExternalLoginsOutputDto | null>(null);
  protected readonly loadFailed = signal(false);
  protected readonly busy = signal<string | null>(null);

  protected readonly rows = computed(() => {
    const data = this.data();
    if (!data) {
      return [];
    }

    const linkedCount = data.providers.filter((p) => p.link).length;
    return data.providers.map((p) => ({
      provider: p.provider,
      label: PROVIDER_LABELS[p.provider] ?? p.provider,
      link: p.link ?? null,
      detail: p.link
        ? this.t('account.externalLogins.linkedAs', {
            name: p.link.providerUsername ?? p.link.providerEmail ?? '',
            time: formatAppDate(
              p.link.creationTime,
              'date',
              this.settingContext.timeZone(),
              this.settingContext.displayLocale(),
            ),
          })
        : this.t('account.externalLogins.notLinked'),
      // 没有密码时最后一个绑定就是唯一的登录方式
      canUnlink: !!p.link && (data.hasPassword || linkedCount > 1),
    }));
  });

  constructor() {
    this.reload();
  }

  protected reload(): void {
    this.loadFailed.set(false);
    this.accountService.getExternalLogins().subscribe({
      next: (data) => this.data.set(data),
      error: () => this.loadFailed.set(true),
    });
  }

  protected async link(provider: string): Promise<void> {
    this.busy.set(provider);
    try {
      const { loginUrl } = await lastValueFrom(this.accountService.getExternalLinkUrl(provider));
      sessionStorage.setItem(EXTERNAL_LINK_PENDING_KEY, provider);
      window.location.href = loginUrl;
    } catch (error) {
      this.busy.set(null);
      this.showError(error);
    }
  }

  protected async unlink(provider: string, id: string): Promise<void> {
    const confirmed = await this.confirmService.open({
      message: this.t('account.externalLogins.unlinkConfirm', {
        provider: PROVIDER_LABELS[provider] ?? provider,
      }),
      confirmText: this.t('account.externalLogins.unlink'),
      variant: 'destructive',
    });
    if (!confirmed) {
      return;
    }

    this.busy.set(provider);
    this.accountService
      .unlinkExternalLogin(id)
      .pipe(finalize(() => this.busy.set(null)))
      .subscribe({
        next: () => {
          toast.success(this.t('account.externalLogins.unlinked'));
          this.reload();
        },
        error: (error) => this.showError(error),
      });
  }

  private showError(error: unknown): void {
    toast.error(this.t('common.requestError'), { description: applicationErrorMessage(error) });
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 的 `account.externalLogins` 同步。 */
const ENGLISH: Record<string, string> = {
  'common.requestError': 'Request failed',
  'common.retry': 'Retry',
  'account.externalLogins.header': 'Linked accounts',
  'account.externalLogins.subtitle':
    'Sign in with these accounts as well as your password. Keep at least one way to sign in.',
  'account.externalLogins.notLinked': 'Not linked',
  'account.externalLogins.linkedAs': 'Linked as {{name}} on {{time}}',
  'account.externalLogins.link': 'Link',
  'account.externalLogins.unlink': 'Unlink',
  'account.externalLogins.unlinkConfirm':
    'Unlink your {{provider}} account? You will no longer be able to sign in with it.',
  'account.externalLogins.unlinked': 'Account unlinked',
  'account.externalLogins.lastMethod': 'Your only way to sign in',
  'account.externalLogins.loadFailed': "Couldn't load linked accounts.",
  'account.externalLogins.none': 'No external sign-in providers are configured.',
};
//#endif
