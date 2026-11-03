import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { StatPanel } from './stat-panel';
import { parkPointer } from '../../utils/pointer.testing';

@Component({
  imports: [StatPanel],
  template: `
    <div [style.width.px]="width()">
      <app-stat-panel [panelTitle]="title()">
        <button type="button" statPanelAction>Action</button>
      </app-stat-panel>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class Host {
  readonly width = signal(120);
  readonly title = signal('Disabled subscriptions');
}

/** 标题单行省略：截断时聚焦提示全文并成为 Tab 停靠点；放得下时不提示、不多一个停靠点。 */
describe('StatPanel', () => {
  let fixture: ComponentFixture<Host>;

  const tooltip = () => document.querySelector<HTMLElement>('[role="tooltip"]');
  // 按结构定位标题，不按被测的 truncate class 选
  const heading = () => (fixture.nativeElement as HTMLElement).querySelector('h3')!;

  beforeEach(async () => {
    fixture = TestBed.createComponent(Host);
    await settle();
    await parkPointer();
    await expect.poll(() => tooltip()).toBeNull();
  });

  async function settle(): Promise<void> {
    fixture.detectChanges();
    // ResizeObserver 与 MutationObserver 在下一帧前回调
    await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  /** 聚焦标题后有没有出现提示；失焦后提示收起。延时由假时钟推进到期，"没出现"即禁用。 */
  async function focusTooltip(): Promise<string | null> {
    const title = heading();
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval'] });
    let text: string | null;
    try {
      title.focus();
      await vi.advanceTimersByTimeAsync(0);
      await fixture.whenStable();
      text = tooltip()?.textContent?.trim() ?? null;
      title.blur();
      await vi.advanceTimersByTimeAsync(0);
    } finally {
      vi.useRealTimers();
    }
    await expect.poll(() => tooltip()).toBeNull();
    return text;
  }

  it('shows the full title and becomes a tab stop when the title is cut', async () => {
    const title = heading();
    // 前提：标题确实被截断成一行，而不是折行放下
    expect(title.scrollWidth).toBeGreaterThan(title.clientWidth);
    expect(title.getAttribute('tabindex')).toBe('0');

    expect(await focusTooltip()).toBe('Disabled subscriptions');
  });

  it('stays silent and leaves the tab order alone when the title fits', async () => {
    fixture.componentInstance.width.set(600);
    await settle();

    expect(heading().hasAttribute('tabindex')).toBe(false);
    expect(await focusTooltip()).toBeNull();
  });
});
