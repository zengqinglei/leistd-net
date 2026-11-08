import { ChangeDetectionStrategy, Component, Injectable, inject } from '@angular/core';
import { injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogFooter, HlmDialogService } from '@spartan-ng/helm/dialog';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { firstValueFrom } from 'rxjs';

import { ConfirmContext, ConfirmDialog } from '../../../../../../core/feedback/confirm-dialog';
import { ConfirmOptions } from '../../../../../../core/feedback/confirm-service';

export interface DistributedEventRecoveryOptions extends ConfirmOptions {
  scope: string;
  cutoff: string;
  criteria: string;
  scopeLabel: string;
  cutoffLabel: string;
  criteriaLabel: string;
}
interface RecoveryContext extends ConfirmContext {
  scope: string;
  cutoff: string;
  criteria: string;
  scopeLabel: string;
  cutoffLabel: string;
  criteriaLabel: string;
}

/** 恢复确认按范围、截止与条件分行展示，沿用通用确认框的单次请求、取消与失败重试行为。 */
@Component({
  selector: 'app-distributed-event-recovery-confirm',
  imports: [HlmButton, HlmDialogFooter, HlmSpinner],
  template: `
    <div class="flex min-h-0 flex-1 flex-col gap-(--dialog-padding)">
      <div #body tabindex="-1" class="flex flex-col gap-4 outline-none" [attr.aria-busy]="busy()">
        <h2 id="event-recovery-title" class="text-lg font-medium">{{ ctx.header }}</h2>
        <div id="event-recovery-description" class="flex flex-col gap-3 text-sm">
          <dl class="grid grid-cols-[auto_1fr] gap-x-3 gap-y-2">
            <dt class="text-muted-foreground">{{ ctx.scopeLabel }}</dt>
            <dd class="min-w-0 wrap-anywhere">{{ ctx.scope }}</dd>
            <dt class="text-muted-foreground">{{ ctx.cutoffLabel }}</dt>
            <dd class="min-w-0 wrap-anywhere">{{ ctx.cutoff }}</dd>
            <dt class="text-muted-foreground">{{ ctx.criteriaLabel }}</dt>
            <dd class="min-w-0 wrap-anywhere">{{ ctx.criteria }}</dd>
          </dl>
          <p class="text-muted-foreground">{{ ctx.message }}</p>
        </div>
      </div>
      <hlm-dialog-footer>
        <button hlmBtn variant="outline" [disabled]="busy()" (click)="close(false)">
          {{ ctx.cancelText }}
        </button>
        <button
          #confirmButton
          hlmBtn
          [variant]="ctx.variant"
          [disabled]="busy()"
          (click)="close(true)"
        >
          @if (busy()) {
            <hlm-spinner data-icon="inline-start" />
          }
          {{ ctx.confirmText }}
        </button>
      </hlm-dialog-footer>
    </div>
  `,
  host: { class: 'flex min-h-0 flex-col', '(keydown.escape)': 'onEscape($event)' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DistributedEventRecoveryConfirm extends ConfirmDialog {
  protected override readonly ctx = injectBrnDialogContext<RecoveryContext>();
}

/** 仅服务本页的恢复确认入口，不扩展全局通用确认框契约。 */
@Injectable({ providedIn: 'root' })
export class DistributedEventRecoveryConfirmService {
  private readonly dialog = inject(HlmDialogService);
  private static sequence = 0;

  async open(options: DistributedEventRecoveryOptions): Promise<boolean> {
    if (options.owner?.destroyed) return false;
    const context: RecoveryContext = {
      ...options,
      header: options.header!,
      confirmText: options.confirmText!,
      cancelText: options.cancelText!,
      variant: options.variant ?? 'default',
    };
    const ref = this.dialog.open<boolean, RecoveryContext>(DistributedEventRecoveryConfirm, {
      id: `distributed-event-recovery-${++DistributedEventRecoveryConfirmService.sequence}`,
      context,
      showCloseButton: false,
      disableClose: true,
      closeOnOutsidePointerEvents: false,
      contentClass: 'sm:max-w-lg',
      role: 'alertdialog',
      ariaLabelledBy: 'event-recovery-title',
      ariaDescribedBy: 'event-recovery-description',
    });
    const unregister = options.owner?.onDestroy(() => ref.forceClose());
    try {
      return (await firstValueFrom(ref.closed$)) === true;
    } finally {
      unregister?.();
    }
  }
}
