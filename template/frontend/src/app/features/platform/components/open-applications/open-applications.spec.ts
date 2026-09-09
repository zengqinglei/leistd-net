import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { PaginationState, SortingState } from '@tanstack/angular-table';
import { of } from 'rxjs';

import { OpenApplications } from './open-applications';
import { OpenApplicationTable } from './widgets/open-application-table/open-application-table';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../core/i18n/transloco.testing';
//#endif
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { StartupService } from '../../../../core/services/startup-service';
import { PERMISSIONS } from '../../../../shared/models/permission';
import { GetOpenApplicationsInputDto } from '../../models/open-application.dto';
import { OpenApplicationService } from '../../services/open-application-service';

/** 开放应用页面的查询闭环。 */
describe('OpenApplications 页面查询闭环', () => {
  let fixture: ComponentFixture<OpenApplications>;
  let component: OpenApplications;
  let router: Router;
  let service: jasmine.SpyObj<OpenApplicationService>;

  /** 最近一次列表请求的参数。 */
  function lastQuery(): GetOpenApplicationsInputDto {
    const calls = service.getOpenApplications.calls.all();
    const query = calls[calls.length - 1]?.args[0];
    if (!query) {
      throw new Error('列表请求从未发出');
    }

    return query;
  }

  /** 真实的子表实例：事件必须从它的 output 发出，才能覆盖模板里的绑定名。 */
  function table(): OpenApplicationTable {
    return fixture.debugElement.query(By.directive(OpenApplicationTable))
      .componentInstance as OpenApplicationTable;
  }

  beforeEach(async () => {
    service = jasmine.createSpyObj<OpenApplicationService>('OpenApplicationService', [
      'getOpenApplications',
    ]);
    service.getOpenApplications.and.returnValue(of({ items: [], totalCount: 0 }) as never);

    await TestBed.configureTestingModule({
      imports: [OpenApplications],
      providers: [
        provideRouter([{ path: 'platform/open-applications', children: [] }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
        { provide: OpenApplicationService, useValue: service },
        { provide: StartupService, useValue: { status: signal('success' as const) } },
      ],
    }).compileComponents();

    router = TestBed.inject(Router);
    await router.navigate(['/platform/open-applications']);

    TestBed.inject(AuthorizationService).setPermissions({
      permissions: [PERMISSIONS.openApplications.default],
      isSuperAdmin: false,
      versionToken: 'r1',
    });

    fixture = TestBed.createComponent(OpenApplications);
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

    table().sortingChange.emit([{ id: 'displayName', desc: true }] as SortingState);
    await fixture.whenStable();

    // 停在第 3 页换排序，看到的是另一批数据的第 3 页，等于结果错乱。
    expect(router.url).toContain('page=1');
    expect(lastQuery().offset).toBe(0);
    expect(lastQuery().sorting).toBeTruthy();
  });

  it('Secret 弹窗关闭后不再持有上一次的 secret', () => {
    // 关闭弹窗只更新 visible 时，secret 与标题会留在组件状态里，
    // 下一次误打开弹窗会显示上一次生成的 secret。
    component.secretValue.set('generated-secret');
    component.secretHeader.set('Client created');
    component.secretDialogVisible.set(true);

    component.onSecretDialogVisibleChange(false);

    expect(component.secretDialogVisible()).toBeFalse();
    expect(component.secretValue()).toBe('');
    expect(component.secretHeader()).toBe('');
  });

  it('Secret 弹窗打开时不清空 secret', () => {
    component.secretValue.set('generated-secret');
    component.secretHeader.set('Client created');

    component.onSecretDialogVisibleChange(true);

    expect(component.secretDialogVisible()).toBeTrue();
    expect(component.secretValue()).toBe('generated-secret');
  });

  it('URL 状态回填组件：刷新与前进后退可复原', async () => {
    await router.navigate(['/platform/open-applications'], {
      queryParams: { page: 2, pageSize: 50, keyword: 'probe' },
    });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.pagination()).toEqual(
      jasmine.objectContaining({ pageIndex: 1, pageSize: 50 }),
    );
    expect(lastQuery().keyword).toBe('probe');
  });
});
