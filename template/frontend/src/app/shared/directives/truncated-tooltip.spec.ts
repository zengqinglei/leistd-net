import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { userEvent } from 'vitest/browser';

import { TruncatedTooltip } from './truncated-tooltip';
import { parkPointer } from '../utils/pointer.testing';

@Component({
  imports: [...HlmTooltipImports, TruncatedTooltip],
  template: `
    <button
      type="button"
      class="block"
      [style.width.px]="width()"
      [hlmTooltip]="label()"
      [appTruncatedTooltip]="text"
    >
      <span #text class="block truncate">{{ label() }}</span>
    </button>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class Host {
  readonly width = signal(80);
  readonly label = signal('Account & security settings');
}

/** 键盘聚焦与悬停一样触发提示：截断时给出全文，放得下时不提示；宽度与文案变化后重新判断。 */
describe('TruncatedTooltip', () => {
  let fixture: ComponentFixture<Host>;

  const tooltip = () => document.querySelector<HTMLElement>('[role="tooltip"]');

  beforeEach(async () => {
    await createHost();
  });

  async function createHost(): Promise<void> {
    fixture = TestBed.createComponent(Host);
    await settle();
    // 物理指针会跨用例保留；就绪后移到空白处，避免先前悬停的提示干扰键盘断言。
    await parkPointer();
    await expect.poll(() => tooltip()).toBeNull();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    // ResizeObserver 与 MutationObserver 在下一帧前回调
    await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    await fixture.whenStable();
  }

  const directive = () =>
    fixture.debugElement.query(By.directive(TruncatedTooltip)).injector.get(TruncatedTooltip);

  /** 聚焦后提示给出全文、失焦后收起；真实计时器，等它出现与消失。 */
  async function expectFocusTooltip(text: string): Promise<void> {
    const button = (fixture.nativeElement as HTMLElement).querySelector('button')!;
    button.focus();
    await expect.poll(() => tooltip()?.textContent?.trim()).toBe(text);
    button.blur();
    await expect.poll(() => tooltip()).toBeNull();
  }

  // "不出现"断言指令的判定（提示被禁用）：等一段时间看提示没来，在它本来就会迟到时也会通过（testing.md §4 因果屏障）
  it('shows the full label on focus when it is cut', async () => {
    await expectFocusTooltip('Account & security settings');
  });

  it('stays silent when the label fits', async () => {
    fixture.componentInstance.width.set(400);
    await settle();

    expect(directive().truncated()).toBe(false);
  });

  it('clears a retained pointer before testing focus on a replacement host', async () => {
    const button = (fixture.nativeElement as HTMLElement).querySelector('button')!;
    await userEvent.hover(button);
    await expect.poll(() => tooltip()?.textContent?.trim()).toBe('Account & security settings');

    // 销毁宿主不会移动物理指针，新宿主在相同位置出现时仍可能被悬停；createHost 停放指针后提示已收起
    fixture.destroy();
    await createHost();
    fixture.componentInstance.width.set(400);
    await settle();

    expect(directive().truncated()).toBe(false);
    expect(tooltip()).toBeNull();
  });

  it('measures again when the text changes', async () => {
    fixture.componentInstance.width.set(200);
    fixture.componentInstance.label.set('Short');
    await settle();
    expect(directive().truncated()).toBe(false);

    fixture.componentInstance.label.set('A much longer label that no longer fits the button');
    await settle();

    expect(directive().truncated()).toBe(true);
    await expectFocusTooltip('A much longer label that no longer fits the button');
  });

  // 不可聚焦的宿主按它决定 tabindex：只有截断时才是 Tab 停靠点
  it('reports whether the text is cut for hosts that add tabindex only then', async () => {
    expect(directive().truncated()).toBe(true);

    fixture.componentInstance.width.set(400);
    await settle();

    expect(directive().truncated()).toBe(false);
  });
});
