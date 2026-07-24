import { toast } from '@spartan-ng/brain/sonner';

/**
 * 全局通知（toast）封装。
 *
 * Spartan 的 `toast()` 是函数式 API（非 DI 服务）。这里收一层薄封装：
 * - 统一入口，便于测试替身与未来替换底层实现；
 * - 用 summary/detail/life 语义对齐调用方习惯，内部映射到 sonner 的 message/description/duration。
 *
 * 用法：`notify.success('已保存')` / `notify.error('失败', { detail: '...' })`。
 * 全局宿主 `<hlm-toaster />` 挂在 app 根组件。
 */
export interface NotifyOptions {
  /** 次要说明文本（对应 sonner 的 description）。 */
  detail?: string;
  /** 自动关闭前的毫秒数（对应 sonner 的 duration）。 */
  life?: number;
}

function toData(options?: NotifyOptions) {
  if (!options) {
    return undefined;
  }
  return { description: options.detail, duration: options.life };
}

export const notify = {
  success(summary: string, options?: NotifyOptions): void {
    toast.success(summary, toData(options));
  },
  error(summary: string, options?: NotifyOptions): void {
    toast.error(summary, toData(options));
  },
  warn(summary: string, options?: NotifyOptions): void {
    toast.warning(summary, toData(options));
  },
  info(summary: string, options?: NotifyOptions): void {
    toast.info(summary, toData(options));
  },
} as const;
