import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { Observable, of, Subject } from 'rxjs';

import { Webhooks } from './webhooks';
import { WebhookStatistics } from './widgets/webhook-statistics/webhook-statistics';
import { WebhookSubscriptionDialog } from './widgets/webhook-subscription-dialog/webhook-subscription-dialog';
import { WebhookSubscriptionTable } from './widgets/webhook-subscription-table/webhook-subscription-table';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../core/i18n/transloco.testing';
//#endif
import { AccountPreferencesService } from '../../../../core/services/account-preferences-service';
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { StartupService } from '../../../../core/services/startup-service';
import { PERMISSIONS } from '../../../../shared/constants/permission.constants';
import {
  expectTouchTarget,
  setCoarsePointer,
} from '../../../../shared/utils/coarse-pointer.testing';
import { WebhookStatisticsOutputDto, WebhookSubscriptionOutputDto } from '../../dtos/webhook.dto';
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

function subscription(id: string): WebhookSubscriptionOutputDto {
  return {
    id,
    name: `Name ${id}`,
    url: `https://example.com/${id}`,
    eventTypes: ['*'],
    headers: {},
    secretPrefix: 'whsec_ab',
    isEnabled: true,
    creationTime: '2026-11-01T00:00:00.000Z',
  };
}

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
      getSubscription: vi.fn(),
      updateSubscription: vi.fn().mockReturnValue(of({})),
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

  afterEach(async () => {
    fixture?.destroy();
    await setCoarsePointer(false);
  });

  const manage = [PERMISSIONS.webhooks.default, PERMISSIONS.webhooks.manage];

  // 标签列表上游定高、项比按钮矮，触屏下项补足 44px，列表随之撑高
  it('gives both view tabs a 44px target on touch screens', async () => {
    await setCoarsePointer(true);
    await setup(manage);
    const tabs = (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(
      '[data-testid="webhook-view"] [role="tab"]',
    );

    expect(tabs.length).toBe(2);
    tabs.forEach(expectTouchTarget);
  });

  it('drops the filters of the other view but keeps the range when switching views', async () => {
    await setup(manage);
    await router.navigate(['/platform/webhooks'], {
      queryParams: { range: '30d', status: 'disabled', keyword: 'erp', page: 2 },
    });
    await fixture.whenStable();

    // 标签顺序固定：订阅、投递日志
    const [subscriptionsTab, deliveriesTab] = (
      fixture.nativeElement as HTMLElement
    ).querySelectorAll<HTMLButtonElement>('[data-testid="webhook-view"] [role="tab"]');
    const tab = (key: 'subscriptions' | 'deliveries') =>
      key === 'subscriptions' ? subscriptionsTab : deliveriesTab;
    expect(tab('subscriptions').getAttribute('aria-selected')).toBe('true');

    tab('deliveries').click();
    await fixture.whenStable();
    fixture.detectChanges();

    const url = router.parseUrl(router.url).queryParams;
    expect(url).toEqual({ view: 'deliveries', range: '30d' });
    expect(tab('deliveries').getAttribute('aria-selected')).toBe('true');
    expect(tab('subscriptions').getAttribute('aria-selected')).toBe('false');
    expect(vi.mocked(service.getDeliveries).mock.calls.at(-1)?.[0]).toMatchObject({
      offset: 0,
      status: undefined,
      keyword: undefined,
    });
  });

  it('links the selected tab and the view panel in both directions', async () => {
    await setup(manage);
    const root = fixture.nativeElement as HTMLElement;

    for (const [view, table] of [
      ['subscriptions', 'app-webhook-subscription-table'],
      ['deliveries', 'app-webhook-delivery-table'],
    ] as const) {
      await router.navigate(['/platform/webhooks'], {
        queryParams: { view: view === 'deliveries' ? view : null },
      });
      await fixture.whenStable();
      fixture.detectChanges();

      const selected = root.querySelector<HTMLElement>(
        '[data-testid="webhook-view"] [role="tab"][aria-selected="true"]',
      )!;
      const controls = selected.getAttribute('aria-controls')!;
      const panel = root.querySelector<HTMLElement>(`#${CSS.escape(controls)}`);

      expect(panel, `${view}: aria-controls="${controls}" resolves`).not.toBeNull();
      expect(panel!.getAttribute('role')).toBe('tabpanel');
      expect(selected.id).toBeTruthy();
      expect(panel!.getAttribute('aria-labelledby')).toBe(selected.id);
      // 面板包住工具栏与当前视图的列表
      expect(panel!.querySelector('input')).not.toBeNull();
      expect(panel!.querySelector(table)).not.toBeNull();
    }
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

  /** 每个订阅详情一个可控响应：测试决定谁先返回。 */
  function deferSubscriptionDetails(): Map<string, Subject<WebhookSubscriptionOutputDto>> {
    const responses = new Map<string, Subject<WebhookSubscriptionOutputDto>>();
    vi.mocked(service.getSubscription).mockImplementation((id: string) => {
      const response = new Subject<WebhookSubscriptionOutputDto>();
      responses.set(id, response);
      return response;
    });
    return responses;
  }

  /** 像 HttpClient 一样发一次值就完成。 */
  function respond(
    responses: Map<string, Subject<WebhookSubscriptionOutputDto>>,
    id: string,
  ): void {
    const response = responses.get(id)!;
    response.next(subscription(id));
    response.complete();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function dialog(): WebhookSubscriptionDialog {
    return fixture.debugElement.query(By.directive(WebhookSubscriptionDialog))
      .componentInstance as WebhookSubscriptionDialog;
  }

  it('ignores a late detail of the previous subscription after editing another one', async () => {
    await setup(manage);
    const responses = deferSubscriptionDetails();

    component.openEditDialog('subscription-A');
    await settle();
    component.editDialogVisible.set(false);
    await settle();
    component.openEditDialog('subscription-B');
    await settle();

    respond(responses, 'subscription-B');
    await settle();
    respond(responses, 'subscription-A');
    await settle();

    expect(component.selectedSubscription()?.id).toBe('subscription-B');
    expect(component.editDialogLoading()).toBe(false);
    expect(dialog().subscriptionForm.name().value()).toBe('Name subscription-B');
    expect(dialog().subscriptionForm.url().value()).toBe('https://example.com/subscription-B');

    const input = { name: 'Renamed', url: 'https://example.com/new', eventTypes: ['*'] };
    const save: Observable<unknown> = component.handleSave(input);
    save.subscribe();
    expect(service.updateSubscription).toHaveBeenCalledWith('subscription-B', input);
  });

  it('does not fill the create form with a late detail of a closed edit', async () => {
    await setup(manage);
    const responses = deferSubscriptionDetails();

    component.openEditDialog('subscription-A');
    await settle();
    component.editDialogVisible.set(false);
    await settle();
    component.openCreateDialog();
    await settle();

    expect(component.editDialogLoading()).toBe(false);
    respond(responses, 'subscription-A');
    await settle();

    expect(component.editingSubscriptionId()).toBeNull();
    expect(component.selectedSubscription()).toBeNull();
    expect(dialog().isEditMode()).toBe(false);
    expect(dialog().subscriptionForm.name().value()).toBe('');
    expect(dialog().subscriptionForm.url().value()).toBe('');
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
