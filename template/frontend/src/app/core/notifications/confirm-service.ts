import { inject, Injectable } from '@angular/core';
import { HlmDialogService } from '@spartan-ng/helm/dialog';
import { firstValueFrom } from 'rxjs';

import { ConfirmContext, ConfirmDialog } from './confirm-dialog';

export interface ConfirmOptions {
  /** 主体提示文本。 */
  message: string;
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
 * 确认对话框服务。
 *
 * Spartan 无服务式 confirm（alert-dialog 是声明式）。这里基于 `HlmDialogService`
 * 动态打开通用 confirm 组件，返回 `Promise<boolean>`（确认 = true）。
 * 替代原 PrimeNG `ConfirmationService.confirm({ ..., accept })` 的用法。
 *
 * 用法：`if (await confirm.open({ message, variant: 'destructive' })) { ... }`。
 */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly dialog = inject(HlmDialogService);

  async open(options: ConfirmOptions): Promise<boolean> {
    const context: ConfirmContext = {
      message: options.message,
      header: options.header ?? '请确认',
      confirmText: options.confirmText ?? '确定',
      cancelText: options.cancelText ?? '取消',
      variant: options.variant ?? 'default',
    };

    const dialogRef = this.dialog.open<boolean, ConfirmContext>(ConfirmDialog, {
      context,
      contentClass: 'sm:!max-w-md',
    });

    const result = await firstValueFrom(dialogRef.closed$);
    return result === true;
  }
}
