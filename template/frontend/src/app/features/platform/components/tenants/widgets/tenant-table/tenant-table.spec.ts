//#if (TenancyEnabled)
import { BreakpointObserver, BreakpointState } from '@angular/cdk/layout';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
//#if (IncludeLocalization)
import { provideTransloco, TRANSLOCO_LOADER } from '@jsverse/transloco';
//#endif
import { PaginationState } from '@tanstack/angular-table';
import { BehaviorSubject } from 'rxjs';

import { TenantTable } from './tenant-table';
import { TenantOutputDto } from '../../../../../../shared/dtos/tenant.dto';

/**
 * 租户表格与用户/角色表格共用同一套受控分页与列收纳约定，这里覆盖同样的关键路径。
 * 三张表各自实现这一层，其中一张语义漂移不会有任何东西报错，只能靠各自的用例钉住。
 *
 * 租户列表的后端契约只有 offset/limit/keyword，没有排序，因此这里不存在排序用例。
 */
describe('TenantTable', () => {
  let fixture: ComponentFixture<TenantTable>;
  let component: TenantTable;
  let viewport: BehaviorSubject<BreakpointState>;

  const viewportState = (medium: boolean, large: boolean): BreakpointState => ({
    matches: medium || large,
    breakpoints: {
      '(min-width: 768px)': medium,
      '(min-width: 1024px)': large,
    },
  });

  function tenant(id: string, name: string): TenantOutputDto {
    return {
      id,
      name,
      displayName: name.toUpperCase(),
      isActive: true,
      creationTime: '2026-01-01T00:00:00Z',
    } as unknown as TenantOutputDto;
  }

  beforeEach(async () => {
    viewport = new BehaviorSubject<BreakpointState>(viewportState(true, true));

    await TestBed.configureTestingModule({
      imports: [TenantTable],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        //#if (IncludeLocalization)
        // 模板用了 transloco 管道：配真实 provider 加空加载器，文案回落成键名，
        // 本组用例关心的是分页与列可见性状态，不是具体文案。
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

    fixture = TestBed.createComponent(TenantTable);
    component = fixture.componentInstance;

    fixture.componentRef.setInput('tenants', [tenant('1', 'acme'), tenant('2', 'globex')]);
    fixture.componentRef.setInput('totalCount', 45);
    fixture.componentRef.setInput('pagination', { pageIndex: 0, pageSize: 20 } as PaginationState);
    fixture.detectChanges();
  });

  it('当前页与总页数按父级传入的分页状态派生', () => {
    expect(component.currentPage()).toBe(1);
    expect(component.totalPages()).toBe(3); // 45 条 / 每页 20

    fixture.componentRef.setInput('pagination', { pageIndex: 2, pageSize: 20 } as PaginationState);
    fixture.detectChanges();

    expect(component.currentPage()).toBe(3);
  });

  it('没有数据时总页数仍为 1', () => {
    fixture.componentRef.setInput('tenants', []);
    fixture.componentRef.setInput('totalCount', 0);
    fixture.detectChanges();

    // 分页栏写着「第 1 / 0 页」，看上去像是数据加载失败了。
    expect(component.totalPages()).toBe(1);
  });

  it('改每页条数时回到第一页', () => {
    fixture.componentRef.setInput('pagination', { pageIndex: 2, pageSize: 20 } as PaginationState);
    fixture.detectChanges();

    const emitted: PaginationState[] = [];
    component.paginationChange.subscribe((value) => emitted.push(value));

    component.changePageSize(50);

    // 停在第 3 页却换成每页 50 条，多半越界，用户看到的是一片空白。
    expect(emitted).toEqual([{ pageIndex: 0, pageSize: 50 }]);
  });

  it('一个可用操作都没有时不渲染溢出菜单', () => {
    fixture.componentRef.setInput('canUpdate', false);
    fixture.componentRef.setInput('canDelete', false);
    fixture.detectChanges();

    // 点开即空的按钮比没有按钮更糟：它承诺了一个并不存在的能力。
    expect(component.hasRowActions()).toBeFalse();

    fixture.componentRef.setInput('canDelete', true);
    fixture.detectChanges();
    expect(component.hasRowActions()).toBeTrue();
  });

  it('窄视口下标记存在被折叠的列，桌面端不标记', () => {
    expect(component.hasCollapsedColumns()).toBeFalse();

    viewport.next(viewportState(false, false));
    fixture.detectChanges();

    // 列被藏起来却不给展开入口，那些字段就等于从界面上消失了。
    expect(component.hasCollapsedColumns()).toBeTrue();
  });

  it('列按优先级逐级收起，锁定列在任何视口都保留', () => {
    expect(component.isColumnHidden('displayName')).toBeFalse();
    expect(component.isColumnHidden('creationTime')).toBeFalse();

    viewport.next(viewportState(true, false));
    fixture.detectChanges();

    // 平板先让三级列（创建时间）出局，二级列还留着。
    expect(component.isColumnHidden('creationTime')).toBeTrue();
    expect(component.isColumnHidden('displayName')).toBeFalse();

    viewport.next(viewportState(false, false));
    fixture.detectChanges();

    expect(component.isColumnHidden('displayName')).toBeTrue();
    // 名称是租户的业务标识、操作列是唯一入口，两者收起来这张表就没用了。
    expect(component.isColumnHidden('name')).toBeFalse();
    expect(component.isColumnHidden('actions')).toBeFalse();
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
//#endif
