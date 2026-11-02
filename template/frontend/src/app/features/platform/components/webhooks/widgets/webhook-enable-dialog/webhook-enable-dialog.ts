import { ChangeDetectionStrategy, Component, input, model, output, signal } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { BrnDialogState } from '@spartan-ng/brain/dialog';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCheckbox } from '@spartan-ng/helm/checkbox';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
//#if (!IncludeLocalization)

import { englishText } from '../../../../../../shared/utils/english-text';
//#endif

/**
 * 重新启用订阅的确认框：可勾选"一并重发失败投递"（该订阅保留期内的失败投递各按手动重发一次）。
 * 通用确认框没有附加选项，这里单独成框；点确定即关闭，请求与结果提示由父页面处理。
 */
@Component({
  selector: 'app-webhook-enable-dialog',
  // prettier-ignore
  imports: [
    HlmButton,
    HlmCheckbox,
    ...HlmDialogImports,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  templateUrl: './webhook-enable-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WebhookEnableDialog {
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif
  readonly visible = model(false);
  readonly subscriptionName = input('');
  readonly confirmed = output<boolean>();
  readonly retryFailed = signal(false);

  onStateChange(state: BrnDialogState): void {
    if (state === 'open') {
      this.retryFailed.set(false);
    }
    this.visible.set(state === 'open');
  }

  confirm(): void {
    this.confirmed.emit(this.retryFailed());
    this.visible.set(false);
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'common.cancel': 'Cancel',
  'webhooks.action.enable': 'Enable',
  'webhooks.enable.header': 'Enable subscription?',
  'webhooks.enable.message':
    '"{{name}}" starts receiving new events. Events that occurred while it was disabled are not sent.',
  'webhooks.enable.retryFailed': 'Also resend failed deliveries',
  'webhooks.enable.retryFailedHint': 'Each failed delivery still in the log is resent once.',
};
//#endif
