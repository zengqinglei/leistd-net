import { BreakpointObserver, BreakpointState } from '@angular/cdk/layout';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PaginationState, SortingState } from '@tanstack/angular-table';
import { BehaviorSubject } from 'rxjs';

import { RoleTable } from './role-table';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../core/i18n/transloco.testing';
//#endif
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
        ...provideTranslocoTesting(['en']),
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

  it("derives the current page and page count from the parent's paging state", () => {
    expect(component.currentPage()).toBe(1);
    expect(component.totalPages()).toBe(3); // 25 条 / 每页 10

    fixture.componentRef.setInput('pagination', { pageIndex: 1, pageSize: 10 } as PaginationState);
    fixture.detectChanges();

    expect(component.currentPage()).toBe(2);
  });

  it('returns to the first page when rows per page changes', () => {
    fixture.componentRef.setInput('pagination', { pageIndex: 2, pageSize: 10 } as PaginationState);
    fixture.detectChanges();

    const emitted: PaginationState[] = [];
    component.paginationChange.subscribe((value) => emitted.push(value));

    component.changePageSize(100);

    expect(emitted).toEqual([{ pageIndex: 0, pageSize: 100 }]);
  });

  it('toggles between ascending and descending on sort click and emits the sort intent', () => {
    const emitted: SortingState[] = [];
    component.sortingChange.subscribe((value) => emitted.push(value));

    component.toggleSort('displayName');
    expect(emitted[0]).toEqual([{ id: 'displayName', desc: false }]);

    fixture.componentRef.setInput('sorting', emitted[0]);
    fixture.detectChanges();

    component.toggleSort('displayName');
    expect(emitted[1]).toEqual([{ id: 'displayName', desc: true }]);
  });

  it('does not render the overflow menu when no action is available', () => {
    fixture.componentRef.setInput('canUpdate', false);
    fixture.componentRef.setInput('canDelete', false);
    fixture.componentRef.setInput('canManagePermissions', false);
    fixture.detectChanges();

    expect(component.hasRowActions()).toBe(false);

    fixture.componentRef.setInput('canManagePermissions', true);
    fixture.detectChanges();
    expect(component.hasRowActions()).toBe(true);
  });

  it('flags collapsed columns on a narrow viewport', () => {
    expect(component.hasCollapsedColumns()).toBe(false);

    viewport.next(desktop(false));
    fixture.detectChanges();

    expect(component.hasCollapsedColumns()).toBe(true);
  });

  it('tracks the expanded state per row', () => {
    component.toggleRow('1');

    expect(component.isRowExpanded('1')).toBe(true);
    expect(component.isRowExpanded('2')).toBe(false);
  });
});
