import {
  afterNextRender,
  DestroyRef,
  Directive,
  ElementRef,
  inject,
  Renderer2,
} from '@angular/core';
import {
  BrnTooltip,
  BrnTooltipPosition,
  provideBrnTooltipDefaultOptions,
} from '@spartan-ng/brain/tooltip';
import { hlm } from '@spartan-ng/helm/utils';
import { cva } from 'class-variance-authority';

export const DEFAULT_TOOLTIP_SVG_CLASS =
  'bg-foreground fill-foreground z-50 block size-2.5 translate-y-[calc(-50%-2px)] rotate-45 rounded-[2px]';

export const DEFAULT_TOOLTIP_CONTENT_CLASSES = hlm(
  'data-open:animate-in data-open:fade-in-0 data-open:zoom-in-95 data-[state=delayed-open]:animate-in data-[state=delayed-open]:fade-in-0 data-[state=delayed-open]:zoom-in-95 data-closed:animate-out data-closed:fade-out-0 data-closed:zoom-out-95 data-[side=bottom]:slide-in-from-top-2 data-[side=left]:slide-in-from-right-2 data-[side=right]:slide-in-from-left-2 data-[side=top]:slide-in-from-bottom-2 rounded-md px-3 py-1.5 text-xs bg-foreground text-background data-open:animate-in data-open:fade-in-0 data-open:zoom-in-95 data-[state=delayed-open]:animate-in data-[state=delayed-open]:fade-in-0 data-[state=delayed-open]:zoom-in-95 data-closed:animate-out data-closed:fade-out-0 data-closed:zoom-out-95 data-[side=bottom]:slide-in-from-top-2 data-[side=left]:slide-in-from-right-2 data-[side=right]:slide-in-from-left-2 data-[side=top]:slide-in-from-bottom-2 z-50 w-fit origin-(--radix-tooltip-content-transform-origin) text-balance',
);

export const tooltipPositionVariants = cva('absolute', {
  variants: {
    position: {
      top: 'bottom-0 left-[calc(50%-5px)] translate-y-full',
      bottom: '-top-2.5 left-[calc(50%-5px)] translate-y-0 rotate-180',
      left: '-end-2.5 top-[calc(50%-5px)] translate-y-0 rotate-270 rtl:-rotate-270',
      right: '-start-2.5 top-[calc(50%-5px)] translate-y-0 rotate-90 rtl:-rotate-90',
    },
  },
});

@Directive({
  selector: '[hlmTooltip]',
  exportAs: 'hlmTooltip',
  providers: [
    provideBrnTooltipDefaultOptions({
      svgClasses: DEFAULT_TOOLTIP_SVG_CLASS,
      tooltipContentClasses: DEFAULT_TOOLTIP_CONTENT_CLASSES,
      arrowClasses: (position: BrnTooltipPosition) => hlm(tooltipPositionVariants({ position })),
    }),
  ],
  hostDirectives: [
    {
      directive: BrnTooltip,
      inputs: [
        'brnTooltip: hlmTooltip',
        'position',
        'hideDelay',
        'showDelay',
        'sideOffset',
        'tooltipDisabled',
      ],
    },
  ],
})
export class HlmTooltip {
  private readonly _brnTooltip = inject(BrnTooltip);
  private readonly _host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  constructor() {
    const renderer = inject(Renderer2);
    const destroyRef = inject(DestroyRef);
    // Brain 的 focus 监听在它自己的 afterNextRender 里注册；宿主指令先于本指令构造，本回调排在其后，
    // 同一元素上的监听按注册顺序执行，所以这里总在 Brain 排上打开之后、延时到点之前撤销。
    afterNextRender(() => {
      const unlisten = renderer.listen(this._host, 'focus', () => this._cancelUnlessFocusVisible());
      destroyRef.onDestroy(unlisten);
    });
  }

  /**
   * Brain 在任何聚焦时都会打开提示框，包括对话框关闭后的程序化焦点恢复；提示框弹出后会盖住相邻行的按钮。
   * 与 Floating UI `useFocus` 的 `visibleOnly` 默认及 Base UI 一致，聚焦只在键盘模式（`:focus-visible`）下打开。
   * 指针仍悬停在宿主上时不撤销：那次打开属于悬停，例如鼠标点按已悬停的按钮。
   * 宿主包着真正的可聚焦元素、把它的 focusin 转成宿主上的 focus 时，按得到焦点的那个元素判断。
   */
  private _cancelUnlessFocusVisible(): void {
    const active = document.activeElement;
    const focused =
      active instanceof HTMLElement && this._host.contains(active) ? active : this._host;
    if (isFocusVisible(focused) || matchesSelector(this._host, ':hover', false)) return;
    // 立即关闭：替换掉 Brain 排上的打开（其延时队列用 switchMap，后到的请求取消先到的）
    (this._brnTooltip as unknown as BrnTooltipDelay).delay(false, -1);
  }
}

/**
 * Brain 的 `delay` 在 1.9 的类型声明里是 private，运行时是实例方法，也是唯一能撤销已排上打开的入口；
 * 公开的 `mutableTooltipDisabled` 另由截断提示与侧栏按钮写入，借用会打乱它们的状态。
 * 升级 Brain 时由 `tooltip-focus-visible.spec.ts` 钉住。
 */
interface BrnTooltipDelay {
  delay(isShow: boolean, delay?: number): void;
}

/** 运行环境不支持 `:focus-visible` 时按可见处理：宁可多弹，也不能让键盘用户看不到提示。 */
function isFocusVisible(element: HTMLElement): boolean {
  return matchesSelector(element, ':focus-visible', true);
}

function matchesSelector(element: HTMLElement, selector: string, unsupported: boolean): boolean {
  try {
    return element.matches(selector);
  } catch {
    return unsupported;
  }
}
