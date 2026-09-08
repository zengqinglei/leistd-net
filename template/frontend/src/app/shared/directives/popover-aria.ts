import { Directive, ElementRef, effect, inject, input } from '@angular/core';

/**
 * 给 Spartan Popover 生成的对话框补可访问名。
 *
 * Brain 把 `role="dialog"` 设在 CDK overlay pane 上（不是 `hlm-popover-content`），
 * 且未暴露 aria 透传输入，因此在内容元素上写 `aria-label` 不会命名该 dialog。
 * 本指令把名称写到内容所属的 overlay pane 上；Spartan 原生支持后可直接移除。
 *
 * 用法：`<hlm-popover-content *hlmPopoverPortal [appPopoverAria]="title()">`
 */
@Directive({
  selector: '[appPopoverAria]',
})
export class PopoverAria {
  /** 对话框的可访问名（通常与面板内可见标题一致）。 */
  readonly label = input.required<string>({ alias: 'appPopoverAria' });

  private readonly host = inject(ElementRef<HTMLElement>);

  constructor() {
    effect(() => {
      const label = this.label();
      const pane = (this.host.nativeElement as HTMLElement).closest<HTMLElement>(
        '.cdk-overlay-pane',
      );
      if (!pane || !label) {
        return;
      }
      pane.setAttribute('aria-label', label);
    });
  }
}
