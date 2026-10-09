import { BreakpointObserver, BreakpointState } from '@angular/cdk/layout';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, DeferBlockState, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { PaginationState, SortingState } from '@tanstack/angular-table';
import { BehaviorSubject } from 'rxjs';
import { page } from 'vitest/browser';

import { UserTable } from './user-table';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../core/i18n/transloco.testing';
//#endif
import { AuthService } from '../../../../../../core/services/auth-service';
import { UserManagementOutputDto } from '../../../../dtos/user-management.dto';

/** 用户表格的交互状态：分页与排序是 manual 模式，验证回传给父级的意图，而不是表格里剩几行。 */
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
        ...provideTranslocoTesting(['en']),
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

  //#if (LocalIdentity)
  it('shows a locked badge only on locked-out users', async () => {
    fixture.componentRef.setInput('users', [
      { ...user('1', 'alice'), isLockedOut: true, lockoutEnd: '2026-01-01T00:15:00Z' },
      user('2', 'bob'),
    ]);
    // 表格包在 @defer 里，测试环境不会自己渲染它
    const [table] = await fixture.getDeferBlocks();
    await table.render(DeferBlockState.Complete);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const badges = host.querySelectorAll('[data-testid="user-locked"]');
    expect(badges.length).toBe(1);
    expect(badges[0].closest('tr')?.textContent).toContain('alice');
  });

  //#endif
  it('derives the current page and total pages from the parent pagination state', () => {
    expect(component.currentPage()).toBe(1);
    expect(component.totalPages()).toBe(3); // 42 条 / 每页 20

    fixture.componentRef.setInput('pagination', { pageIndex: 2, pageSize: 20 } as PaginationState);
    fixture.detectChanges();

    expect(component.currentPage()).toBe(3);
  });

  // 不出现"第 1 / 0 页"
  it('keeps total pages at 1 when there is no data', () => {
    fixture.componentRef.setInput('users', []);
    fixture.componentRef.setInput('totalCount', 0);
    fixture.detectChanges();

    expect(component.totalPages()).toBe(1);
  });

  it('returns to the first page when the page size changes', () => {
    fixture.componentRef.setInput('pagination', { pageIndex: 3, pageSize: 20 } as PaginationState);
    fixture.detectChanges();

    const emitted: PaginationState[] = [];
    component.paginationChange.subscribe((value) => emitted.push(value));

    component.changePageSize(50);

    // 停在第 4 页却换成每页 50 条，多半越界，用户看到的是一片空白。
    expect(emitted).toEqual([{ pageIndex: 0, pageSize: 50 }]);
  });

  it('toggles between ascending and descending on sort and emits the sorting intent', () => {
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

  it('updates the sort icon and aria state with the current sorting', () => {
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

  it('hides the overflow menu when no row action is available', () => {
    fixture.componentRef.setInput('canUpdate', false);
    fixture.componentRef.setInput('canDelete', false);
    fixture.componentRef.setInput('canManageRoles', false);
    fixture.detectChanges();

    // 点开即空的按钮比没有按钮更糟：它承诺了一个并不存在的能力。
    expect(component.hasRowActions()).toBe(false);

    fixture.componentRef.setInput('canDelete', true);
    fixture.detectChanges();
    expect(component.hasRowActions()).toBe(true);
  });

  // 停用是本服务对人员的本地处置，所有形态都随 Users.Update 提供
  it('offers enable or disable in the row menu only with the update permission', async () => {
    fixture.componentRef.setInput('users', [user('1', 'alice')]);
    fixture.componentRef.setInput('canDelete', false);
    fixture.componentRef.setInput('canManageRoles', false);
    // 表格包在 @defer 里，测试环境不会自己渲染它
    const [table] = await fixture.getDeferBlocks();
    await table.render(DeferBlockState.Complete);
    fixture.detectChanges();

    onTestFinished(() => document.querySelector('.cdk-overlay-container')?.remove());
    const menu = await openRowMenu();
    // 图标名是属性绑定，不反射成 attribute，按文案（或未装词条时的键名）找
    expect(menu.textContent).toMatch(/Disable|users\.tooltip\.disable/);
    //#if (!LocalIdentity)
    // 资料由签发方维护，本服务不提供编辑入口
    expect(menu.querySelector('ng-icon[name="lucidePencil"]')).toBeNull();
    expect(host().querySelector('ng-icon[name="lucidePencil"]')).toBeNull();
    //#else
    expect(menu.querySelector('ng-icon[name="lucidePencil"]')).not.toBeNull();
    //#endif

    fixture.componentRef.setInput('canUpdate', false);
    fixture.detectChanges();
    expect(rowMenuTrigger()).toBeNull();
  });

  it('hides the menu trigger on desktop when it would only repeat the inline buttons', async () => {
    await page.viewport(1280, 800);
    fixture.componentRef.setInput('users', [user('1', 'alice')]);
    fixture.componentRef.setInput('canUpdate', false);
    fixture.componentRef.setInput('canDelete', false);
    fixture.componentRef.setInput('canManageRoles', true);
    const [table] = await fixture.getDeferBlocks();
    await table.render(DeferBlockState.Complete);
    fixture.detectChanges();

    // 分配角色在桌面端是行内按钮，菜单里只有它的窄屏副本：桌面端点开会是空菜单
    expect(getComputedStyle(rowMenuTrigger()!).display).toBe('none');

    fixture.componentRef.setInput('canUpdate', true);
    fixture.detectChanges();
    expect(getComputedStyle(rowMenuTrigger()!).display).not.toBe('none');
  });

  it('flags collapsed columns on narrow viewports but not on desktop', () => {
    expect(component.hasCollapsedColumns()).toBe(false);

    viewport.next(desktop(false));
    fixture.detectChanges();

    // 列被藏起来却不给展开入口，那些字段就等于从界面上消失了。
    expect(component.hasCollapsedColumns()).toBe(true);
  });

  // 展开状态用 TanStack 的行展开，按行 id 记：数据刷新（同一批行换了新对象、换了顺序）后
  // 展开的仍是原来那一行。按下标记会让展开跟着位置走，数据一换就默认全部收起。
  it('keeps a row expanded by its id across data refreshes', async () => {
    viewport.next(desktop(false));
    // 表格包在 @defer 里，测试环境不会自己渲染它
    const [table] = await fixture.getDeferBlocks();
    await table.render(DeferBlockState.Complete);
    fixture.detectChanges();

    expandButtons()[0].click();
    await fixture.whenStable();
    expect(expandedStates()).toEqual(['true', 'false']);

    fixture.componentRef.setInput('users', [user('2', 'bob'), user('1', 'alice')]);
    await fixture.whenStable();
    expect(expandedStates()).toEqual(['false', 'true']);

    expandButtons()[1].click();
    await fixture.whenStable();
    expect(expandedStates()).toEqual(['false', 'false']);
  });

  // 最窄一档无列可收时主列截断，表格不横向滚动，状态列与操作列完整可见。
  it('truncates a long name instead of scrolling the table at the narrowest level', async () => {
    // 手机视口：`sm:` 断点（单元格内边距、行内操作按钮）也要按手机生效，只模拟档位不够
    await page.viewport(390, 844);
    onTestFinished(() => page.viewport(1280, 800));
    //#if (IncludeLocalization)
    // 测试不装词条时表头渲染成键名，比真实文案宽得多；量宽度要用真实长度的文案
    TestBed.inject(TranslocoService).setTranslation(
      { table: { colUser: 'User', colStatus: 'Status' }, status: { active: 'Active' } },
      'users/en',
    );
    //#endif
    const host = fixture.nativeElement as HTMLElement;
    host.style.display = 'block';
    host.style.width = '356px';
    viewport.next(desktop(false));
    fixture.componentRef.setInput('users', [
      { ...user('1', 'admin'), displayName: 'System Administrator of the Whole Organization' },
    ]);
    // 表格包在 @defer 里，测试环境不会自己渲染它
    const [table] = await fixture.getDeferBlocks();
    await table.render(DeferBlockState.Complete);
    fixture.autoDetectChanges();

    const scroller = host.querySelector<HTMLElement>('[hlmTableContainer]')!;
    await expect
      .poll(() => host.querySelector('[data-table-fit]')?.getAttribute('data-table-fit'))
      .toBe('mobile');
    await expect.poll(() => scroller.scrollWidth <= scroller.clientWidth).toBe(true);

    const name = host.querySelector<HTMLElement>('tbody span.truncate')!;
    expect(name.scrollWidth).toBeGreaterThan(name.clientWidth);
    const status = host.querySelector<HTMLElement>('tbody [hlmBadge]')!;
    const actions = host.querySelector<HTMLElement>('tbody td:last-child')!;
    expect(status.getBoundingClientRect().right).toBeLessThanOrEqual(
      actions.getBoundingClientRect().left,
    );
  });

  function host(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function rowMenuTrigger(): HTMLButtonElement | null {
    const icon = host().querySelector('tbody ng-icon[name="lucideEllipsis"]');
    return icon?.closest('button') ?? null;
  }

  /** 菜单经 CDK 浮层挂在 document 上，不在组件宿主里。 */
  async function openRowMenu(): Promise<HTMLElement> {
    rowMenuTrigger()!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    return document.querySelector<HTMLElement>('[data-slot="dropdown-menu"]')!;
  }

  function expandButtons(): HTMLButtonElement[] {
    const host = fixture.nativeElement as HTMLElement;
    return Array.from(host.querySelectorAll('tbody ng-icon[name="lucideChevronRight"]')).map(
      (icon) => icon.closest('button')!,
    );
  }

  function expandedStates(): (string | null)[] {
    return expandButtons().map((button) => button.getAttribute('aria-expanded'));
  }
});
