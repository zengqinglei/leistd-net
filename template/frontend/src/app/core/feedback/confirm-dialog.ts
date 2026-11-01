import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  inject,
  Injector,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTriangleAlert } from '@ng-icons/lucide';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogFooter } from '@spartan-ng/helm/dialog';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { defer, firstValueFrom, Observable, takeUntil } from 'rxjs';

import { applicationErrorMessage } from '../errors/application-http-error';

/** confirm 对话框的上下文（由 ConfirmService 传入）。 */
export interface ConfirmContext {
  action?: () => Observable<unknown>;
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
  imports: [HlmButton, HlmDialogFooter, HlmSpinner, NgIcon],
  providers: [provideIcons({ lucideTriangleAlert })],
  host: { class: 'flex min-h-0 flex-col', '(keydown.escape)': 'onEscape($event)' },
  // 动态内容位于 DialogContent 内，无 alert-dialog 的命名 group/data-size，且 Brain 根不同；
  // 在此展开 Vega header/media/title 布局。上游没有列模板，短文案会拉伸隐式 auto 图标列；
  // 显式 auto/1fr 把图标列收在内容宽度，保留 24px 间距及服务给出的 aria 关联。
  template: `
    <div class="flex min-h-0 flex-1 flex-col gap-(--dialog-padding)">
      <div
        #body
        data-dialog-body
        tabindex="-1"
        [attr.aria-busy]="busy()"
        class="grid grid-rows-[auto_auto_1fr] place-items-center gap-1.5 gap-x-6 text-center sm:grid-cols-[auto_1fr] sm:grid-rows-[auto_1fr] sm:place-items-start sm:text-start"
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
        <div class="flex flex-col gap-2 sm:col-start-2">
          <p
            id="confirm-dialog-description"
            class="text-muted-foreground text-sm text-balance md:text-pretty"
          >
            {{ ctx.message }}
          </p>
          @if (error(); as message) {
            <p role="alert" class="text-destructive text-sm whitespace-pre-line">
              {{ message }}
            </p>
          }
        </div>
      </div>
      <hlm-dialog-footer class="shrink-0">
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
})
export class ConfirmDialog {
  protected readonly ctx = injectBrnDialogContext<ConfirmContext>();
  private readonly dialogRef = inject<BrnDialogRef<boolean>>(BrnDialogRef);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly body = viewChild.required<ElementRef<HTMLElement>>('body');
  private readonly confirmButton =
    viewChild.required<ElementRef<HTMLButtonElement>>('confirmButton');
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);

  protected onEscape(event: Event): void {
    if (
      !(event instanceof KeyboardEvent) ||
      !this.ctx.action ||
      event.defaultPrevented ||
      event.isComposing ||
      event.keyCode === 229
    )
      return;
    // 不让原 Esc 继续传给父级弹窗或全局浮层分派器。
    event.preventDefault();
    event.stopPropagation();
    this.close(false);
  }

  async close(result: boolean): Promise<void> {
    if (this.busy() || !this.dialogRef.open) return;
    if (!result || !this.ctx.action) {
      this.dialogRef.close(result);
      return;
    }
    // 原确认按钮即将禁用；主动移到正文，避免浏览器把焦点退到框外。
    this.body().nativeElement.focus();
    this.busy.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(
        defer(this.ctx.action).pipe(
          takeUntil(this.dialogRef.closing$),
          takeUntilDestroyed(this.destroyRef),
        ),
      );
      if (!this.destroyRef.destroyed && this.dialogRef.open) this.dialogRef.close(true);
    } catch (error: unknown) {
      if (this.destroyRef.destroyed || !this.dialogRef.open) return;
      this.error.set(applicationErrorMessage(error));
      // 等 disabled 复位后再回焦，读屏同时由 role=alert 感知正文错误。
      afterNextRender(
        () => {
          if (!this.destroyRef.destroyed && this.dialogRef.open)
            this.confirmButton().nativeElement.focus();
        },
        { injector: this.injector },
      );
    } finally {
      this.busy.set(false);
    }
  }
}
