import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideX } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { injectHlmA11yLabels } from '@spartan-ng/helm/utils';

import { ConfirmService } from '../../feedback/confirm-service';

/** 业务弹窗的退出入口；根须静态 disableClose 并隐藏 Helm 内置关闭按钮。 */
@Component({
  selector: 'app-dialog-close',
  exportAs: 'appDialogClose',
  imports: [HlmButton, NgIcon],
  providers: [provideIcons({ lucideX })],
  template: `
    <button
      hlmBtn
      type="button"
      variant="ghost"
      size="icon-sm"
      class="absolute end-4 top-4"
      [disabled]="busy()"
      (click)="requestClose()"
    >
      <span class="sr-only">{{ a11y.close() }}</span>
      <ng-icon name="lucideX" />
    </button>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DialogClose {
  readonly dirty = input(false);
  readonly busy = input(false);
  readonly dismissed = output<void>();
  protected readonly a11y = injectHlmA11yLabels();
  private readonly confirm = inject(ConfirmService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif
  private confirming = false;

  async requestClose(
    event?: Event,
    action: () => void = () => this.dismissed.emit(),
  ): Promise<void> {
    if (
      event?.defaultPrevented ||
      (event instanceof KeyboardEvent && (event.isComposing || event.keyCode === 229))
    )
      return;
    if (this.busy() || this.confirming) return;
    event?.preventDefault();
    if (!this.dirty()) {
      action();
      return;
    }
    this.confirming = true;
    // 标记覆盖整个确认阶段，连续 Esc/点击不能叠出多个确认框。
    try {
      const discard = await this.confirm.open({
        //#if (IncludeLocalization)
        message: this.transloco.translate('common.unsavedChanges'),
        confirmText: this.transloco.translate('common.discardChanges'),
        cancelText: this.transloco.translate('common.continueEditing'),
        //#else
        message: 'Discard your unsaved changes?',
        confirmText: 'Discard changes',
        cancelText: 'Continue editing',
        //#endif
      });
      if (discard && !this.busy()) action();
    } finally {
      this.confirming = false;
    }
  }
}
