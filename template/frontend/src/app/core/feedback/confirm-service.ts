import { DestroyRef, inject, Injectable } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { HlmDialogService } from '@spartan-ng/helm/dialog';
import { firstValueFrom, Observable } from 'rxjs';

import { ConfirmContext, ConfirmDialog } from './confirm-dialog';

export interface ConfirmOptions {
  message: string;
  /**
   * 框内只执行一次写请求，成功须发值（含 undefined/null）；失败 toast 一次并留框重试。
   * 不发值即完成按取消处理：关框并返回 false，不提示错误。
   * 可取消的前置验证须在 open() 前完成；需再认证重试、请求后接弹窗或跳转的流程不用 action。
   */
  action?: () => Observable<unknown>;
  /** 调用组件销毁时强制关闭，停止尚未完成的请求订阅。 */
  owner?: DestroyRef;
  /** 标题（默认「请确认」）。 */
  header?: string;
  /** 确认按钮文案（默认「确定」）。 */
  confirmText?: string;
  /** 取消按钮文案（默认「取消」）。 */
  cancelText?: string;
  /** 确认按钮样式；破坏性操作（删除等）用 destructive。 */
  variant?: 'default' | 'destructive';
}

/**
 * 确认对话框服务：基于 `HlmDialogService` 动态打开通用 confirm 组件，确认成功时返回 `true`，取消返回 `false`。
 * 用法：`if (await confirm.open({ message, variant: 'destructive' })) { ... }`。
 */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private static sequence = 0;
  private readonly dialog = inject(HlmDialogService);
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  //#endif

  async open(options: ConfirmOptions): Promise<boolean> {
    if (options.owner?.destroyed) return false;
    const context: ConfirmContext = {
      action: options.action,
      message: options.message,
      //#if (IncludeLocalization)
      header: options.header ?? this.transloco.translate('common.confirm'),
      confirmText: options.confirmText ?? this.transloco.translate('common.ok'),
      cancelText: options.cancelText ?? this.transloco.translate('common.cancel'),
      //#else
      header: options.header ?? 'Please confirm',
      confirmText: options.confirmText ?? 'OK',
      cancelText: options.cancelText ?? 'Cancel',
      //#endif
      variant: options.variant ?? 'default',
    };

    // role=alertdialog + aria 关联标题/正文：确认/破坏性提示的 WAI-ARIA 正确语义。
    // 无 ✕ 关闭钮（alert-dialog 语义：必须显式选择动作）。
    const dialogRef = this.dialog.open<boolean, ConfirmContext>(ConfirmDialog, {
      // Brain 的声明式根与服务有独立的默认 id 计数；应用命名空间避免叠层时碰撞。
      id: `app-confirm-${++ConfirmService.sequence}`,
      context,
      showCloseButton: false,
      // 写请求框统一由组件处理 Esc；程序化关闭与父级销毁不经过用户退出闸门。
      disableClose: !!options.action,
      closeOnOutsidePointerEvents: false,
      // 非断点 max-w-xs 不会覆盖 DialogContent 的 sm:max-w-md，紧凑样式须同时约束 sm。
      contentClass: context.variant === 'destructive' ? 'max-w-xs sm:max-w-xs' : 'sm:max-w-lg',
      role: 'alertdialog',
      ariaLabelledBy: 'confirm-dialog-title',
      ariaDescribedBy: 'confirm-dialog-description',
    });

    const unregister = options.owner?.onDestroy(() => dialogRef.forceClose());
    try {
      const result = await firstValueFrom(dialogRef.closed$);
      return result === true;
    } finally {
      unregister?.();
    }
  }
}
