/** 容器里可能接收焦点的元素；是否真能聚焦再由 {@link canFocus} 判断。 */
const FOCUSABLE = 'input:not([type="hidden"]), select, textarea, button, [tabindex]';

/** 按可见 DOM 顺序定位错误，亦覆盖自定义控件的真实内部输入框。 */
export function focusFirstInvalid(root: HTMLElement | null): void {
  const controls = root?.querySelectorAll<HTMLElement>('[aria-invalid="true"]');
  for (const control of controls ?? []) {
    const target = canFocus(control) ? control : focusableInside(control);
    if (!target) continue;
    target.focus();
    return;
  }
}

/**
 * 无效态标在不可聚焦的容器上时（ARIA 1.2 中单选只在 radiogroup 上标 aria-invalid），聚焦容器里的控件：
 * 单选组按键盘进入的落点取选中项，没有选中项时取第一个可聚焦项。
 */
function focusableInside(container: HTMLElement): HTMLElement | undefined {
  const checked = container.querySelector<HTMLElement>('input[type="radio"]:checked');
  if (checked && canFocus(checked)) return checked;
  return Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE)).find(canFocus);
}

function canFocus(control: HTMLElement): boolean {
  return (
    !control.matches(':disabled,[disabled],[aria-disabled="true"]') &&
    control.tabIndex >= 0 &&
    control.getClientRects().length > 0
  );
}
