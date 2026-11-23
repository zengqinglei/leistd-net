import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { userEvent } from 'vitest/browser';

import { focusWithKeyboard } from './keyboard-focus.testing';

/**
 * 提示框聚焦只认键盘模式（helm 定制，见 docs/standards/frontend-spartan.md）：Brain 在任何聚焦时都会打开，
 * 对话框用鼠标关闭后焦点恢复到行内按钮，提示框弹出并盖住上一行。用例跑在真实 Chromium 里，
 * `:focus-visible` 由浏览器按最近一次交互判定：指针交互后的程序化聚焦不匹配，键盘交互后匹配。
 */
@Component({
  selector: 'app-test-host',
  imports: [HlmButtonImports, HlmTooltipImports],
  template: `
    <button hlmBtn variant="outline" data-testid="before">Before</button>
    <div class="h-24"></div>
    <button hlmBtn variant="outline" data-testid="target" hlmTooltip="Edit">Target</button>
    <!-- 宿主包着按钮，把 focusin 转成自己的 focus（同租户人员对话框的选人触发器） -->
    <div hlmTooltip="Relayed" (focusin)="relay($event)">
      <button hlmBtn variant="outline" data-testid="inner">Inner</button>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class TestHost {
  relay(event: FocusEvent): void {
    (event.currentTarget as HTMLElement).dispatchEvent(new FocusEvent('focus'));
  }
}

describe('tooltip opens on focus only when it is focus-visible', () => {
  let fixture: ComponentFixture<TestHost>;

  async function settle(): Promise<void> {
    await fixture.whenStable();
    // Brain 的打开走 timer(0)，再等两帧让浮层挂上
    await new Promise((resolve) => setTimeout(resolve, 20));
    await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
  }

  function byTestId(id: string): HTMLButtonElement {
    return (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      `[data-testid="${id}"]`,
    )!;
  }

  function tooltip(): HTMLElement | null {
    return document.querySelector<HTMLElement>('[role="tooltip"]');
  }

  /** 用真实点按进入指针模式；点的是另一个按钮，指针不停在目标上。 */
  async function enterPointerModality(): Promise<void> {
    await userEvent.click(byTestId('before'));
    await settle();
  }

  beforeEach(async () => {
    fixture = TestBed.createComponent(TestHost);
    await settle();
  });

  afterEach(() => fixture.destroy());

  it('does not open on programmatic focus after a pointer interaction, e.g. focus restored by a dialog', async () => {
    await enterPointerModality();
    const target = byTestId('target');

    target.focus();
    await settle();

    expect(document.activeElement).toBe(target);
    // 前提：浏览器确实把这次聚焦判为非键盘，否则本用例没有覆盖到撤销分支
    expect(target.matches(':focus-visible')).toBe(false);
    expect(tooltip()).toBeNull();
  });

  it('opens when the keyboard moves focus onto the trigger', async () => {
    await enterPointerModality();

    await userEvent.keyboard('{Tab}');

    const target = byTestId('target');
    expect(document.activeElement).toBe(target);
    expect(target.matches(':focus-visible')).toBe(true);
    await expect.poll(() => tooltip()?.textContent?.trim()).toBe('Edit');
  });

  it('still opens on programmatic focus while the user is on the keyboard', async () => {
    await enterPointerModality();
    await userEvent.keyboard('{Tab}');
    await expect.poll(() => tooltip()).not.toBeNull();
    await userEvent.keyboard('{Shift>}{Tab}{/Shift}');
    await expect.poll(() => tooltip()).toBeNull();

    const target = byTestId('target');
    target.focus();

    expect(target.matches(':focus-visible')).toBe(true);
    await expect.poll(() => tooltip()?.textContent?.trim()).toBe('Edit');
  });

  it('judges a relayed focus by the focused element inside the trigger', async () => {
    await enterPointerModality();
    const inner = byTestId('inner');
    inner.focus();
    await settle();
    expect(inner.matches(':focus-visible')).toBe(false);
    expect(tooltip()).toBeNull();

    await focusWithKeyboard(inner);

    await expect.poll(() => tooltip()?.textContent?.trim()).toBe('Relayed');
  });

  it('opens on mouse hover', async () => {
    await userEvent.hover(byTestId('target'));

    await expect.poll(() => tooltip()?.textContent?.trim()).toBe('Edit');
  });

  it('keeps the hover tooltip open when the hovered trigger is clicked', async () => {
    const target = byTestId('target');
    await userEvent.hover(target);
    await expect.poll(() => tooltip()).not.toBeNull();

    await userEvent.click(target);
    await settle();

    expect(document.activeElement).toBe(target);
    expect(target.matches(':focus-visible')).toBe(false);
    expect(tooltip()).not.toBeNull();
  });

  it('treats focus as visible when the browser cannot evaluate :focus-visible', async () => {
    await enterPointerModality();
    const matches = Element.prototype.matches;
    vi.spyOn(Element.prototype, 'matches').mockImplementation(function (
      this: Element,
      selector: string,
    ) {
      if (selector === ':focus-visible') throw new SyntaxError(`unsupported selector ${selector}`);
      return matches.call(this, selector);
    });

    byTestId('target').focus();

    await expect.poll(() => tooltip()?.textContent?.trim()).toBe('Edit');
  });

  // WCAG 1.4.13：悬停出的内容须可悬停停留，指针从宿主移到提示框上时提示不关闭
  it('keeps the hover tooltip open while the pointer moves onto the tooltip', async () => {
    await userEvent.hover(byTestId('target'));
    await expect.poll(() => tooltip()).not.toBeNull();

    await userEvent.hover(tooltip()!);
    await settle();

    expect(tooltip()?.textContent?.trim()).toBe('Edit');
    expect(tooltip()!.getAttribute('data-state')).not.toBe('closed');
  });
});
