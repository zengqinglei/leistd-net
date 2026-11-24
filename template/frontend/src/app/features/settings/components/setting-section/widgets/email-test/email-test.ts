import {
  viewChild,
  ElementRef,
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { FormField, form, required, email } from '@angular/forms/signals';
//#if (IncludeLocalization)
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSend } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { firstValueFrom } from 'rxjs';

import { ApplicationHttpError } from '../../../../../../core/errors/application-http-error';
import { AuthService } from '../../../../../../core/services/auth-service';
import { SettingService } from '../../../../../../core/settings/setting-service';
import { injectFormSubmission } from '../../../../../../core/validation/form-submission';
//#if (!IncludeLocalization)
import { englishText } from '../../../../../../shared/utils/english-text';
//#endif
import { SettingTestPanel } from '../setting-test-panel/setting-test-panel';

/** 发信参数的分组标识，与后端 `SettingConstant.Groups.Email` 一致。 */
export const EMAIL_GROUP = 'Email';

/**
 * 发信失败码 → 本地词条：原因按码给出，不依赖服务端文案；不认识的码与其他失败用归一化后的通用原因。
 * 码与后端 `AppSettingErrorCodes` 逐字一致。
 */
const EMAIL_TEST_FAILURES: Readonly<Partial<Record<string, string>>> = {
  'AppSetting:TestEmailUnreachable': 'settings.emailTest.failure.unreachable',
  'AppSetting:TestEmailAuthRejected': 'settings.emailTest.failure.authRejected',
  'AppSetting:TestEmailRejected': 'settings.emailTest.failure.rejected',
  'AppSetting:TestEmailFailed': 'settings.emailTest.failure.sendFailed',
};

/**
 * 「邮件发送」分组底部的测试区：用当前生效的参数发一封测试邮件，收件人默认是自己。
 * 发送前先等本组的写入落定（{@link settle}），刚改的参数才会用上。成功提示自动消失；失败提示保留到手动关闭，
 * 原因按错误码取本地词条。
 */
@Component({
  selector: 'app-email-test',
  // prettier-ignore
  imports: [
    FormField,
    ...HlmFieldImports,
    NgIcon,
    HlmButton,
    HlmInput,
    HlmSpinner,
    SettingTestPanel,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [provideIcons({ lucideSend })],
  templateUrl: './email-test.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmailTest {
  private readonly emailTestRoot = viewChild.required<ElementRef<HTMLElement>>('emailTestRoot');
  private readonly settingService = inject(SettingService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#else
  protected readonly t = englishText(ENGLISH);
  //#endif

  /**
   * 发送前让本组的写入落定：为真时继续；本组有没保存成功的输入时为假，由提供方说明原因，这里不发请求。
   * 不给时直接发送。
   */
  readonly settle = input<() => Promise<boolean>>(() => Promise.resolve(true));

  /** 默认发给自己：最常见的用法就是"发一封给我看看收不收得到"。 */
  private readonly submitForm = injectFormSubmission();
  private readonly model = signal({ to: inject(AuthService).currentUser()?.email ?? '' });
  readonly emailForm = form(this.model, (path) => {
    required(path.to);
    email(path.to);
  });
  protected readonly to = computed(() => this.model().to);
  protected readonly sending = computed(() => this.emailForm().submitting());

  protected async send(): Promise<void> {
    this.model.update((value) => ({ to: value.to.trim() }));
    await this.submitForm(
      this.emailForm,
      this.emailTestRoot().nativeElement,
      async () => {
        // 等写入期间按钮保持"发送中"：点按钮时输入框失焦提交的那次写入也在其中
        if (!(await this.settle()())) {
          return;
        }

        const to = this.to();
        await firstValueFrom(this.settingService.sendTestEmail(to));
        toast.success(this.text('settings.emailTest.sent'), {
          description: this.text('settings.emailTest.sentDescription', { to }),
        });
      },
      (message, error) => this.reportFailure(message, error),
    );
  }

  /** 失败保留到手动关闭：管理员要照着原因去改参数，自动消失就看不全了。 */
  private reportFailure(message: string, error: unknown): void {
    const code = error instanceof ApplicationHttpError ? error.code : undefined;
    const reason = code ? EMAIL_TEST_FAILURES[code] : undefined;
    toast.error(this.text('settings.emailTest.failed'), {
      description: reason ? this.text(reason) : message,
      duration: Number.POSITIVE_INFINITY,
      closeButton: true,
    });
  }

  private text(key: string, params?: Record<string, unknown>): string {
    //#if (IncludeLocalization)
    return this.transloco.translate(key, params);
    //#else
    return this.t(key, params);
    //#endif
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'settings.emailTest.title': 'Send a test email',
  'settings.emailTest.description':
    'Sends an email with the saved settings to confirm the server can be reached and mail gets through',
  'settings.emailTest.recipient': 'Recipient',
  'settings.emailTest.send': 'Send',
  'settings.emailTest.sent': 'Test email sent',
  'settings.emailTest.sentDescription': 'Delivered to {{to}}. Check the inbox',
  'settings.emailTest.failed': "The test email wasn't sent",
  'settings.emailTest.failure.unreachable':
    "Couldn't reach the SMTP server. Check the host, port and TLS setting",
  'settings.emailTest.failure.authRejected':
    'The SMTP server rejected the sign-in. Check the username and password',
  'settings.emailTest.failure.rejected':
    'The SMTP server refused the message. Check the sender address and the recipient',
  'settings.emailTest.failure.sendFailed':
    "Couldn't connect, sign in or deliver through the SMTP server. Check the host, port, TLS and credentials; the server logs have the details",
};
//#endif
