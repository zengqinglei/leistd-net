import { CdkMenuTrigger } from '@angular/cdk/menu';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';

@Component({
  imports: [...HlmDropdownMenuImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      style="position: fixed; top: 300px; left: 300px; width: 80px; height: 40px"
      [hlmDropdownMenuTrigger]="menu"
      [side]="side()"
      align="end"
    >
      Open
    </button>
    <ng-template #menu>
      <hlm-dropdown-menu style="width: 120px">
        <button hlmDropdownMenuItem>Item</button>
      </hlm-dropdown-menu>
    </ng-template>
  `,
})
class SideSwitchHost {
  readonly side = signal<'right' | 'top'>('right');
}

/**
 * 同一个下拉触发器在打开过之后改弹出方向，下次打开必须按新方向定位。
 *
 * 侧栏内容在桌面与手机抽屉之间复用同一实例：用户菜单与区域切换器的方向随断点变化
 * （桌面向右、手机向上 / 向下）。helm 触发器靠直接赋值把位置交给 CDK，不经过 CDK 的
 * ngOnChanges，overlay 建好后新方向就被忽略，菜单仍按旧方向摆、再被推回视口——
 * 不报错，只是摆错。libs/ui 的触发器为此做了定制（见 coding-frontend.md §4.7），这里钉住。
 */
describe('dropdown trigger side change after opening', () => {
  afterEach(() => {
    document.querySelectorAll('.cdk-overlay-container').forEach((el) => el.remove());
  });

  it('positions the menu on the new side', async () => {
    const fixture = TestBed.createComponent(SideSwitchHost);
    fixture.detectChanges();
    const trigger = fixture.debugElement
      .query(By.directive(CdkMenuTrigger))
      .injector.get(CdkMenuTrigger);
    const buttonElement = (fixture.nativeElement as HTMLElement).querySelector('button')!;
    const button = buttonElement.getBoundingClientRect();

    async function openMenu(): Promise<DOMRect> {
      // 按用户的方式点开：程序化 open() 在测试里会被 CDK 随即关掉
      buttonElement.click();
      fixture.detectChanges();
      await fixture.whenStable();
      // 浮层挂在 document 上，不在组件宿主里；等进场动画（缩放、平移）结束再量
      const menu = document.querySelector<HTMLElement>('[data-slot="dropdown-menu"]')!;
      // 动画可能被取消后重播（finished 以 AbortError 拒绝），方向属性也可能晚一帧才更新并重启进场动画，
      // 所以要等到没有在跑的动画、且位置连续两帧不变才量；只等一轮在慢机器上会量到动画中途
      let previous = '';
      for (let round = 0; round < 30; round++) {
        const running = menu.getAnimations().filter((a) => a.playState === 'running');
        if (running.length > 0) {
          await Promise.allSettled(running.map((a) => a.finished));
          continue;
        }
        await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));
        const current = JSON.stringify(menu.getBoundingClientRect());
        if (current === previous) break;
        previous = current;
      }
      return menu.getBoundingClientRect();
    }

    const right = await openMenu();
    expect(right.left).toBeGreaterThanOrEqual(button.right - 1);

    trigger.close();
    fixture.componentInstance.side.set('top');
    fixture.detectChanges();
    await fixture.whenStable();

    const top = await openMenu();
    expect(top.bottom).toBeLessThanOrEqual(button.top + 1);
    expect(Math.abs(top.right - button.right)).toBeLessThan(2);
  });
});
