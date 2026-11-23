import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HlmComboboxImports } from '@spartan-ng/helm/combobox';
import { HlmDatePickerImports } from '@spartan-ng/helm/date-picker';
import { HlmSelectImports } from '@spartan-ng/helm/select';

import { TablePaginator } from '../components/table-paginator/table-paginator';
import { setCoarsePointer } from './coarse-pointer.testing';

/**
 * 触发器宿主不出盒子（helm 定制，见 docs/standards/frontend-spartan.md）：class 经输入转交内部按钮，
 * 静态 class 属性仍留在宿主上；宿主 `display: contents` 后，底色、内边距只画在按钮上一次。
 */
@Component({
  imports: [HlmComboboxImports, HlmSelectImports, HlmDatePickerImports],
  template: `
    <div class="w-64" data-testid="combobox">
      <hlm-combobox>
        <hlm-combobox-trigger class="bg-muted px-2">Zone</hlm-combobox-trigger>
      </hlm-combobox>
    </div>
    <div class="w-64" data-testid="select">
      <hlm-select>
        <hlm-select-trigger class="bg-muted px-2"><hlm-select-value /></hlm-select-trigger>
      </hlm-select>
    </div>
    <div class="w-64" data-testid="date-picker">
      <hlm-date-picker>
        <hlm-date-picker-trigger class="bg-muted px-2">Pick</hlm-date-picker-trigger>
      </hlm-date-picker>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class TriggerHost {}

describe('helm trigger hosts', () => {
  // select 的按钮是块级 flex，行内宿主的内边距推不动它，只有另外两个会被右移
  for (const [name, selector, shiftedByHostPadding] of [
    ['combobox', 'hlm-combobox-trigger', true],
    ['select', 'hlm-select-trigger', false],
    ['date-picker', 'hlm-date-picker-trigger', true],
  ] as const) {
    it(`renders the ${name} trigger classes once, on the button only`, async () => {
      const fixture = TestBed.createComponent(TriggerHost);
      await fixture.whenStable();
      const box = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
        `[data-testid="${name}"]`,
      )!;
      const host = box.querySelector<HTMLElement>(selector)!;
      const button = host.querySelector('button')!;

      expect(getComputedStyle(host).display).toBe('contents');
      expect(host.getClientRects()).toHaveLength(0);
      expect(button.classList).toContain('px-2');
      if (shiftedByHostPadding) {
        // 宿主有盒子时，宿主的 px-2 会把按钮右移 8px
        expect(button.getBoundingClientRect().left).toBe(box.getBoundingClientRect().left);
      }
    });
  }
});

describe('table paginator rows-per-page trigger on touch', () => {
  afterEach(() => setCoarsePointer(false));

  // 触屏下按钮升到 44px。宿主上写 display 类（如 inline-flex）会盖过 contents，宿主又成了 h-8 的盒子，
  // 按钮伸出所在的行
  it('keeps the enlarged trigger inside its row', async () => {
    await setCoarsePointer(true);
    const fixture = TestBed.createComponent(TablePaginator);
    fixture.componentRef.setInput('labels', {
      currentPageReport: '1 - 10 of 42',
      rowsPerPage: 'Rows per page',
      page: 'Page 1 of 5',
      first: 'First',
      previous: 'Previous',
      next: 'Next',
      last: 'Last',
    });
    fixture.componentRef.setInput('rows', 10);
    fixture.componentRef.setInput('canPrev', false);
    fixture.componentRef.setInput('canNext', true);
    await fixture.whenStable();

    const button = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
      'button[brnSelectTrigger]',
    )!;
    const row = button.closest('hlm-select')!.parentElement!.getBoundingClientRect();
    const rect = button.getBoundingClientRect();
    // 前提：触屏尺寸确实生效
    expect(rect.height).toBe(44);
    expect(rect.top).toBeGreaterThanOrEqual(row.top);
    expect(rect.bottom).toBeLessThanOrEqual(row.bottom);
  });
});
