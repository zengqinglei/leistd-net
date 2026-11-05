import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { StatPanel } from './stat-panel';
import { TruncatedTooltip } from '../../directives/truncated-tooltip';
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

  /** 标题上提示指令的判定：被截断才启用。"不出现"断言它，不靠等一段时间没看到提示。 */
  const tooltipEnabled = () =>
    fixture.debugElement
      .query(By.directive(TruncatedTooltip))
      .injector.get(TruncatedTooltip)
      .truncated();

  it('shows the full title and becomes a tab stop when the title is cut', async () => {
    const title = heading();
    // 前提：标题确实被截断成一行，而不是折行放下
    expect(title.scrollWidth).toBeGreaterThan(title.clientWidth);
    expect(title.getAttribute('tabindex')).toBe('0');

    title.focus();
    await expect.poll(() => tooltip()?.textContent?.trim()).toBe('Disabled subscriptions');
    title.blur();
    await expect.poll(() => tooltip()).toBeNull();
  });

  it('stays silent and leaves the tab order alone when the title fits', async () => {
    fixture.componentInstance.width.set(600);
    await settle();

    expect(heading().hasAttribute('tabindex')).toBe(false);
    expect(tooltipEnabled()).toBe(false);
  });
});
