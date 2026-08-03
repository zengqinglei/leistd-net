import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTriangleAlert } from '@ng-icons/lucide';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButton } from '@spartan-ng/helm/button';

/** confirm 对话框的上下文（由 ConfirmService 传入）。 */
export interface ConfirmContext {
  message: string;
  header: string;
  confirmText: string;
  cancelText: string;
  /** 确认按钮样式：destructive 用于删除等破坏性操作。 */
  variant: 'default' | 'destructive';
}

/**
 * 通用确认对话框内容组件，经 HlmDialogService 动态打开。
 * 关闭时通过 BrnDialogRef 回传布尔结果（确认 = true）。
 */
@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmButton, NgIcon],
  providers: [provideIcons({ lucideTriangleAlert })],
  template: `
    <div class="flex flex-col gap-4">
      <div class="flex items-start gap-3">
        <ng-icon name="lucideTriangleAlert" class="mt-0.5 shrink-0 text-xl text-destructive" />
        <div class="flex flex-col gap-1">
          <h2 id="confirm-dialog-title" class="text-lg font-semibold">{{ ctx.header }}</h2>
          <p id="confirm-dialog-description" class="text-sm text-muted-foreground">
            {{ ctx.message }}
          </p>
        </div>
      </div>
      <div class="flex justify-end gap-2">
        <button hlmBtn variant="outline" (click)="close(false)">{{ ctx.cancelText }}</button>
        <button hlmBtn [variant]="ctx.variant" (click)="close(true)">{{ ctx.confirmText }}</button>
      </div>
    </div>
  `,
})
export class ConfirmDialog {
  protected readonly ctx = injectBrnDialogContext<ConfirmContext>();
  private readonly dialogRef = inject<BrnDialogRef<boolean>>(BrnDialogRef);

  close(result: boolean): void {
    this.dialogRef.close(result);
  }
}
