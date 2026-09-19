import { CdkMenuTrigger } from '@angular/cdk/menu';
import {
  computed,
  Directive,
  effect,
  forwardRef,
  inject,
  input,
  SimpleChange,
} from '@angular/core';
import {
  createMenuPosition,
  MENU_SIDE,
  type MenuAlign,
  type MenuSide,
} from '@spartan-ng/brain/core';
import { injectHlmDropdownMenuConfig } from './hlm-dropdown-menu-token';

@Directive({
  selector: '[hlmDropdownMenuTrigger]',
  providers: [{ provide: MENU_SIDE, useExisting: forwardRef(() => HlmDropdownMenuTrigger) }],
  hostDirectives: [
    {
      directive: CdkMenuTrigger,
      inputs: [
        'cdkMenuTriggerFor: hlmDropdownMenuTrigger',
        'cdkMenuTriggerData: hlmDropdownMenuTriggerData',
      ],
      outputs: ['cdkMenuOpened: hlmDropdownMenuOpened', 'cdkMenuClosed: hlmDropdownMenuClosed'],
    },
  ],
  host: { 'data-slot': 'dropdown-menu-trigger' },
})
export class HlmDropdownMenuTrigger {
  private readonly _cdkTrigger = inject(CdkMenuTrigger, { host: true });
  private readonly _config = injectHlmDropdownMenuConfig();

  public readonly align = input<MenuAlign>(this._config.align);
  public readonly side = input<MenuSide>(this._config.side);

  private readonly _menuPosition = computed(() => createMenuPosition(this.align(), this.side()));

  constructor() {
    // CDK sets transform-origin on the menu content from the resolved position; the content reads it to
    // animate from the anchored corner and to derive its data-side. Cast tolerates @angular/cdk < 21.2
    // (we still support >=21.0), where the property is absent and the assignment is a harmless no-op.
    (this._cdkTrigger as { transformOriginSelector?: string }).transformOriginSelector =
      '[data-slot="dropdown-menu"]';

    effect(() => {
      const previous = this._cdkTrigger.menuPosition;
      const position = this._menuPosition();
      this._cdkTrigger.menuPosition = position;
      // 本项目定制（登记见 coding-frontend.md §4.7）：直接赋值不经过 CDK 的 ngOnChanges，
      // 菜单打开过一次、overlay 建好之后再改 side / align 不会生效（例如侧栏内容在桌面与手机抽屉间
      // 复用同一实例，弹出方向随断点变化）。走 CDK 自己的变更入口，让它更新已有 overlay 的定位策略。
      this._cdkTrigger.ngOnChanges({
        menuPosition: new SimpleChange(previous, position, previous === undefined),
      });
    });
  }
}
