import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { PaginationState, SortingState } from '@tanstack/angular-table';
import { of } from 'rxjs';

import { Users } from './users';
import { UserTable } from './widgets/user-table/user-table';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../core/i18n/transloco.testing';
//#endif
import { AuthService } from '../../../../core/services/auth-service';
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { StartupService } from '../../../../core/services/startup-service';
import { PERMISSIONS } from '../../../../shared/models/permission';
import { GetUsersInputDto } from '../../models/user-management.dto';
import { UserManagementService } from '../../services/user-management-service';

/**
 * 用户页面的查询闭环：子表事件 → URL query → 列表请求参数。
 *
 * 子表用例只证明组件内部计算正确，证明不了父页面这一层——模板绑定接错、
 * query 键写错、DTO 映射漏字段，子表照样全绿，而界面上翻页翻不动、筛选不生效。
 */
describe('Users 页面查询闭环', () => {
  let fixture: ComponentFixture<Users>;
  let component: Users;
  let router: Router;
  let service: jasmine.SpyObj<UserManagementService>;

  /** 最近一次列表请求的参数。 */
  function lastQuery(): GetUsersInputDto {
    const calls = service.getUsers.calls.all();
    const query = calls[calls.length - 1]?.args[0];
    if (!query) {
      throw new Error('列表请求从未发出');
    }

    return query;
  }

  /** 真实的子表实例：事件必须从它的 output 发出，才能覆盖模板里的绑定名。 */
  function table(): UserTable {
    return fixture.debugElement.query(By.directive(UserTable)).componentInstance as UserTable;
  }

  beforeEach(async () => {
    service = jasmine.createSpyObj<UserManagementService>('UserManagementService', ['getUsers']);
    service.getUsers.and.returnValue(of({ items: [], totalCount: 0 }) as never);

    await TestBed.configureTestingModule({
      imports: [Users],
      providers: [
        provideRouter([{ path: 'platform/users', children: [] }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { currentUser: signal(null) } },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
        { provide: UserManagementService, useValue: service },
        { provide: StartupService, useValue: { status: signal('success' as const) } },
      ],
    }).compileComponents();

    router = TestBed.inject(Router);
    await router.navigate(['/platform/users']);

    TestBed.inject(AuthorizationService).setPermissions({
      permissions: [PERMISSIONS.users.default],
      isSuperAdmin: false,
      versionToken: 'r1',
    });

    fixture = TestBed.createComponent(Users);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('翻页写进 URL，并按新页码重新请求', async () => {
    table().paginationChange.emit({ pageIndex: 2, pageSize: 20 } as PaginationState);
    await fixture.whenStable();

    // 页码在 URL 里是 1 基（可分享、可前进后退），发给接口的是 offset。
    expect(router.url).toContain('page=3');
    expect(lastQuery().offset).toBe(40);
    expect(lastQuery().limit).toBe(20);
  });

  it('改每页条数回到第一页，请求的 offset 随之归零', async () => {
    table().paginationChange.emit({ pageIndex: 3, pageSize: 20 } as PaginationState);
    await fixture.whenStable();

    table().paginationChange.emit({ pageIndex: 0, pageSize: 50 } as PaginationState);
    await fixture.whenStable();

    expect(lastQuery().offset).toBe(0);
    expect(lastQuery().limit).toBe(50);
  });

  it('排序写进 URL 并回到第一页，转成接口排序参数', async () => {
    table().paginationChange.emit({ pageIndex: 2, pageSize: 20 } as PaginationState);
    await fixture.whenStable();

    table().sortingChange.emit([{ id: 'username', desc: true }] as SortingState);
    await fixture.whenStable();

    // 停在第 3 页换排序，看到的是另一批数据的第 3 页，等于结果错乱。
    expect(router.url).toContain('page=1');
    expect(lastQuery().offset).toBe(0);
    expect(lastQuery().sorting).toBeTruthy();
  });

  it('URL 状态回填组件：刷新与前进后退可复原', async () => {
    await router.navigate(['/platform/users'], {
      queryParams: { page: 2, pageSize: 50, keyword: 'alice' },
    });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.pagination()).toEqual(
      jasmine.objectContaining({ pageIndex: 1, pageSize: 50 }),
    );
    expect(lastQuery().keyword).toBe('alice');
  });
});
