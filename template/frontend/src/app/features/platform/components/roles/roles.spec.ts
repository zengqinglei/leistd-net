import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { PaginationState, SortingState } from '@tanstack/angular-table';
import { of, Subject } from 'rxjs';

import { Roles } from './roles';
import { RoleTable } from './widgets/role-table/role-table';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../core/i18n/transloco.testing';
//#endif
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { StartupService } from '../../../../core/services/startup-service';
import { SettingContextService } from '../../../../core/settings/setting-context-service';
import { PERMISSIONS } from '../../../../shared/models/permission';
import { GetRolesInputDto, RoleOutputDto } from '../../models/role.dto';
import { RoleService } from '../../services/role-service';

import type { MockedObject } from 'vitest';

/**
 * 角色页面的查询闭环。与用户页各写一份：两个页面各自实现这一层，
 * 其中一个接线写错，另一个的用例不会有任何反应。
 */
describe('Roles page query round trip', () => {
  let fixture: ComponentFixture<Roles>;
  let component: Roles;
  let router: Router;
  let service: Pick<MockedObject<RoleService>, 'getRoles'>;

  /** 最近一次列表请求的参数。 */
  function lastQuery(): GetRolesInputDto {
    const calls = vi.mocked(service.getRoles).mock.calls;
    const query = calls.at(-1)?.[0];
    if (!query) {
      throw new Error('列表请求从未发出');
    }

    return query;
  }

  /** 真实的子表实例：事件必须从它的 output 发出，才能覆盖模板里的绑定名。 */
  function table(): RoleTable {
    return fixture.debugElement.query(By.directive(RoleTable)).componentInstance as RoleTable;
  }

  beforeEach(async () => {
    service = {
      getRoles: vi.fn().mockName('RoleService.getRoles'),
    };
    service.getRoles.mockReturnValue(of({ items: [], totalCount: 0 }) as never);

    await TestBed.configureTestingModule({
      imports: [Roles],
      providers: [
        provideRouter([{ path: 'platform/roles', children: [] }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
        { provide: RoleService, useValue: service },
        { provide: StartupService, useValue: { status: signal('success' as const) } },
      ],
    }).compileComponents();

    router = TestBed.inject(Router);
    await router.navigate(['/platform/roles']);

    TestBed.inject(AuthorizationService).setPermissions({
      permissions: [PERMISSIONS.roles.default],
      isSuperAdmin: false,
      versionToken: 'r1',
    });

    // 应用里设置上下文（连带语言服务）在启动流中就已创建，首帧之前语言已经激活。
    // 这里同样先建好：否则它要到页面首次渲染途中才被子表格注入，构造时激活语言会让
    // 页面外层的 *transloco 在视图还没建完时再建一次。
    TestBed.inject(SettingContextService);
    fixture = TestBed.createComponent(Roles);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('writes paging to the URL and refetches the new page', async () => {
    table().paginationChange.emit({ pageIndex: 2, pageSize: 20 } as PaginationState);
    await fixture.whenStable();

    // 页码在 URL 里是 1 基（可分享、可前进后退），发给接口的是 offset。
    expect(router.url).toContain('page=3');
    expect(lastQuery().offset).toBe(40);
    expect(lastQuery().limit).toBe(20);
  });

  it('resets to the first page and offset 0 when rows per page changes', async () => {
    table().paginationChange.emit({ pageIndex: 3, pageSize: 20 } as PaginationState);
    await fixture.whenStable();

    table().paginationChange.emit({ pageIndex: 0, pageSize: 50 } as PaginationState);
    await fixture.whenStable();

    expect(lastQuery().offset).toBe(0);
    expect(lastQuery().limit).toBe(50);
  });

  it('writes sorting to the URL, resets to page one and maps it to the API sort', async () => {
    table().paginationChange.emit({ pageIndex: 2, pageSize: 20 } as PaginationState);
    await fixture.whenStable();

    table().sortingChange.emit([{ id: 'displayName', desc: true }] as SortingState);
    await fixture.whenStable();

    // 停在第 3 页换排序，看到的是另一批数据的第 3 页，等于结果错乱。
    expect(router.url).toContain('page=1');
    expect(lastQuery().offset).toBe(0);
    expect(lastQuery().sorting).toBeTruthy();
  });

  it('restores component state from the URL on reload and back/forward navigation', async () => {
    await router.navigate(['/platform/roles'], {
      queryParams: { page: 2, pageSize: 50, keyword: 'admin' },
    });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.pagination()).toEqual(expect.objectContaining({ pageIndex: 1, pageSize: 50 }));
    expect(lastQuery().keyword).toBe('admin');
  });

  it('keeps loading on while a superseded request is cancelled and the new one is in flight', async () => {
    const first = new Subject<{ items: RoleOutputDto[]; totalCount: number }>();
    const second = new Subject<{ items: RoleOutputDto[]; totalCount: number }>();
    service.getRoles.mockReturnValueOnce(first as never).mockReturnValueOnce(second as never);

    table().paginationChange.emit({ pageIndex: 1, pageSize: 20 } as PaginationState);
    await fixture.whenStable();
    expect(component.loading()).toBe(true);

    // 第一页还没返回就翻到下一页：旧请求被取消，它的 finalize 不能把新请求的加载状态关掉。
    table().paginationChange.emit({ pageIndex: 2, pageSize: 20 } as PaginationState);
    await fixture.whenStable();
    expect(component.loading()).toBe(true);

    second.next({ items: [], totalCount: 0 });
    second.complete();
    expect(component.loading()).toBe(false);
  });
});
