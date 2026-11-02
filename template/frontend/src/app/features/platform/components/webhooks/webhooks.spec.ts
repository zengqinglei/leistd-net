import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { Webhooks } from './webhooks';
import { WebhookStatistics } from './widgets/webhook-statistics/webhook-statistics';
import { WebhookSubscriptionTable } from './widgets/webhook-subscription-table/webhook-subscription-table';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../core/i18n/transloco.testing';
//#endif
import { AccountPreferencesService } from '../../../../core/services/account-preferences-service';
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { StartupService } from '../../../../core/services/startup-service';
import { PERMISSIONS } from '../../../../shared/constants/permission.constants';
import { WebhookStatisticsOutputDto } from '../../dtos/webhook.dto';
import { WebhookService } from '../../services/webhook-service';

import type { MockedObject } from 'vitest';

const STATISTICS: WebhookStatisticsOutputDto = {
  range: {
    startDate: '2026-11-01',
    endDate: '2026-11-07',
    timeZone: 'UTC',
    granularity: 'day',
    start: '2026-11-01T00:00:00.000Z',
    end: '2026-11-07T12:00:00.000Z',
    previousStart: '2026-10-25T00:00:00.000Z',
    previousEnd: '2026-10-31T12:00:00.000Z',
  },
  buckets: [],
  successRate: { value: 0.5, previous: null, series: [] },
  deliveries: { value: 4, previous: 2, series: [] },
  succeeded: { value: 2, previous: 1, series: [] },
  failed: { value: 2, previous: 1, series: [] },
  retrying: { value: 1 },
  disabledSubscriptions: { value: 0 },
};

/** Webhook 页面的 URL 状态闭环：视图、筛选、区间与打开的投递详情都以查询参数为唯一来源。 */
describe('Webhooks page', () => {
  let fixture: ComponentFixture<Webhooks>;
  let component: Webhooks;
  let router: Router;
  let service: MockedObject<WebhookService>;

  async function setup(permissions: string[]): Promise<void> {
    service = {
      getEventTypes: vi.fn().mockReturnValue(of([])),
      getSubscriptions: vi.fn().mockReturnValue(of({ items: [], totalCount: 0 })),
      getDeliveries: vi.fn().mockReturnValue(of({ items: [], totalCount: 0 })),
      getDelivery: vi.fn().mockReturnValue(of(null)),
      getStatistics: vi.fn().mockReturnValue(of(STATISTICS)),
      retryDelivery: vi.fn().mockReturnValue(of({})),
    } as unknown as MockedObject<WebhookService>;

    await TestBed.configureTestingModule({
      imports: [Webhooks],
      providers: [
        provideRouter([{ path: 'platform/webhooks', children: [] }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
        { provide: WebhookService, useValue: service },
        { provide: StartupService, useValue: { status: signal('success' as const) } },
      ],
    }).compileComponents();

    router = TestBed.inject(Router);
    await router.navigate(['/platform/webhooks']);
    TestBed.inject(AuthorizationService).setPermissions({
      permissions,
      isSuperAdmin: false,
      versionToken: 'r1',
    });
    // 先建好设置上下文（连带语言服务），否则首帧渲染途中创建它会让结构指令重入。
    TestBed.inject(AccountPreferencesService);
    fixture = TestBed.createComponent(Webhooks);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  }

  const manage = [PERMISSIONS.webhooks.default, PERMISSIONS.webhooks.manage];

  it('drops the filters of the other view but keeps the range when switching views', async () => {
    await setup(manage);
    await router.navigate(['/platform/webhooks'], {
      queryParams: { range: '30d', status: 'disabled', keyword: 'erp', page: 2 },
    });
    await fixture.whenStable();

    component.setView('deliveries');
    await fixture.whenStable();

    const url = router.parseUrl(router.url).queryParams;
    expect(url).toEqual({ view: 'deliveries', range: '30d' });
    expect(vi.mocked(service.getDeliveries).mock.calls.at(-1)?.[0]).toMatchObject({
      offset: 0,
      status: undefined,
      keyword: undefined,
    });
  });

  it('jumps to the delivery log of one subscription and scopes the statistics to it', async () => {
    await setup(manage);
    const table = fixture.debugElement.query(By.directive(WebhookSubscriptionTable))
      .componentInstance as WebhookSubscriptionTable;

    table.viewDeliveries.emit('sub-1');
    await fixture.whenStable();
    fixture.detectChanges();

    expect(router.parseUrl(router.url).queryParams).toEqual({
      view: 'deliveries',
      subscriptionId: 'sub-1',
    });
    expect(vi.mocked(service.getDeliveries).mock.calls.at(-1)?.[0]?.subscriptionId).toBe('sub-1');
    expect(vi.mocked(service.getStatistics).mock.calls.at(-1)?.[0]?.subscriptionId).toBe('sub-1');
  });

  it('opens the failed deliveries from the failed card', async () => {
    await setup(manage);
    const statistics = fixture.debugElement.query(By.directive(WebhookStatistics))
      .componentInstance as WebhookStatistics;

    statistics.showFailed.emit();
    await fixture.whenStable();

    expect(router.parseUrl(router.url).queryParams).toEqual({
      view: 'deliveries',
      status: 'failed',
    });
    expect(vi.mocked(service.getDeliveries).mock.calls.at(-1)?.[0]?.status).toBe('failed');
  });

  it('loads the delivery details from the URL without refetching the list', async () => {
    await setup(manage);
    await router.navigate(['/platform/webhooks'], { queryParams: { view: 'deliveries' } });
    await fixture.whenStable();
    const listCalls = vi.mocked(service.getDeliveries).mock.calls.length;

    component.openDelivery('delivery-1');
    await fixture.whenStable();

    expect(router.parseUrl(router.url).queryParams['delivery']).toBe('delivery-1');
    expect(service.getDelivery).toHaveBeenCalledWith('delivery-1');
    expect(vi.mocked(service.getDeliveries).mock.calls.length).toBe(listCalls);
  });

  it('refetches the statistics for a new range without refetching the list', async () => {
    await setup(manage);
    const listCalls = vi.mocked(service.getSubscriptions).mock.calls.length;

    component.setPreset('today');
    await fixture.whenStable();
    fixture.detectChanges();

    const query = vi.mocked(service.getStatistics).mock.calls.at(-1)?.[0];
    expect(router.parseUrl(router.url).queryParams['range']).toBe('today');
    expect(query?.startDate).toBe(query?.endDate);
    expect(vi.mocked(service.getSubscriptions).mock.calls.length).toBe(listCalls);
  });

  it('does not resend a delivery that is still retrying', async () => {
    await setup(manage);

    await component.retryDelivery({ id: 'delivery-1', status: 'pending' });

    expect(service.retryDelivery).not.toHaveBeenCalled();
  });

  it('renders no write entry points with the read permission only', async () => {
    await setup([PERMISSIONS.webhooks.default]);
    const table = fixture.debugElement.query(By.directive(WebhookSubscriptionTable))
      .componentInstance as WebhookSubscriptionTable;

    expect(component.canManage()).toBe(false);
    expect(table.canManage()).toBe(false);
    // 新建入口与订阅对话框都不渲染
    expect(fixture.nativeElement.querySelector('app-webhook-subscription-dialog')).toBeNull();
    expect(
      [...fixture.nativeElement.querySelectorAll('button')].some((button: HTMLButtonElement) =>
        button.querySelector('ng-icon[name="lucidePlus"]'),
      ),
    ).toBe(false);
  });
});
