import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
// prettier-ignore
import {
  signal,
  //#if (IncludeRealTime)
  type DestroyRef,
  //#endif
} from '@angular/core';
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
//#if (IncludeRealTime)
import { SignalRService } from '../../../../core/services/signalr-service';
//#endif
import { StartupService } from '../../../../core/services/startup-service';
import { SettingContextService } from '../../../../core/settings/setting-context-service';
import { PERMISSIONS } from '../../../../shared/models/permission';
import { GetRolesInputDto, RoleOutputDto } from '../../dtos/role.dto';
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
  //#if (IncludeRealTime)
  const realtime = {
    lastResourceEvent: signal<{ eventName: string; payload: unknown } | null>(null),
    registerResourceEvent: vi.fn(),
    connect: vi.fn(() => Promise.resolve()),
    watchResource: vi.fn<(resourceKey: string, destroyRef?: DestroyRef) => void>(),
    resourceSubscribed$: new Subject<string>(),
  };
  //#endif

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
        //#if (IncludeRealTime)
        { provide: SignalRService, useValue: realtime },
        //#endif
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
  //#if (IncludeRealTime)

  it('subscribes to the role list of its own scope and refetches when it changes', async () => {
    await fixture.whenStable();
    // 宿主用户：作用域段为 host，与后端 ICurrentTenant.ScopeKey 一致
    expect(realtime.registerResourceEvent).toHaveBeenCalledWith('Roles.Changed');
    expect(realtime.watchResource).toHaveBeenCalledWith('host:roles', expect.anything());
    expect(realtime.connect).toHaveBeenCalled();

    const before = vi.mocked(service.getRoles).mock.calls.length;
    realtime.lastResourceEvent.set({ eventName: 'Roles.Changed', payload: {} });
    fixture.detectChanges();
    await fixture.whenStable();

    expect(vi.mocked(service.getRoles).mock.calls.length).toBe(before + 1);
  });

  it('refetches once its own role list subscription is confirmed, such as after reconnecting', async () => {
    await fixture.whenStable();
    const before = vi.mocked(service.getRoles).mock.calls.length;

    // 断线期间的变更不会补推：订阅恢复确认后补查一次
    realtime.resourceSubscribed$.next('host:roles');
    await fixture.whenStable();
    expect(vi.mocked(service.getRoles).mock.calls.length).toBe(before + 1);

    // 别的资源恢复与本页无关
    realtime.resourceSubscribed$.next('host:users');
    await fixture.whenStable();
    expect(vi.mocked(service.getRoles).mock.calls.length).toBe(before + 1);
  });

  it('holds the subscription for exactly the lifetime of the page', async () => {
    await fixture.whenStable();
    // 交给服务的是本页的 DestroyRef：页面销毁即撤销，连接前离开也由服务保证不再订阅
    const destroyRef = realtime.watchResource.mock.calls.at(-1)?.[1];
    expect(destroyRef?.destroyed).toBe(false);

    fixture.destroy();

    expect(destroyRef?.destroyed).toBe(true);
  });
  //#endif
});
