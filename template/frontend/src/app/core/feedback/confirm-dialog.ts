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
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTriangleAlert } from '@ng-icons/lucide';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogFooter } from '@spartan-ng/helm/dialog';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { defer, firstValueFrom, Observable, takeUntil } from 'rxjs';

import { applicationErrorMessage } from '../errors/application-http-error';

const EMPTY_ACTION = Symbol('EMPTY_ACTION');

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
  // 按 variant 使用 Vega 官方默认示例（无图标）或 destructive 的 size=sm 示例。
  // 动态内容位于 DialogContent 内，没有 alert-dialog 的命名 group/data-size，且 Brain 根不同；
  // 因此在此等价展开 header/media/footer 布局，不混用依赖另一根的 HlmAlertDialog 部件。
  template: `
    <div class="flex min-h-0 flex-1 flex-col gap-(--dialog-padding)">
      <div
        #body
        data-dialog-body
        tabindex="-1"
        [attr.aria-busy]="busy()"
        class="grid place-items-center gap-1.5 text-center outline-none"
        [class]="
          ctx.variant === 'destructive'
            ? 'grid-rows-[auto_auto_1fr]'
            : 'grid-rows-[auto_1fr] sm:place-items-start sm:text-start'
        "
      >
        @if (ctx.variant === 'destructive') {
          <div
            class="bg-destructive/10 text-destructive dark:bg-destructive/20 mb-2 inline-flex size-16 shrink-0 items-center justify-center rounded-md"
            aria-hidden="true"
          >
            <ng-icon name="lucideTriangleAlert" class="text-[length:--spacing(8)]" />
          </div>
        }
        <h2 id="confirm-dialog-title" class="text-lg font-medium">
          {{ ctx.header }}
        </h2>
        <p
          id="confirm-dialog-description"
          class="text-muted-foreground text-sm text-balance md:text-pretty"
        >
          {{ ctx.message }}
        </p>
      </div>
      <hlm-dialog-footer
        class="shrink-0"
        [class]="ctx.variant === 'destructive' ? 'grid grid-cols-2' : ''"
      >
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
  private readonly body = viewChild.required<ElementRef<HTMLElement>>('body');
  private readonly confirmButton =
    viewChild.required<ElementRef<HTMLButtonElement>>('confirmButton');
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif

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
    // 非交互落点按官方 dialog content 的写法去掉描边。
    this.body().nativeElement.focus();
    this.busy.set(true);
    try {
      const value = await firstValueFrom(
        defer(this.ctx.action).pipe(
          takeUntil(this.dialogRef.closing$),
          takeUntilDestroyed(this.destroyRef),
        ),
        { defaultValue: EMPTY_ACTION },
      );
      if (!this.destroyRef.destroyed && this.dialogRef.open)
        this.dialogRef.close(value !== EMPTY_ACTION);
    } catch (error: unknown) {
      if (this.destroyRef.destroyed || !this.dialogRef.open) return;
      const description = applicationErrorMessage(error);
      //#if (IncludeLocalization)
      toast.error(this.transloco.translate('common.requestError'), { description });
      //#else
      toast.error('Request error', { description });
      //#endif
      // 与非字段表单错误一致，只 toast 一次；等 disabled 复位后回焦，留框供重试或取消。
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
