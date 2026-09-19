import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideLogOut, lucideMonitor, lucideSmartphone, lucideTablet } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmItemImports } from '@spartan-ng/helm/item';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { finalize } from 'rxjs/operators';

import { applicationErrorMessage } from '../../../../core/errors/application-http-error';
import { ConfirmService } from '../../../../core/feedback/confirm-service';
//#if (IncludeLocalization)
import { translationReady } from '../../../../core/i18n/translation-ready';
//#endif
import { SettingContextService } from '../../../../core/settings/setting-context-service';
import { formatAppDate } from '../../../../shared/pipes/app-date-pipe';
import { DeviceKind, describeUserAgent } from '../../../../shared/utils/user-agent';
import { UserSessionOutputDto } from '../../models/account.dto';
import { AccountService } from '../../services/account-service';

const DEVICE_ICONS: Record<DeviceKind, string> = {
  desktop: 'lucideMonitor',
  mobile: 'lucideSmartphone',
  tablet: 'lucideTablet',
};

/** 撤销"其他所有设备"时占用的忙碌标记，与单个会话的 Id 区分开。 */
const OTHERS = 'others';

/**
 * 「账户与安全」面板的一节：本人当前登录着的设备，可以让其中一台或除本机外的全部退出。
 *
 * 撤销对已发出的 Cookie 立即生效（服务端逐请求确认会话还在）。本机不给"退出"按钮——那是退出登录。
 */
@Component({
  selector: 'app-login-devices',
  imports: [NgIcon, HlmBadge, HlmButton, HlmSpinner, ...HlmItemImports],
  providers: [provideIcons({ lucideLogOut, lucideMonitor, lucideSmartphone, lucideTablet })],
  templateUrl: './login-devices.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginDevices {
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

  /** null 表示尚未取回。 */
  protected readonly sessions = signal<UserSessionOutputDto[] | null>(null);
  protected readonly loadFailed = signal(false);
  /** 正在撤销的会话 Id，或 {@link OTHERS}。 */
  protected readonly busy = signal<string | null>(null);
  protected readonly othersBusy = computed(() => this.busy() === OTHERS);

  protected readonly hasOthers = computed(() => (this.sessions() ?? []).some((s) => !s.isCurrent));

  protected readonly rows = computed(() =>
    (this.sessions() ?? []).map((session) => {
      const device = describeUserAgent(session.userAgent);
      const title =
        [device.browser, device.os].filter(Boolean).join(' · ') ||
        this.t('account.sessions.unknownDevice');
      const when = (value: string) =>
        formatAppDate(
          value,
          'short',
          this.settingContext.timeZone(),
          this.settingContext.displayLocale(),
        );
      const details = [
        session.ipAddress ? this.t('account.sessions.ip', { ip: session.ipAddress }) : null,
        this.t('account.sessions.lastSeen', { time: when(session.lastSeenTime) }),
        this.t('account.sessions.signedInAt', { time: when(session.creationTime) }),
      ];

      return {
        id: session.id,
        isCurrent: session.isCurrent,
        icon: DEVICE_ICONS[device.kind],
        title,
        detail: details.filter(Boolean).join(' · '),
        impersonation: session.impersonatorName
          ? this.t('account.sessions.impersonatedBy', { name: session.impersonatorName })
          : null,
      };
    }),
  );

  constructor() {
    this.reload();
  }

  /** 重新取回设备列表。改密码会让其他设备退出，面板据此调用。 */
  reload(): void {
    this.loadFailed.set(false);
    this.accountService.getSessions().subscribe({
      next: (sessions) => this.sessions.set(sessions),
      error: () => this.loadFailed.set(true),
    });
  }

  protected async revoke(id: string): Promise<void> {
    const confirmed = await this.confirmService.open({
      message: this.t('account.sessions.revokeConfirm'),
      confirmText: this.t('account.sessions.revoke'),
      variant: 'destructive',
    });
    if (!confirmed) {
      return;
    }

    this.busy.set(id);
    this.accountService
      .revokeSession(id)
      .pipe(finalize(() => this.busy.set(null)))
      .subscribe({
        next: () => {
          this.sessions.update((list) => list?.filter((s) => s.id !== id) ?? null);
          toast.success(this.t('account.sessions.revoked'));
        },
        error: (error) => this.showError(error),
      });
  }

  protected async revokeOthers(): Promise<void> {
    const confirmed = await this.confirmService.open({
      message: this.t('account.sessions.revokeOthersConfirm'),
      confirmText: this.t('account.sessions.revokeOthers'),
      variant: 'destructive',
    });
    if (!confirmed) {
      return;
    }

    this.busy.set(OTHERS);
    this.accountService
      .revokeOtherSessions()
      .pipe(finalize(() => this.busy.set(null)))
      .subscribe({
        next: (count) => {
          this.sessions.update((list) => list?.filter((s) => s.isCurrent) ?? null);
          toast.success(this.t('account.sessions.revokedOthers', { count }));
        },
        error: (error) => this.showError(error),
      });
  }

  private showError(error: unknown): void {
    toast.error(this.t('common.requestError'), { description: applicationErrorMessage(error) });
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 的 `account.sessions` 同步。 */
const ENGLISH: Record<string, string> = {
  'common.requestError': 'Request failed',
  'common.retry': 'Retry',
  'account.sessions.header': 'Signed-in devices',
  'account.sessions.subtitle':
    "These devices are signed in to your account. If you don't recognize one, sign it out and change your password.",
  'account.sessions.current': 'This device',
  'account.sessions.unknownDevice': 'Unknown device',
  'account.sessions.ip': 'IP {{ip}}',
  'account.sessions.lastSeen': 'Last active {{time}}',
  'account.sessions.signedInAt': 'Signed in {{time}}',
  'account.sessions.impersonatedBy': 'Signed in by {{name}} via impersonation',
  'account.sessions.revoke': 'Sign out',
  'account.sessions.revokeOthers': 'Sign out other devices',
  'account.sessions.revokeConfirm': 'Sign this device out? It will need to sign in again.',
  'account.sessions.revokeOthersConfirm':
    'Sign out every device except this one? They will need to sign in again.',
  'account.sessions.revoked': 'The device has been signed out',
  'account.sessions.revokedOthers': 'Signed out {{count}} device(s)',
  'account.sessions.loadFailed': "Couldn't load your devices.",
};
//#endif
