import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmSkeleton } from '@spartan-ng/helm/skeleton';
import { lastValueFrom } from 'rxjs';

import { API_ERROR_CODES } from '../../../../core/errors/api-error-codes';
import {
  ApplicationHttpError,
  applicationErrorMessage,
} from '../../../../core/errors/application-http-error';
import { AuthService } from '../../../../core/services/auth-service';
//#if (!IncludeLocalization)
import { englishText } from '../../../../shared/utils/english-text';
//#endif
import { AccountService } from '../../services/account-service';
import { injectLoginCompletion } from '../../utils/login-completion';
import { AuthShell } from '../auth-shell/auth-shell';
import {
  TwoFactorChallenge,
  TwoFactorExit,
  TwoFactorPrompt,
  twoFactorPromptOf,
} from '../two-factor-challenge/two-factor-challenge';

/**
 * 登录第二步页（`/auth/two-factor?returnUrl=`）：口令或外部登录已通过、尚待第二步。
 *
 * 待完成的登录由服务端的 HttpOnly 临时 Cookie 表明，本页凭它读取可用方式，因此刷新、或授权端点把别的依赖方
 * 的请求接回来时都能继续。服务端明确答"没有待完成的登录"时回登录页，`returnUrl` 原样带回；
 * 连不上或服务端故障不当作没有，就地说明并可重试。
 * 待完成的登录到期时先确认是否已登录（另一个标签页可能已经完成了它）：已登录就直接回到原目标。
 */
@Component({
  selector: 'app-two-factor-login',
  // prettier-ignore
  imports: [AuthShell,
    ...HlmAlertImports,
    HlmButton,
    HlmSkeleton,
    NgIcon,
    TwoFactorChallenge,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [provideIcons({ lucideCircleAlert })],
  templateUrl: './two-factor-login.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TwoFactorLogin implements OnInit {
  private readonly accountService = inject(AccountService);
  private readonly authService = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly completeLogin = injectLoginCompletion();
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#else
  protected readonly t = englishText(ENGLISH);
  //#endif

  /** 第二步完成后回到的地址（OIDC 授权回跳等），是否可接续在收尾时判定。 */
  private readonly returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');

  /** 待完成登录的可用方式；读到之前显示占位。 */
  protected readonly prompt = signal<TwoFactorPrompt | null>(null);
  /** 读取失败的原因（连不上、服务端故障）；有值时显示说明与重试。 */
  protected readonly loadError = signal<string | null>(null);
  /** 第二步已通过、正在收尾（读当前用户、建立会话上下文或整页离开）。 */
  protected readonly finishing = signal(false);

  ngOnInit(): void {
    void this.load();
  }

  /** 读取待完成的登录；重试也走这里。 */
  protected async load(): Promise<void> {
    this.loadError.set(null);
    try {
      this.prompt.set(
        twoFactorPromptOf(await lastValueFrom(this.accountService.getTwoFactorLogin())),
      );
    } catch (error) {
      // 没有待完成的登录（已到期、已完成或从没开始）是常态，安静地回到登录页
      if (isExpired(error)) {
        await this.backToLogin();
        return;
      }
      this.loadError.set(applicationErrorMessage(error));
    }
  }

  /**
   * 第二步通过、会话已下发：接着收尾。加载态交给第二步显示，验证请求回来到页面跳走之间按钮一直忙碌；
   * 已发起整页离开时不复位。
   */
  protected async onCompleted(): Promise<void> {
    this.finishing.set(true);
    let left = false;
    try {
      left = (await this.completeLogin(this.returnUrl)) === 'left';
    } catch (error) {
      this.showFailure(applicationErrorMessage(error));
    } finally {
      if (!left) {
        this.finishing.set(false);
      }
    }
  }

  protected async onCancelled(exit: TwoFactorExit): Promise<void> {
    if (exit.reason === 'expired' && (await this.isSignedIn())) {
      // 另一个标签页已经完成了这次登录：会话已经在了，直接回到原目标
      await this.onCompleted();
      return;
    }

    if (exit.reason !== 'back') {
      this.showFailure(exit.message);
    }
    // 返回登录页（换账号）或账号此刻不能登录：放弃这次待完成的登录；到期的那个服务端已经删了，再删一次也无妨
    try {
      await lastValueFrom(this.accountService.cancelTwoFactorLogin());
    } catch {
      // 放弃失败不挡住返回：挑战照样会到期，下一次登录也会先结束它
    }
    await this.backToLogin();
  }

  private backToLogin(): Promise<boolean> {
    return this.router.navigate(['/auth/login'], {
      queryParams: this.returnUrl ? { returnUrl: this.returnUrl } : {},
      replaceUrl: true,
    });
  }

  /** 静默探测会话：失败即视为未登录。 */
  private async isSignedIn(): Promise<boolean> {
    try {
      await lastValueFrom(this.authService.loadUser());
      return true;
    } catch {
      return false;
    }
  }

  private showFailure(description: string): void {
    //#if (IncludeLocalization)
    toast.error(this.transloco.translate('account.login.loginFailed'), { description });
    //#else
    toast.error('Login failed', { description });
    //#endif
  }
}

function isExpired(error: unknown): boolean {
  return (
    error instanceof ApplicationHttpError &&
    error.code === API_ERROR_CODES.twoFactorChallengeExpired
  );
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'common.retry': 'Retry',
};
//#endif
