//#if (MultiTenancy)
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
//#if (IncludeLocalization)
import { provideTransloco, TRANSLOCO_LOADER } from '@jsverse/transloco';
//#endif
import { PaginationState } from '@tanstack/angular-table';
import { of, throwError } from 'rxjs';

import { Tenants } from './tenants';
import { TenantEditDialog } from './widgets/tenant-edit-dialog/tenant-edit-dialog';
import { TenantTable } from './widgets/tenant-table/tenant-table';
import { ConfirmService } from '../../../../core/feedback/confirm-service';
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { StartupService } from '../../../../core/services/startup-service';
import {
  CreateTenantInputDto,
  GetTenantsInputDto,
  TenantOutputDto,
} from '../../../../shared/dtos/tenant.dto';
import { PERMISSIONS } from '../../../../shared/models/permission';
import { TenantService } from '../../services/tenant-service';

/**
 * 租户页面的查询与写操作闭环。与用户/角色页各写一份：三个页面各自实现这一层，
 * 其中一个接线写错，另外两个的用例不会有任何反应。
 */
describe('Tenants 页面闭环', () => {
  let fixture: ComponentFixture<Tenants>;
  let component: Tenants;
  let router: Router;
  let service: jasmine.SpyObj<TenantService>;
  let confirm: jasmine.SpyObj<ConfirmService>;

  const tenant: TenantOutputDto = {
    id: '019ff8ed-221b-7673-9ba8-6b6dd5a638ab',
    name: 'acme',
    displayName: 'Acme Inc.',
    isActive: true,
    creationTime: '2026-08-14T00:00:00Z',
  };

  /** 最近一次列表请求的参数。 */
  function lastQuery(): GetTenantsInputDto {
    const calls = service.getTenants.calls.all();
    const query = calls[calls.length - 1]?.args[0];
    if (!query) {
      throw new Error('列表请求从未发出');
    }

    return query;
  }

  /** 真实的子表实例：事件必须从它的 output 发出，才能覆盖模板里的绑定名。 */
  function table(): TenantTable {
    return fixture.debugElement.query(By.directive(TenantTable)).componentInstance as TenantTable;
  }

  /** 真实的编辑对话框实例：同理，保存事件要从它发出才覆盖 (save) 绑定。 */
  function editDialog(): TenantEditDialog {
    return fixture.debugElement.query(By.directive(TenantEditDialog))
      .componentInstance as TenantEditDialog;
  }

  const newTenantPayload: CreateTenantInputDto = {
    name: 'globex',
    displayName: 'Globex Corp.',
    adminEmail: 'admin@globex.example.com',
    adminPassword: 'Globex@123456',
    databaseMode: 'SharedDatabase',
  };

  beforeEach(async () => {
    service = jasmine.createSpyObj<TenantService>('TenantService', [
      'getTenants',
      'createTenant',
      'updateTenant',
      'setActivation',
      'deleteTenant',
    ]);
    service.getTenants.and.returnValue(of({ items: [tenant], totalCount: 1 }) as never);
    service.createTenant.and.returnValue(of(tenant) as never);
    service.updateTenant.and.returnValue(of(tenant) as never);
    service.setActivation.and.returnValue(of(tenant) as never);
    service.deleteTenant.and.returnValue(of(undefined) as never);

    confirm = jasmine.createSpyObj<ConfirmService>('ConfirmService', ['open']);
    confirm.open.and.resolveTo(true);

    await TestBed.configureTestingModule({
      imports: [Tenants],
      providers: [
        provideRouter([{ path: 'platform/tenants', children: [] }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        //#if (IncludeLocalization)
        provideTransloco({
          config: { availableLangs: ['en'], defaultLang: 'en', fallbackLang: 'en' },
        }),
        { provide: TRANSLOCO_LOADER, useValue: { getTranslation: () => Promise.resolve({}) } },
        //#endif
        { provide: TenantService, useValue: service },
        { provide: ConfirmService, useValue: confirm },
        { provide: StartupService, useValue: { status: signal('success' as const) } },
      ],
    }).compileComponents();

    router = TestBed.inject(Router);
    await router.navigate(['/platform/tenants']);

    TestBed.inject(AuthorizationService).setPermissions({
      permissions: [
        PERMISSIONS.tenants.default,
        PERMISSIONS.tenants.update,
        PERMISSIONS.tenants.delete,
      ],
      isSuperAdmin: false,
      revision: 'r1',
    });

    fixture = TestBed.createComponent(Tenants);
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

  it('URL 状态回填组件：刷新与前进后退可复原', async () => {
    await router.navigate(['/platform/tenants'], {
      queryParams: { page: 2, pageSize: 50, keyword: 'acme' },
    });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.pagination()).toEqual(
      jasmine.objectContaining({ pageIndex: 1, pageSize: 50 }),
    );
    expect(lastQuery().keyword).toBe('acme');
  });

  it('搜索经防抖写进 URL 并回到第一页', async () => {
    table().paginationChange.emit({ pageIndex: 3, pageSize: 20 } as PaginationState);
    await fixture.whenStable();

    component.onSearchQueryChange('acme');
    await new Promise((resolve) => setTimeout(resolve, 500));
    await fixture.whenStable();

    // 停在第 4 页换关键字，看到的是另一批结果的第 4 页，等于结果错乱。
    expect(router.url).toContain('keyword=acme');
    expect(lastQuery().offset).toBe(0);
  });

  it('启停经确认后调用接口并刷新列表', async () => {
    const before = service.getTenants.calls.count();

    table().toggleActive.emit(tenant);
    await fixture.whenStable();

    expect(confirm.open).toHaveBeenCalled();
    expect(service.setActivation).toHaveBeenCalledWith(tenant.id, false);
    expect(service.getTenants.calls.count()).toBeGreaterThan(before);
  });

  it('新建保存走创建接口，成功后关闭对话框并刷新列表', async () => {
    const before = service.getTenants.calls.count();

    component.openCreate();
    fixture.detectChanges();

    editDialog().save.emit(newTenantPayload);
    await fixture.whenStable();

    // 创建与更新走同一个 (save) 出口，选错分支会把新建打成"更新一个不存在的租户"
    expect(service.createTenant).toHaveBeenCalledWith(newTenantPayload);
    expect(service.updateTenant).not.toHaveBeenCalled();

    expect(component.editDialogOpen()).toBeFalse();
    expect(service.getTenants.calls.count()).toBeGreaterThan(before);
  });

  it('编辑保存带上被编辑租户的 Id 走更新接口', async () => {
    component.openEdit(tenant);
    fixture.detectChanges();

    const payload = { name: 'acme', displayName: 'Acme Renamed' };
    editDialog().save.emit(payload);
    await fixture.whenStable();

    expect(service.updateTenant).toHaveBeenCalledWith(tenant.id, payload);
    expect(service.createTenant).not.toHaveBeenCalled();
    expect(component.editDialogOpen()).toBeFalse();
  });

  it('保存失败时对话框保持打开，不丢用户已填内容', async () => {
    service.createTenant.and.returnValue(throwError(() => new Error('boom')) as never);

    component.openCreate();
    fixture.detectChanges();

    editDialog().save.emit(newTenantPayload);
    await fixture.whenStable();

    // 关掉对话框等于连同用户填的表单一起丢掉，只能报错并留在原地
    expect(component.editDialogOpen()).toBeTrue();
  });

  it('确认删除后调用接口并刷新列表', async () => {
    const before = service.getTenants.calls.count();

    table().delete.emit(tenant);
    await fixture.whenStable();

    expect(service.deleteTenant).toHaveBeenCalledWith(tenant.id);
    expect(service.getTenants.calls.count()).toBeGreaterThan(before);
  });

  it('取消删除确认时不调用接口', async () => {
    confirm.open.and.resolveTo(false);

    table().delete.emit(tenant);
    await fixture.whenStable();

    expect(service.deleteTenant).not.toHaveBeenCalled();
  });
});
//#endif
