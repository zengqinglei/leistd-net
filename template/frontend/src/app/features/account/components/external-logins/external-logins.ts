import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideLink } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmItemImports } from '@spartan-ng/helm/item';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { lastValueFrom } from 'rxjs';
import { finalize } from 'rxjs/operators';

import { applicationErrorMessage } from '../../../../core/errors/application-http-error';
import { ConfirmService } from '../../../../core/feedback/confirm-service';
import { SettingContextService } from '../../../../core/settings/setting-context-service';
import { formatAppDate } from '../../../../shared/pipes/app-date-pipe';
//#if (!IncludeLocalization)
import { englishText } from '../../../../shared/utils/english-text';
//#endif
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
  // prettier-ignore
  imports: [
    NgIcon,
    HlmButton,
    HlmSpinner,
    ...HlmItemImports,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
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
  //#else
  protected readonly t = englishText(ENGLISH);
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
      // 文案在模板里经 t 组装，这里只给出绑定的账号名与时间
      linkedName: p.link ? (p.link.providerUsername ?? p.link.providerEmail ?? '') : '',
      linkedAt: p.link
        ? formatAppDate(
            p.link.creationTime,
            'date',
            this.settingContext.timeZone(),
            this.settingContext.displayLocale(),
          )
        : '',
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
    const providerLabel = PROVIDER_LABELS[provider] ?? provider;
    const confirmed = await this.confirmService.open({
      //#if (IncludeLocalization)
      message: this.transloco.translate('account.externalLogins.unlinkConfirm', {
        provider: providerLabel,
      }),
      confirmText: this.transloco.translate('account.externalLogins.unlink'),
      //#else
      message: this.t('account.externalLogins.unlinkConfirm', { provider: providerLabel }),
      confirmText: this.t('account.externalLogins.unlink'),
      //#endif
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
          //#if (IncludeLocalization)
          toast.success(this.transloco.translate('account.externalLogins.unlinked'));
          //#else
          toast.success(this.t('account.externalLogins.unlinked'));
          //#endif
          this.reload();
        },
        error: (error) => this.showError(error),
      });
  }

  private showError(error: unknown): void {
    //#if (IncludeLocalization)
    toast.error(this.transloco.translate('common.requestError'), {
      description: applicationErrorMessage(error),
    });
    //#else
    toast.error(this.t('common.requestError'), { description: applicationErrorMessage(error) });
    //#endif
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'common.requestError': 'Request error',
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
