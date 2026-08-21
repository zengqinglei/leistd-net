import { BreakpointObserver, BreakpointState } from '@angular/cdk/layout';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
//#if (IncludeLocalization)
import { provideTransloco, TRANSLOCO_LOADER } from '@jsverse/transloco';
//#endif
import { PaginationState, SortingState } from '@tanstack/angular-table';
import { BehaviorSubject } from 'rxjs';

import { UserTable } from './user-table';
import { AuthService } from '../../../../../../core/services/auth-service';
import { UserManagementOutputDto } from '../../../../models/user-management.dto';

/**
 * 用户表格的交互状态。
 *
 * 分页与排序都是 manual 模式：表格自己不切数据，只把意图回传给父级去取下一页。
 * 因此这里验的是"回传了什么"，而不是"表格里剩几行"——把这两件事搞混，
 * 就会出现界面翻了页、请求却没换参数。
 */
describe('UserTable', () => {
  let fixture: ComponentFixture<UserTable>;
  let component: UserTable;
  let viewport: BehaviorSubject<BreakpointState>;

  const desktop = (matches: boolean): BreakpointState => ({
    matches,
    breakpoints: {
      '(min-width: 768px)': matches,
      '(min-width: 1024px)': matches,
    },
  });

  function user(id: string, username: string): UserManagementOutputDto {
    return {
      id,
      username,
      email: `${username}@example.test`,
      displayName: username,
      isActive: true,
      emailConfirmed: true,
      creationTime: '2026-01-01T00:00:00Z',
      roles: [],
    } as unknown as UserManagementOutputDto;
  }

  beforeEach(async () => {
    viewport = new BehaviorSubject<BreakpointState>(desktop(true));

    await TestBed.configureTestingModule({
      imports: [UserTable],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { currentUser: signal(null) } },
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

    fixture = TestBed.createComponent(UserTable);
    component = fixture.componentInstance;

    fixture.componentRef.setInput('users', [user('1', 'alice'), user('2', 'bob')]);
    fixture.componentRef.setInput('totalCount', 42);
    fixture.componentRef.setInput('pagination', { pageIndex: 0, pageSize: 20 } as PaginationState);
    fixture.detectChanges();
  });

  it('当前页与总页数按父级传入的分页状态派生', () => {
    expect(component.currentPage()).toBe(1);
    expect(component.totalPages()).toBe(3); // 42 条 / 每页 20

    fixture.componentRef.setInput('pagination', { pageIndex: 2, pageSize: 20 } as PaginationState);
    fixture.detectChanges();

    expect(component.currentPage()).toBe(3);
  });

  it('没有数据时总页数仍为 1，不出现"第 1 / 0 页"', () => {
    fixture.componentRef.setInput('users', []);
    fixture.componentRef.setInput('totalCount', 0);
    fixture.detectChanges();

    expect(component.totalPages()).toBe(1);
  });

  it('改每页条数时回到第一页', () => {
    fixture.componentRef.setInput('pagination', { pageIndex: 3, pageSize: 20 } as PaginationState);
    fixture.detectChanges();

    const emitted: PaginationState[] = [];
    component.paginationChange.subscribe((value) => emitted.push(value));

    component.changePageSize(50);

    // 停在第 4 页却换成每页 50 条，多半越界，用户看到的是一片空白。
    expect(emitted).toEqual([{ pageIndex: 0, pageSize: 50 }]);
  });

  it('点击排序在升序与降序之间切换，并回传排序意图', () => {
    const emitted: SortingState[] = [];
    component.sortingChange.subscribe((value) => emitted.push(value));

    component.toggleSort('username');
    expect(emitted[0]).toEqual([{ id: 'username', desc: false }]);

    // 表格是受控的：把上一轮的排序回灌进去，才能模拟父级更新后的下一次点击。
    fixture.componentRef.setInput('sorting', emitted[0]);
    fixture.detectChanges();

    component.toggleSort('username');
    expect(emitted[1]).toEqual([{ id: 'username', desc: true }]);
  });

  it('排序图标与 aria 状态跟随当前排序', () => {
    expect(component.sortIcon('username')).toBe('lucideArrowUpDown');
    expect(component.sortAria('username')).toBe('none');

    fixture.componentRef.setInput('sorting', [{ id: 'username', desc: false }] as SortingState);
    fixture.detectChanges();
    expect(component.sortIcon('username')).toBe('lucideSortAsc');
    expect(component.sortAria('username')).toBe('ascending');

    fixture.componentRef.setInput('sorting', [{ id: 'username', desc: true }] as SortingState);
    fixture.detectChanges();
    expect(component.sortIcon('username')).toBe('lucideSortDesc');
    expect(component.sortAria('username')).toBe('descending');
  });

  it('一个可用操作都没有时不渲染溢出菜单', () => {
    fixture.componentRef.setInput('canUpdate', false);
    fixture.componentRef.setInput('canDelete', false);
    //#if (LocalAuthorization)
    fixture.componentRef.setInput('canManageRoles', false);
    //#endif
    fixture.detectChanges();

    // 点开即空的按钮比没有按钮更糟：它承诺了一个并不存在的能力。
    expect(component.hasRowActions()).toBeFalse();

    fixture.componentRef.setInput('canDelete', true);
    fixture.detectChanges();
    expect(component.hasRowActions()).toBeTrue();
  });

  it('窄视口下标记存在被折叠的列，桌面端不标记', () => {
    expect(component.hasCollapsedColumns()).toBeFalse();

    viewport.next(desktop(false));
    fixture.detectChanges();

    // 列被藏起来却不给展开入口，那些字段就等于从界面上消失了。
    expect(component.hasCollapsedColumns()).toBeTrue();
  });

  it('行展开状态按行独立记录', () => {
    expect(component.isRowExpanded('1')).toBeFalse();

    component.toggleRow('1');
    expect(component.isRowExpanded('1')).toBeTrue();
    expect(component.isRowExpanded('2')).toBeFalse();

    component.toggleRow('1');
    expect(component.isRowExpanded('1')).toBeFalse();
  });
});
