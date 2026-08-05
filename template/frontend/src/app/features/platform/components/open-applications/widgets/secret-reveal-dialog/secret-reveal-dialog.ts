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
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCopy } from '@ng-icons/lucide';
import { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

/**
 * 一次性揭示 Client Secret 的弹窗（重置 / 新建后复用同一实例）。
 * 展示安全告警 + 明文 + 复制按钮；关闭后明文不再展示。文案由组件内部按条件编译处理。
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
    TranslocoModule,
    //#endif
  ],
  providers: [provideIcons({ lucideCopy })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './secret-reveal-dialog.html',
})
export class SecretRevealDialog {
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif
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
    navigator.clipboard?.writeText(value).then(() => {
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
