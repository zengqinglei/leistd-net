import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideCopy } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { toDataURL } from 'qrcode';
import { finalize } from 'rxjs/operators';

import { applicationErrorMessage } from '../../../../core/errors/application-http-error';
//#if (IncludeLocalization)
import { translationReady } from '../../../../core/i18n/translation-ready';
//#endif
import { injectCopyToClipboard } from '../../../../shared/utils/clipboard';
import { AccountService } from '../../services/account-service';
import { OtpCodeInput } from '../otp-code-input/otp-code-input';

/**
 * 设置两步验证：把密钥添加到身份验证器应用，再输入应用上的验证码确认启用。
 *
 * 个人设置与"组织要求启用"的强制设置页共用。确认成功后发出恢复码，展示与保存由使用方负责。
 * 二维码在浏览器里生成：密钥不经过任何第三方二维码服务。
 */
@Component({
  selector: 'app-two-factor-setup',
  imports: [NgIcon, HlmButton, HlmSpinner, OtpCodeInput],
  providers: [provideIcons({ lucideCheck, lucideCopy })],
  templateUrl: './two-factor-setup.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TwoFactorSetup {
  private readonly accountService = inject(AccountService);
  private readonly clipboard = injectCopyToClipboard();
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

  /** 确认启用后发出恢复码（明文只有这一次）。 */
  readonly enabled = output<string[]>();
  /** 是否提供"取消"：组织要求启用时没有退路，不给这个按钮。 */
  readonly cancellable = input(true);
  /** 放弃设置。 */
  readonly cancelled = output<void>();

  protected readonly secret = signal<string | null>(null);
  protected readonly qrDataUrl = signal<string | null>(null);
  protected readonly loadFailed = signal(false);
  protected readonly code = signal('');
  protected readonly submitting = signal(false);
  protected readonly copied = this.clipboard.copied;

  /** 按 4 位一组显示密钥，照抄时不容易看串行。 */
  protected readonly groupedSecret = computed(
    () =>
      this.secret()
        ?.match(/.{1,4}/g)
        ?.join(' ') ?? '',
  );
  protected readonly canSubmit = computed(
    () => /^\d{6}$/.test(this.code().replace(/\s/g, '')) && !this.submitting(),
  );

  constructor() {
    this.start();
  }

  protected start(): void {
    this.loadFailed.set(false);
    this.accountService.beginTwoFactorSetup().subscribe({
      next: async (setup) => {
        this.secret.set(setup.secret);
        try {
          this.qrDataUrl.set(await toDataURL(setup.otpAuthUri, { margin: 1, width: 192 }));
        } catch {
          // 二维码生成失败时仍可手动输入密钥
          this.qrDataUrl.set(null);
        }
      },
      error: () => this.loadFailed.set(true),
    });
  }

  // 剪贴板不可用（非安全上下文等）时用户仍能手动选中复制
  protected copySecret(): void {
    const secret = this.secret();
    if (secret) {
      void this.clipboard.copy(secret);
    }
  }

  protected submit(): void {
    if (!this.canSubmit()) {
      return;
    }

    this.submitting.set(true);
    this.accountService
      .enableTwoFactor(this.code().replace(/\s/g, ''))
      .pipe(finalize(() => this.submitting.set(false)))
      .subscribe({
        next: (result) => this.enabled.emit(result.recoveryCodes),
        error: (error) =>
          toast.error(this.t('common.requestError'), {
            description: applicationErrorMessage(error),
          }),
      });
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 的 `account.twoFactor` 同步。 */
const ENGLISH: Record<string, string> = {
  'common.requestError': 'Request failed',
  'common.retry': 'Retry',
  'common.cancel': 'Cancel',
  'account.twoFactor.scanTitle': '1. Add this account to your authenticator app',
  'account.twoFactor.scanHint':
    'Scan the QR code with an app such as Google Authenticator, Microsoft Authenticator or Tencent Authenticator.',
  'account.twoFactor.manualHint': "Can't scan it? Enter this key instead:",
  'account.twoFactor.copyKey': 'Copy key',
  'account.twoFactor.copied': 'Copied',
  'account.twoFactor.qrAlt': 'QR code for the authenticator app',
  'account.twoFactor.verifyTitle': '2. Enter the 6-digit code shown in the app',
  'account.twoFactor.codeLabel': 'Verification code',
  'account.twoFactor.enable': 'Verify and turn on',
  'account.twoFactor.setupLoadFailed': "Couldn't start the setup.",
};
//#endif
