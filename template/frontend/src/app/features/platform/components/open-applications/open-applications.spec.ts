import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { PaginationState, SortingState } from '@tanstack/angular-table';
import { of, Subject } from 'rxjs';

import { OpenApplications } from './open-applications';
import { OpenApplicationTable } from './widgets/open-application-table/open-application-table';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../core/i18n/transloco.testing';
//#endif
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { StartupService } from '../../../../core/services/startup-service';
import { SettingContextService } from '../../../../core/settings/setting-context-service';
import { PERMISSIONS } from '../../../../shared/models/permission';
import { GetOpenApplicationsInputDto } from '../../dtos/open-application.dto';
import { OpenApplicationService } from '../../services/open-application-service';

import type { MockedObject } from 'vitest';

/** 开放应用页面的查询闭环。 */
describe('OpenApplications page query round trip', () => {
  let fixture: ComponentFixture<OpenApplications>;
  let component: OpenApplications;
  let router: Router;
  let service: Pick<MockedObject<OpenApplicationService>, 'getOpenApplications' | 'getScopes'>;

  /** 最近一次列表请求的参数。 */
  function lastQuery(): GetOpenApplicationsInputDto {
    const calls = vi.mocked(service.getOpenApplications).mock.calls;
    const query = calls.at(-1)?.[0];
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
    service = {
      getOpenApplications: vi.fn().mockName('OpenApplicationService.getOpenApplications'),
      getScopes: vi.fn().mockName('OpenApplicationService.getScopes'),
    };
    service.getOpenApplications.mockReturnValue(of({ items: [], totalCount: 0 }) as never);
    service.getScopes.mockReturnValue(of([]));

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

    // 应用启动时会话设置（连带语言服务）早已建好；留到首帧渲染途中才惰性创建的话，
    // 语言服务构造时激活语言，会让模板结构指令在创建视图的半途重入
    TestBed.inject(SettingContextService);
    fixture = TestBed.createComponent(OpenApplications);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('writes the page to the URL and refetches for the new page', async () => {
    table().paginationChange.emit({ pageIndex: 2, pageSize: 20 } as PaginationState);
    await fixture.whenStable();

    // 页码在 URL 里是 1 基（可分享、可前进后退），发给接口的是 offset。
    expect(router.url).toContain('page=3');
    expect(lastQuery().offset).toBe(40);
    expect(lastQuery().limit).toBe(20);
  });

  it('resets to the first page and offset 0 when the page size changes', async () => {
    table().paginationChange.emit({ pageIndex: 3, pageSize: 20 } as PaginationState);
    await fixture.whenStable();

    table().paginationChange.emit({ pageIndex: 0, pageSize: 50 } as PaginationState);
    await fixture.whenStable();

    expect(lastQuery().offset).toBe(0);
    expect(lastQuery().limit).toBe(50);
  });

  it('writes sorting to the URL, resets to page 1 and maps it to the API param', async () => {
    table().paginationChange.emit({ pageIndex: 2, pageSize: 20 } as PaginationState);
    await fixture.whenStable();

    table().sortingChange.emit([{ id: 'displayName', desc: true }] as SortingState);
    await fixture.whenStable();

    // 停在第 3 页换排序，看到的是另一批数据的第 3 页，等于结果错乱。
    expect(router.url).toContain('page=1');
    expect(lastQuery().offset).toBe(0);
    expect(lastQuery().sorting).toBeTruthy();
  });

  it('drops the previous secret after the secret dialog closes', () => {
    // 关闭弹窗只更新 visible 时，secret 与标题会留在组件状态里，
    // 下一次误打开弹窗会显示上一次生成的 secret。
    component.secretValue.set('generated-secret');
    component.secretHeader.set('Client created');
    component.secretDialogVisible.set(true);

    component.onSecretDialogVisibleChange(false);

    expect(component.secretDialogVisible()).toBe(false);
    expect(component.secretValue()).toBe('');
    expect(component.secretHeader()).toBe('');
  });

  it('keeps the secret while the secret dialog opens', () => {
    component.secretValue.set('generated-secret');
    component.secretHeader.set('Client created');

    component.onSecretDialogVisibleChange(true);

    expect(component.secretDialogVisible()).toBe(true);
    expect(component.secretValue()).toBe('generated-secret');
  });

  it('restores component state from the URL on reload and back/forward navigation', async () => {
    await router.navigate(['/platform/open-applications'], {
      queryParams: { page: 2, pageSize: 50, keyword: 'probe' },
    });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.pagination()).toEqual(expect.objectContaining({ pageIndex: 1, pageSize: 50 }));
    expect(lastQuery().keyword).toBe('probe');
  });

  it('keeps loading on while a superseded request is cancelled and the new one is in flight', async () => {
    const first = new Subject<never>();
    const second = new Subject<{ items: never[]; totalCount: number }>();
    service.getOpenApplications
      .mockReturnValueOnce(first as never)
      .mockReturnValueOnce(second as never);

    table().paginationChange.emit({ pageIndex: 1, pageSize: 20 } as PaginationState);
    await fixture.whenStable();
    expect(component.loading()).toBe(true);

    // 上一页还没返回就翻页：旧请求被取消，它的 finalize 不能把新请求的加载状态关掉。
    table().paginationChange.emit({ pageIndex: 2, pageSize: 20 } as PaginationState);
    await fixture.whenStable();
    expect(component.loading()).toBe(true);

    second.next({ items: [], totalCount: 0 });
    second.complete();
    expect(component.loading()).toBe(false);
  });
});
