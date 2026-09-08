import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { TablePaginator, TablePaginatorLabels } from './table-paginator';

/**
 * 分页栏是所有管理表格共用的导航入口。
 *
 * 这里验的是边界状态与意图回传：按钮的禁用完全由 canPrev/canNext 决定，
 * 组件自身不推断页码。禁用判断一旦失准，用户会在首页点"上一页"、
 * 或在末页点"下一页"，父表格据此发出越界请求。
 */
describe('TablePaginator', () => {
  let fixture: ComponentFixture<TablePaginator>;
  let component: TablePaginator;

  const labels: TablePaginatorLabels = {
    currentPageReport: '第 1 - 10 条，共 42 条',
    rowsPerPage: '每页条数',
    page: '第 1 / 5 页',
    first: '首页',
    previous: '上一页',
    next: '下一页',
    last: '末页',
  };

  function buttonFor(label: string): HTMLButtonElement {
    const found = fixture.debugElement
      .queryAll(By.css('button'))
      .map((item) => item.nativeElement as HTMLButtonElement)
      .find((element) => element.getAttribute('aria-label') === label);

    if (!found) {
      throw new Error(`按钮未找到：${label}`);
    }

    return found;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [TablePaginator] }).compileComponents();

    fixture = TestBed.createComponent(TablePaginator);
    component = fixture.componentInstance;

    fixture.componentRef.setInput('labels', labels);
    fixture.componentRef.setInput('rows', 10);
    fixture.componentRef.setInput('canPrev', false);
    fixture.componentRef.setInput('canNext', true);
    fixture.detectChanges();
  });

  it('首页时禁用首页与上一页，保留下一页与末页', () => {
    expect(buttonFor('首页').disabled).toBeTrue();
    expect(buttonFor('上一页').disabled).toBeTrue();
    expect(buttonFor('下一页').disabled).toBeFalse();
    expect(buttonFor('末页').disabled).toBeFalse();
  });

  it('末页时禁用下一页与末页', () => {
    fixture.componentRef.setInput('canPrev', true);
    fixture.componentRef.setInput('canNext', false);
    fixture.detectChanges();

    expect(buttonFor('首页').disabled).toBeFalse();
    expect(buttonFor('上一页').disabled).toBeFalse();
    expect(buttonFor('下一页').disabled).toBeTrue();
    expect(buttonFor('末页').disabled).toBeTrue();
  });

  it('只有一页时四个按钮全部禁用', () => {
    fixture.componentRef.setInput('canPrev', false);
    fixture.componentRef.setInput('canNext', false);
    fixture.detectChanges();

    for (const label of ['首页', '上一页', '下一页', '末页']) {
      expect(buttonFor(label).disabled).withContext(label).toBeTrue();
    }
  });

  it('点击可用的导航按钮回传对应意图', () => {
    fixture.componentRef.setInput('canPrev', true);
    fixture.componentRef.setInput('canNext', true);
    fixture.detectChanges();

    const emitted: string[] = [];
    component.firstPage.subscribe(() => emitted.push('first'));
    component.prevPage.subscribe(() => emitted.push('prev'));
    component.nextPage.subscribe(() => emitted.push('next'));
    component.lastPage.subscribe(() => emitted.push('last'));

    buttonFor('首页').click();
    buttonFor('上一页').click();
    buttonFor('下一页').click();
    buttonFor('末页').click();

    expect(emitted).toEqual(['first', 'prev', 'next', 'last']);
  });

  it('渲染父级传入的当前页信息，不自行推算', () => {
    const report = fixture.nativeElement.textContent as string;

    // 分页状态由父表格持有，本组件只渲染；自己算一份必然与父级的口径分叉。
    expect(report).toContain('第 1 - 10 条，共 42 条');
    expect(report).toContain('第 1 / 5 页');
  });

  it('每页条数为空值时不回传，避免把 null 当成合法页大小', () => {
    const emitted: (number | null)[] = [];
    component.rowsChange.subscribe((value) => emitted.push(value));

    component.onRowsChange(null);
    component.onRowsChange(undefined);
    expect(emitted).toEqual([]);

    component.onRowsChange(50);
    expect(emitted).toEqual([50]);
  });
});
