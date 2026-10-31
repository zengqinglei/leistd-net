/** 按可见 DOM 顺序定位错误，亦覆盖自定义控件的真实内部输入框。 */
export function focusFirstInvalid(root: HTMLElement | null): void {
  const controls = root?.querySelectorAll<HTMLElement>('[aria-invalid="true"]');
  for (const control of controls ?? []) {
    if (
      control.matches(':disabled,[disabled],[aria-disabled="true"]') ||
      control.tabIndex < 0 ||
      !control.getClientRects().length
    )
      continue;
    control.focus();
    return;
  }
}
