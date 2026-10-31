import { DOCUMENT } from '@angular/common';
import {
  viewChild,
  ElementRef,
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  Injector,
  output,
  signal,
} from '@angular/core';
import { maxLength, FormField, form, required, pattern } from '@angular/forms/signals';
//#if (IncludeLocalization)
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideShieldCheck, lucideTriangleAlert } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { firstValueFrom } from 'rxjs';
import { finalize } from 'rxjs/operators';

import { API_ERROR_CODES } from '../../../../core/errors/api-error-codes';
import {
  ApplicationHttpError,
  applicationErrorMessage,
} from '../../../../core/errors/application-http-error';
import { AuthService } from '../../../../core/services/auth-service';
import { injectFormSubmission } from '../../../../core/validation/form-submission';
import { OtpCodeInput } from '../../../../shared/components/otp-code-input/otp-code-input';
import { StatusDot } from '../../../../shared/directives/status-dot';
import { TwoFactorStatusOutputDto } from '../../../../shared/dtos/account-security.dto';
//#if (!IncludeLocalization)
import { englishText } from '../../../../shared/utils/english-text';
//#endif
import { AccountService } from '../../services/account-service';
import { canChooseDefault } from '../../utils/two-factor-methods';
import { RecoveryCodes } from '../recovery-codes/recovery-codes';
import { TwoFactorSetup } from '../two-factor-setup/two-factor-setup';

type Mode = 'idle' | 'setup' | 'codes' | 'disable' | 'regenerate' | 'replace' | 'replaceSetup';

/** 剩余恢复码不多于这个数时提醒重新生成。 */
const RECOVERY_CODES_LOW = 3;

/**
 * 「账户与安全」面板的一节：两步验证的启用、换绑、停用与恢复码。启用、换绑与停用都会让本人其他设备退出，
 * 面板据 {@link sessionsChanged} 刷新设备列表。
 *
 * 换绑先出示现有验证器上的码；验证器丢了时用一个恢复码（当场用掉），再走与首次启用相同的设置。
 * 不经过"停用"，组织强制两步验证时同样可用。
 */
@Component({
  selector: 'app-two-factor-settings',
  // prettier-ignore
  imports: [FormField,
    NgIcon,
    ...HlmAlertImports,
    HlmBadge,
    HlmButton,
    ...HlmFieldImports,
    HlmInput,
    HlmSpinner,
    OtpCodeInput,
    RecoveryCodes,
    StatusDot,
    TwoFactorSetup,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [provideIcons({ lucideShieldCheck, lucideTriangleAlert })],
  templateUrl: './two-factor-settings.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TwoFactorSettings {
  private readonly verificationRoot =
    viewChild.required<ElementRef<HTMLElement>>('verificationRoot');
  private readonly accountService = inject(AccountService);
  private readonly authService = inject(AuthService);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#else
  protected readonly t = englishText(ENGLISH);
  //#endif

  /** 本人的其他会话被服务端撤销了（启用、停用两步验证时）。 */
  readonly sessionsChanged = output<void>();
  /** 登录默认方式或可用方式变了（设为默认、启用、停用）：面板据此刷新其他各节。 */
  readonly defaultChanged = output<void>();

  protected readonly status = signal<TwoFactorStatusOutputDto | null>(null);
  protected readonly loadFailed = signal(false);
  protected readonly mode = signal<Mode>('idle');
  protected readonly codes = signal<string[]>([]);
  private readonly operationBusy = signal(false);
  protected readonly busy = computed(
    () => this.operationBusy() || this.verificationForm().submitting(),
  );

  private readonly submitForm = injectFormSubmission();
  private readonly verificationModel = signal({ code: '', password: '', recoveryCode: '' });
  readonly verificationForm = form(this.verificationModel, (path) => {
    maxLength(path.recoveryCode, 64);
    const needsCode = () =>
      this.mode() === 'disable' ||
      this.mode() === 'regenerate' ||
      (this.mode() === 'replace' && !this.useRecoveryCode());
    required(path.code, { when: needsCode });
    pattern(path.code, /^\d{6}$/, { when: needsCode, error: { kind: 'otpCode' } });
    required(path.password, { when: () => this.mode() === 'disable' && this.hasPassword() });
    required(path.recoveryCode, {
      when: () => this.mode() === 'replace' && this.useRecoveryCode(),
    });
  });
  protected readonly code = this.verificationForm.code().value;
  protected readonly password = this.verificationForm.password().value;

  protected readonly recoveryCode = this.verificationForm.recoveryCode().value;
  /** 换绑时用恢复码代替验证码（验证器已经丢了）。 */
  protected readonly useRecoveryCode = signal(false);
  /** 换绑出示的第二因素，交给设置组件随开始设置一并提交。 */
  protected readonly proof = signal<{ code?: string; recoveryCode?: string } | null>(null);
  /** 本人是否设有口令：没有口令的账号停用时只填验证码。 */
  protected readonly hasPassword = computed(
    () => this.authService.currentUser()?.hasPassword ?? true,
  );
  /** 身份验证器能用、还有别的方式可选（通行密钥、邮箱验证码）：才有"默认"可言。 */
  protected readonly defaultChoice = computed(() =>
    canChooseDefault(this.status(), 'authenticator'),
  );
  /**
   * 组织要求两步验证、身份验证器又是最后一个独立因子（没有通行密钥）：不能停用。
   */
  protected readonly disableBlocked = computed(() => {
    const status = this.status();
    return !!status?.requiredByPolicy && status.passkeys.length === 0;
  });
  /** 剩余恢复码不多：提醒里带"重新生成"，操作行里就不再重复这个按钮。 */
  protected readonly recoveryCodesLow = computed(() => {
    const status = this.status();
    return !!status?.enabled && status.recoveryCodesLeft <= RECOVERY_CODES_LOW;
  });

  constructor() {
    this.reload();
  }

  /** 重新读取状态；邮箱验证码一节改了默认方式后由面板调用。 */
  reload(): void {
    this.loadFailed.set(false);
    this.accountService.getTwoFactorStatus().subscribe({
      next: (status) => this.status.set(status),
      error: () => this.loadFailed.set(true),
    });
  }

  protected setMode(mode: Mode): void {
    // 先结束旧控件的焦点会话，否则模板销毁时的 blur 会在 reset 后重新标记 touched。
    if (['replace', 'disable', 'regenerate'].includes(this.mode())) {
      const active = this.document.activeElement;
      if (active instanceof HTMLElement && this.verificationRoot().nativeElement.contains(active)) {
        active.blur();
      }
    }
    this.verificationForm().reset({ password: '', code: '', recoveryCode: '' });
    if (mode !== 'replaceSetup') {
      this.proof.set(null);
    }
    this.mode.set(mode);
  }

  protected toggleRecoveryCode(): void {
    this.useRecoveryCode.update((value) => !value);
    this.code.set('');
    this.recoveryCode.set('');
    // 切换后焦点落在新出现的输入框上
    const id = this.useRecoveryCode()
      ? 'two-factor-replace-recovery-code'
      : 'two-factor-replace-code';
    afterNextRender(() => this.document.getElementById(id)?.focus(), {
      injector: this.injector,
    });
  }

  protected async startReplace(): Promise<void> {
    await this.submitForm(
      this.verificationForm,
      this.verificationRoot().nativeElement,
      async () => {
        if (this.operationBusy()) {
          return;
        }

        this.proof.set(
          this.useRecoveryCode()
            ? { recoveryCode: this.recoveryCode() }
            : { code: this.code().replace(/\s/g, '') },
        );
        this.setMode('replaceSetup');
      },
    );
  }

  protected onEnabled(codes: string[]): void {
    const replaced = this.mode() === 'replaceSetup';
    this.proof.set(null);
    this.codes.set(codes);
    // 已有通行密钥时恢复码不变，服务端不发新的：直接回到状态
    this.mode.set(codes.length > 0 ? 'codes' : 'idle');
    this.reload();
    this.refreshCurrentUser();
    this.sessionsChanged.emit();
    this.defaultChanged.emit();
    const message = replaced ? 'account.twoFactor.replacedToast' : 'account.twoFactor.enabledToast';
    //#if (IncludeLocalization)
    toast.success(this.transloco.translate(message));
    //#else
    toast.success(this.t(message));
    //#endif
  }

  protected finishCodes(): void {
    this.codes.set([]);
    this.setMode('idle');
  }

  protected async disable(): Promise<void> {
    await this.submitForm(
      this.verificationForm,
      this.verificationRoot().nativeElement,
      async () => {
        if (this.disableBlocked()) {
          return;
        }

        await firstValueFrom(
          this.accountService.disableTwoFactor({
            // 没有口令的账号只带验证码：带了口令服务端按"方式不具备"拒绝
            password: this.hasPassword() ? this.password() : undefined,
            code: this.code().replace(/\s/g, ''),
          }),
        );
        this.setMode('idle');
        this.reload();
        this.refreshCurrentUser();
        this.sessionsChanged.emit();
        this.defaultChanged.emit();
        //#if (IncludeLocalization)
        toast.success(this.transloco.translate('account.twoFactor.disabledToast'));
        //#else
        toast.success(this.t('account.twoFactor.disabledToast'));
        //#endif
      },
      (message, error) => this.showError(error, message),
    );
  }

  protected async regenerate(): Promise<void> {
    await this.submitForm(
      this.verificationForm,
      this.verificationRoot().nativeElement,
      async () => {
        const result = await firstValueFrom(
          this.accountService.regenerateRecoveryCodes({ code: this.code().replace(/\s/g, '') }),
        );
        this.codes.set(result.recoveryCodes);
        this.mode.set('codes');
        this.reload();
      },
      (message, error) => this.showError(error, message),
    );
  }

  /** 登录时默认先给身份验证器；不增减方式，不要求确认身份。 */
  protected setAsDefault(): void {
    if (this.busy()) {
      return;
    }

    this.operationBusy.set(true);
    this.accountService
      .setPreferredTwoFactorMethod('authenticator')
      .pipe(finalize(() => this.operationBusy.set(false)))
      .subscribe({
        next: () => {
          this.reload();
          this.defaultChanged.emit();
          //#if (IncludeLocalization)
          toast.success(this.transloco.translate('account.twoFactor.defaultToast'));
          //#else
          toast.success(this.t('account.twoFactor.defaultToast'));
          //#endif
        },
        error: (error) => this.showError(error),
      });
  }

  // 顶栏等处读的是当前用户上的 isTwoFactorEnabled，跟着刷新
  private refreshCurrentUser(): void {
    this.authService.loadUser().subscribe({ error: () => undefined });
  }

  /**
   * 停用与重发恢复码都要输入验证码：码被拒时补一句刚用过的码要等新码（同一个码只能用一次，
   * 刚在登录或别处用过的会被拒）。
   */
  private showError(error: unknown, remainingMessage?: string): void {
    const message = remainingMessage ?? applicationErrorMessage(error);
    const codeRejected =
      error instanceof ApplicationHttpError && error.code === API_ERROR_CODES.twoFactorCodeInvalid;
    //#if (IncludeLocalization)
    const hint = codeRejected ? this.transloco.translate('common.twoFactorCodeReuseHint') : '';
    toast.error(this.transloco.translate('common.requestError'), {
      description: hint ? `${message} ${hint}` : message,
    });
    //#else
    const hint = codeRejected ? this.t('common.twoFactorCodeReuseHint') : '';
    toast.error(this.t('common.requestError'), {
      description: hint ? `${message} ${hint}` : message,
    });
    //#endif
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'common.requestError': 'Request error',
  'common.twoFactorCodeReuseHint': 'If you just used this code, wait for a new one.',
  'common.retry': 'Retry',
  'common.cancel': 'Cancel',
  'account.twoFactor.header': 'Two-factor authentication',
  'account.twoFactor.subtitleOff':
    'Besides your password, sign-in also asks for a code from an authenticator app on your phone.',
  'account.twoFactor.subtitleOn':
    'Sign-in asks for a code from your authenticator app after your password.',
  'account.twoFactor.on': 'On',
  'account.twoFactor.turnOn': 'Turn on',
  'account.twoFactor.turnOff': 'Turn off',
  'account.twoFactor.regenerate': 'Regenerate recovery codes',
  'account.twoFactor.recoveryCodesLeft': '{{count}} unused recovery code(s) left',
  'account.twoFactor.recoveryCodesLow':
    "Only {{count}} recovery code(s) left. Once they run out, you can't sign in if you lose your phone.",
  'account.twoFactor.requiredByPolicy':
    'Your organization requires two-factor authentication, so it cannot be turned off.',
  'account.twoFactor.disableHint':
    'Enter your password and a code from the authenticator app to turn it off.',
  'account.twoFactor.disableHintNoPassword':
    'Enter a code from the authenticator app to turn it off.',
  'account.twoFactor.regenerateHint':
    'Enter a code from the authenticator app. The old recovery codes stop working.',
  'account.twoFactor.passwordLabel': 'Current password',
  'account.twoFactor.codeLabel': 'Verification code',
  'account.twoFactor.confirmDisable': 'Turn off two-factor authentication',
  'account.twoFactor.confirmRegenerate': 'Regenerate',
  'account.twoFactor.enabledToast':
    'Two-factor authentication is on. Other devices were signed out.',
  'account.twoFactor.disabledToast':
    'Two-factor authentication is off. Other devices were signed out.',
  'account.twoFactor.loadFailed': "Couldn't load the two-factor status.",
  'account.twoFactor.replace': 'Replace authenticator',
  'account.twoFactor.replaceHint':
    'Enter a code from your current authenticator app. Lost it? Use a recovery code instead; it is used up right away.',
  'account.twoFactor.useRecoveryCode': "Can't use your authenticator? Use a recovery code",
  'account.twoFactor.useCode': 'Use an authenticator code instead',
  'account.twoFactor.recoveryCodeLabel': 'Recovery code',
  'account.twoFactor.continue': 'Continue',
  'account.twoFactor.default': 'Default',
  'account.twoFactor.setDefault': 'Set as default',
  'account.twoFactor.defaultToast': 'Saved. Sign-in will ask for this method first.',
  'account.twoFactor.replacedToast':
    'Authenticator replaced. The old one and its recovery codes no longer work, and other devices were signed out.',
};
//#endif
