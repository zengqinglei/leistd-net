import { afterEveryRender, ElementRef, inject } from '@angular/core';

/** 读屏无效态与可见报错使用相同的 matcher 结果（含 forceInvalid）。 */
export function hlmAriaInvalid(invalid: boolean | null | undefined): 'true' | null {
  return invalid ? 'true' : null;
}

/**
 * 在 Brain 渲染后同步真实控件，覆盖其私有模板及后续 raw invalid 写入。
 * undefined 表示由组合控件接管；null/false 移除属性。
 */
export function syncHlmAriaInvalid(
  selector: string | null,
  invalid: () => boolean | null | undefined,
): void {
  const host = inject<ElementRef<HTMLElement>>(ElementRef);
  afterEveryRender({
    write: () => {
      const control = selector
        ? host.nativeElement.querySelector<HTMLElement>(selector)
        : host.nativeElement;
      const state = invalid();
      if (state === undefined) return;
      if (state) control?.setAttribute('aria-invalid', 'true');
      else control?.removeAttribute('aria-invalid');
    },
  });
}
