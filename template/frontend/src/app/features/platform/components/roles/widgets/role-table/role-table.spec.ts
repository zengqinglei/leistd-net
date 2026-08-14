import { BreakpointObserver, BreakpointState } from '@angular/cdk/layout';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
//#if (IncludeLocalization)
import { provideTransloco, TRANSLOCO_LOADER } from '@jsverse/transloco';
//#endif
import { PaginationState, SortingState } from '@tanstack/angular-table';
import { BehaviorSubject } from 'rxjs';

import { RoleTable } from './role-table';
import { RoleOutputDto } from '../../../../models/role.dto';

/**
 * 角色表格与用户表格共用同一套受控分页/排序约定，这里覆盖同样的关键路径。
 * 两张表各自实现，语义漂移不会有任何东西报错，只能靠各自的用例钉住。
 */
describe('RoleTable', () => {
  let fixture: ComponentFixture<RoleTable>;
  let component: RoleTable;
  let viewport: BehaviorSubject<BreakpointState>;

  const desktop = (matches: boolean): BreakpointState => ({
    matches,
    breakpoints: {
      '(min-width: 768px)': matches,
      '(min-width: 1024px)': matches,
    },
  });

  function role(id: string, name: string): RoleOutputDto {
    return {
      id,
      name,
      displayName: name,
      isDefault: false,
      isStatic: false,
      sort: 1,
      userCount: 0,
      permissionCount: 0,
      creationTime: '2026-01-01T00:00:00Z',
    } as unknown as RoleOutputDto;
  }

  beforeEach(async () => {
    viewport = new BehaviorSubject<BreakpointState>(desktop(true));

    await TestBed.configureTestingModule({
      imports: [RoleTable],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        //#if (IncludeLocalization)
        // 模板用了 transloco 管道：配真实 provider 加空加载器，文案回落成键名，
        // 本组用例关心的是分页与排序状态，不是具体文案。
        provideTransloco({
          config: { availableLangs: ['en'], defaultLang: 'en', fallbackLang: 'en' },
        }),
        { provide: TRANSLOCO_LOADER, useValue: { getTranslation: () => Promise.resolve({}) } },
        //#endif
        {
          provide: BreakpointObserver,
          useValue: { observe: () => viewport.asObservable() },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RoleTable);
    component = fixture.componentInstance;

    fixture.componentRef.setInput('roles', [role('1', 'admin'), role('2', 'member')]);
    fixture.componentRef.setInput('totalCount', 25);
    fixture.componentRef.setInput('pagination', { pageIndex: 0, pageSize: 10 } as PaginationState);
    fixture.detectChanges();
  });

  it('当前页与总页数按父级传入的分页状态派生', () => {
    expect(component.currentPage()).toBe(1);
    expect(component.totalPages()).toBe(3); // 25 条 / 每页 10

    fixture.componentRef.setInput('pagination', { pageIndex: 1, pageSize: 10 } as PaginationState);
    fixture.detectChanges();

    expect(component.currentPage()).toBe(2);
  });

  it('改每页条数时回到第一页', () => {
    fixture.componentRef.setInput('pagination', { pageIndex: 2, pageSize: 10 } as PaginationState);
    fixture.detectChanges();

    const emitted: PaginationState[] = [];
    component.paginationChange.subscribe((value) => emitted.push(value));

    component.changePageSize(100);

    expect(emitted).toEqual([{ pageIndex: 0, pageSize: 100 }]);
  });

  it('点击排序在升序与降序之间切换，并回传排序意图', () => {
    const emitted: SortingState[] = [];
    component.sortingChange.subscribe((value) => emitted.push(value));

    component.toggleSort('displayName');
    expect(emitted[0]).toEqual([{ id: 'displayName', desc: false }]);

    fixture.componentRef.setInput('sorting', emitted[0]);
    fixture.detectChanges();

    component.toggleSort('displayName');
    expect(emitted[1]).toEqual([{ id: 'displayName', desc: true }]);
  });

  it('一个可用操作都没有时不渲染溢出菜单', () => {
    fixture.componentRef.setInput('canUpdate', false);
    fixture.componentRef.setInput('canDelete', false);
    fixture.componentRef.setInput('canManagePermissions', false);
    fixture.detectChanges();

    expect(component.hasRowActions()).toBeFalse();

    fixture.componentRef.setInput('canManagePermissions', true);
    fixture.detectChanges();
    expect(component.hasRowActions()).toBeTrue();
  });

  it('窄视口下标记存在被折叠的列', () => {
    expect(component.hasCollapsedColumns()).toBeFalse();

    viewport.next(desktop(false));
    fixture.detectChanges();

    expect(component.hasCollapsedColumns()).toBeTrue();
  });

  it('行展开状态按行独立记录', () => {
    component.toggleRow('1');

    expect(component.isRowExpanded('1')).toBeTrue();
    expect(component.isRowExpanded('2')).toBeFalse();
  });
});
