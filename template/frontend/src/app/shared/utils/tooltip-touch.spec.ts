import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

import { setCoarsePointer, touchTap } from './coarse-pointer.testing';

/**
 * 触摸点按不弹提示（helm 定制，见 docs/standards/frontend-spartan.md）：点按会让按钮获得焦点，此时 `:focus-visible`
 * 不成立而 CSS `:hover` 成立；提示只认键盘聚焦与鼠标或笔的悬停，不能把点按后的 `:hover` 当悬停放行。
 */
@Component({
  selector: 'app-test-host',
  imports: [HlmButtonImports, HlmTooltipImports],
  template: `<button hlmBtn variant="outline" data-testid="target" hlmTooltip="Edit">
    Target
  </button>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class TestHost {}

describe('tooltip on touch', () => {
  let fixture: ComponentFixture<TestHost>;

  async function settle(): Promise<void> {
    await fixture.whenStable();
    // Brain 的打开走 timer(0)，再等两帧让浮层挂上
    await new Promise((resolve) => setTimeout(resolve, 20));
    await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
  }

  beforeEach(async () => {
    await setCoarsePointer(true);
    fixture = TestBed.createComponent(TestHost);
    await settle();
  });

  afterEach(() => fixture.destroy());

  it('does not open when the trigger is tapped', async () => {
    const target = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
      '[data-testid="target"]',
    )!;
    const pointerTypes: string[] = [];
    target.addEventListener('pointerdown', (event) => pointerTypes.push(event.pointerType));

    await touchTap(target);
    await settle();

    // 前提：这是一次真实的触摸点按，且点按后按钮有焦点、:hover 成立
    expect(pointerTypes).toEqual(['touch']);
    expect(document.activeElement).toBe(target);
    expect(target.matches(':hover')).toBe(true);
    expect(document.querySelector('[role="tooltip"]')).toBeNull();
  });
});
