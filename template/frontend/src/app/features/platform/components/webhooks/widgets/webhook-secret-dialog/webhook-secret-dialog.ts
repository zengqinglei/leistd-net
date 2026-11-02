// prettier-ignore
import {
  ChangeDetectionStrategy,
  Component,
  //#if (IncludeLocalization)
  inject,
  //#endif
  input,
  model,
} from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideCopy, lucideTriangleAlert } from '@ng-icons/lucide';
import { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

import { injectCopyToClipboard } from '../../../../../../shared/utils/clipboard';
//#if (!IncludeLocalization)
import { englishText } from '../../../../../../shared/utils/english-text';
//#endif

/** 揭示密钥的起因：新建订阅，或轮换已有订阅的密钥。 */
export type WebhookSecretKind = 'created' | 'rotated';

/**
 * 一次性揭示签名密钥的弹窗（新建与轮换复用同一实例），写法同开放应用的 `secret-reveal-dialog`：
 * 所属订阅、接入提示、密钥明文与复制；关闭后明文不再展示，由父页面清空。
 */
@Component({
  selector: 'app-webhook-secret-dialog',
  // prettier-ignore
  imports: [
    NgIcon,
    HlmButton,
    ...HlmDialogImports,
    ...HlmTooltipImports,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [provideIcons({ lucideCheck, lucideCopy, lucideTriangleAlert })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './webhook-secret-dialog.html',
})
export class WebhookSecretDialog {
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#else
  protected readonly t = englishText(ENGLISH);
  //#endif
  protected readonly clipboard = injectCopyToClipboard();
  readonly visible = model(false);
  readonly secret = input('');
  readonly kind = input<WebhookSecretKind>('created');
  readonly subscriptionName = input('');

  onStateChange(state: BrnDialogState): void {
    this.visible.set(state === 'open');
  }

  copy(): void {
    const value = this.secret();
    if (!value) {
      return;
    }
    void this.clipboard.copy(value).then((copied) => {
      if (!copied) {
        return;
      }
      //#if (IncludeLocalization)
      toast.success(this.transloco.translate('webhooks.toast.secretCopied'));
      //#else
      toast.success('Signing secret copied');
      //#endif
    });
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'common.copy': 'Copy',
  'webhooks.secret.copy': 'Copy secret',
  'webhooks.secret.createdHeader': 'Signing secret',
  'webhooks.secret.createdHint':
    'Receivers verify requests with a Standard Webhooks library and deduplicate by webhook-id.',
  'webhooks.secret.onlyOnce':
    'It cannot be viewed again after closing. Hand it to the receiver now.',
  'webhooks.secret.rotatedHeader': 'New signing secret',
  'webhooks.secret.rotatedHint':
    'The previous secret keeps signing alongside this one for a transition period. Update the receiver before it ends.',
  'webhooks.secret.saved': "I've saved it",
  'webhooks.toast.secretCopied': 'Signing secret copied',
};
//#endif
