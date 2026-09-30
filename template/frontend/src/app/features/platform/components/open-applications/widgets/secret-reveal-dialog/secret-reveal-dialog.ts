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
import { lucideCopy } from '@ng-icons/lucide';
import { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

import { injectCopyToClipboard } from '../../../../../../shared/utils/clipboard';
//#if (!IncludeLocalization)
import { englishText } from '../../../../../../shared/utils/english-text';
//#endif

/**
 * 一次性揭示 Client Secret 的弹窗（重置 / 新建后复用同一实例）。
 * 展示安全告警 + 明文 + 复制按钮；关闭后明文不再展示。
 */
@Component({
  selector: 'app-secret-reveal-dialog',
  standalone: true,
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
  providers: [provideIcons({ lucideCopy })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './secret-reveal-dialog.html',
})
export class SecretRevealDialog {
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#else
  protected readonly t = englishText(ENGLISH);
  //#endif
  private readonly clipboard = injectCopyToClipboard();
  readonly visible = model(false);
  readonly secret = input('');
  readonly header = input('');

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
      toast.success(this.transloco.translate('common.success'), {
        description: this.transloco.translate('openApp.toast.secretCopied'),
      });
      //#else
      toast.success('Success', { description: 'Secret copied' });
      //#endif
    });
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'openApp.secret.warning':
    'Please copy and store it securely now; the secret will not be shown in plain text again.',
  'common.copy': 'Copy',
  'openApp.secret.copyAndClose': 'Copy and close',
};
//#endif
