import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTriangleAlert } from '@ng-icons/lucide';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogFooter } from '@spartan-ng/helm/dialog';

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
  imports: [HlmButton, HlmDialogFooter, NgIcon],
  providers: [provideIcons({ lucideTriangleAlert })],
  host: { class: 'flex min-h-0 flex-col' },
  // 动态内容位于 DialogContent 内，无 alert-dialog 的命名 group/data-size，且 Brain 根不同；
  // 在此等价展开 Vega 默认尺寸的 header/media/title 布局，保留服务给出的 aria 关联。
  template: `
    <div class="flex min-h-0 flex-1 flex-col gap-(--dialog-padding)">
      <div
        data-dialog-body
        class="grid grid-rows-[auto_auto_1fr] place-items-center gap-1.5 gap-x-6 text-center sm:grid-rows-[auto_1fr] sm:place-items-start sm:text-start"
      >
        <div
          class="bg-muted mb-2 inline-flex size-16 shrink-0 items-center justify-center rounded-md sm:row-span-2"
          aria-hidden="true"
        >
          <ng-icon name="lucideTriangleAlert" class="text-destructive text-[length:--spacing(8)]" />
        </div>
        <h2 id="confirm-dialog-title" class="text-lg font-medium sm:col-start-2">
          {{ ctx.header }}
        </h2>
        <p
          id="confirm-dialog-description"
          class="text-muted-foreground text-sm text-balance md:text-pretty"
        >
          {{ ctx.message }}
        </p>
      </div>
      <hlm-dialog-footer class="shrink-0">
        <button hlmBtn variant="outline" (click)="close(false)">{{ ctx.cancelText }}</button>
        <button hlmBtn [variant]="ctx.variant" (click)="close(true)">{{ ctx.confirmText }}</button>
      </hlm-dialog-footer>
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
