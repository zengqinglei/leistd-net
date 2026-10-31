import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
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

import { AuthService } from '../../../../../../core/services/auth-service';
import { SettingService } from '../../../../../../core/settings/setting-service';
import { injectFormSubmission } from '../../../../../../core/validation/form-submission';
//#if (!IncludeLocalization)
import { englishText } from '../../../../../../shared/utils/english-text';
//#endif

/** 「邮件发送」面板底部：用当前生效的参数发一封测试邮件，失败时原样显示服务端给出的原因。 */
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
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [provideIcons({ lucideSend })],
  templateUrl: './email-test.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmailTest {
  private readonly settingService = inject(SettingService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#else
  protected readonly t = englishText(ENGLISH);
  //#endif

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
      'email-test-form',
      async () => {
        await firstValueFrom(this.settingService.sendTestEmail(this.to()));
        //#if (IncludeLocalization)
        toast.success(this.transloco.translate('settings.emailTest.sent', { to: this.to() }));
        //#else
        toast.success(this.t('settings.emailTest.sent', { to: this.to() }));
        //#endif
      },
      (message) => {
        //#if (IncludeLocalization)
        toast.error(this.transloco.translate('common.requestError'), { description: message });
        //#else
        toast.error(this.t('common.requestError'), { description: message });
        //#endif
      },
    );
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'settings.emailTest.title': 'Send a test email',
  'settings.emailTest.description':
    'Uses the settings above as they are now. Save your changes first.',
  'settings.emailTest.recipient': 'Recipient',
  'settings.emailTest.send': 'Send test email',
  'settings.emailTest.sent': 'Test email sent to {{to}}',
  'common.requestError': 'Request error',
};
//#endif
