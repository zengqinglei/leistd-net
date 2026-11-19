import { Location, PlatformLocation } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { MockPlatformLocation } from '@angular/common/testing';
import { signal } from '@angular/core';
import { ComponentFixture, DeferBlockState, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { Observable, of, Subject, throwError } from 'rxjs';

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
import { dateRangeToQuery } from '../../../../shared/utils/date-range-query';
import {
  WebhookDeliveryOutputDto,
  WebhookStatisticsOutputDto,
  WebhookSubscriptionOutputDto,
} from '../../dtos/webhook.dto';
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

function delivery(id: string): WebhookDeliveryOutputDto {
  return {
    id,
    eventId: `evt_${id}`,
    eventType: 'user.created',
    subscriptionId: 'sub-1',
    subscriptionName: 'Name sub-1',
    subscriptionUrl: 'https://example.com/sub-1',
    status: 'succeeded',
    attemptCount: 1,
    lastStatusCode: 200,
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
      getSubscription: vi.fn().mockReturnValue(of(null)),
      updateSubscription: vi.fn().mockReturnValue(of({})),
    } as unknown as MockedObject<WebhookService>;

    await TestBed.configureTestingModule({
      imports: [Webhooks],
      providers: [
        provideRouter([{ path: 'platform/webhooks', children: [] }]),
        // 历史放在内存里：后退、前进同步可控，也不动测试页自己的浏览器历史
        { provide: PlatformLocation, useClass: MockPlatformLocation },
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
    vi.useRealTimers();
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

  it('searches the same keyword after changing views clears the URL keyword', async () => {
    await setup(manage);
    vi.useFakeTimers({
      toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date'],
    });
    component.onSearchQueryChange('erp');
    await vi.advanceTimersByTimeAsync(300);
    await fixture.whenStable();
    component.setView('deliveries');
    await fixture.whenStable();
    component.onSearchQueryChange('erp');
    await vi.advanceTimersByTimeAsync(300);
    await fixture.whenStable();
    expect(service.getDeliveries).toHaveBeenLastCalledWith(
      expect.objectContaining({ keyword: 'erp' }),
    );
  });

  it('debounces remote subscription searches and retains names outside the result page', async () => {
    await setup(manage);
    vi.useFakeTimers({
      toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date'],
    });
    service.getSubscription.mockReturnValue(
      of({ id: 'beyond-page', name: 'Outside the first page' }) as never,
    );
    service.getSubscriptions.mockReturnValue(
      of({ items: [subscription('visible')], totalCount: 121 }),
    );
    await router.navigate(['/platform/webhooks'], {
      queryParams: { view: 'deliveries', subscriptionId: 'beyond-page' },
    });
    await fixture.whenStable();
    expect(component.selectedSubscriptionName()).toBe('Outside the first page');
    expect(service.getSubscription).toHaveBeenCalledWith('beyond-page');
    const requests = service.getSubscriptions.mock.calls.length;
    component.onSubscriptionSearch(' Integration 120 ');
    await vi.advanceTimersByTimeAsync(299);
    expect(service.getSubscriptions.mock.calls.length).toBe(requests);
    await vi.advanceTimersByTimeAsync(1);
    expect(service.getSubscriptions).toHaveBeenLastCalledWith({
      keyword: 'Integration 120',
      limit: 20,
    });
    expect(component.subscriptionOptionsTotal()).toBe(121);
    expect(component.selectedSubscriptionName()).toBe('Outside the first page');
    component.onFilterChange('subscriptionId', null);
    await fixture.whenStable();
    expect(component.selectedSubscriptionId()).toBeNull();
  });

  it('reports failed remote searches, supports retry and keeps unknown IDs clearable', async () => {
    await setup(manage);
    vi.useFakeTimers({
      toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date'],
    });
    service.getSubscription.mockReturnValue(throwError(() => new Error('missing')));
    await router.navigate(['/platform/webhooks'], {
      queryParams: { view: 'deliveries', subscriptionId: 'missing-id' },
    });
    await fixture.whenStable();
    expect(component.selectedSubscriptionName()).toBe('missing-id');
    service.getSubscriptions.mockReturnValue(throwError(() => new Error('offline')));
    component.onSubscriptionSearch('unknown');
    await vi.advanceTimersByTimeAsync(300);
    expect(component.subscriptionOptionsError()).toBeTruthy();
    expect(component.subscriptionOptionsLoading()).toBe(false);
    service.getSubscriptions.mockReturnValue(of({ items: [], totalCount: 0 }));
    component.loadSubscriptionOptions();
    await vi.advanceTimersByTimeAsync(300);
    expect(component.subscriptionOptionsError()).toBeNull();
    expect(component.subscriptionOptionsSource()).toEqual([]);
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

  function statisticsPanel(): WebhookStatistics {
    return fixture.debugElement.query(By.directive(WebhookStatistics))
      .componentInstance as WebhookStatistics;
  }

  /** 统计响应换一组 UTC 边界，其余沿用默认数据。 */
  function statisticsFor(start: string, end: string): WebhookStatisticsOutputDto {
    return { ...STATISTICS, range: { ...STATISTICS.range, start, end } };
  }

  // 统计的 UTC 边界直接带进列表的时间范围，失败数与列表对得上
  it('opens the failed deliveries of the statistics range from the failed card', async () => {
    await setup(manage);

    statisticsPanel().showFailed.emit();
    await fixture.whenStable();

    expect(router.parseUrl(router.url).queryParams).toEqual({
      view: 'deliveries',
      status: 'failed',
      startTime: STATISTICS.range.start,
      endTime: STATISTICS.range.end,
    });
    expect(vi.mocked(service.getDeliveries).mock.calls.at(-1)?.[0]).toMatchObject({
      status: 'failed',
      startTime: STATISTICS.range.start,
      endTime: STATISTICS.range.end,
    });
  });

  it('carries a range that ends at the statistics query time when it includes today', async () => {
    await setup(manage);
    // 含今天：服务端把结束截到统计查询时刻（最多 7 位小数），不是当天结束；列表要原样带上，不截成毫秒
    const today = statisticsFor('2026-11-07T00:00:00.000Z', '2026-11-07T09:41:27.5123456Z');
    service.getStatistics.mockReturnValue(of(today));
    component.setPreset('today');
    await fixture.whenStable();

    statisticsPanel().showFailed.emit();
    await fixture.whenStable();

    const url = router.parseUrl(router.url).queryParams;
    expect(url['range']).toBe('today');
    expect(url['startTime']).toBe('2026-11-07T00:00:00.000Z');
    expect(url['endTime']).toBe('2026-11-07T09:41:27.5123456Z');
    expect(vi.mocked(service.getDeliveries).mock.calls.at(-1)?.[0]).toMatchObject({
      startTime: '2026-11-07T00:00:00.000Z',
      endTime: '2026-11-07T09:41:27.5123456Z',
    });
  });

  it('keeps the subscription and its statistics range when opening failed deliveries', async () => {
    await setup(manage);
    const scoped = statisticsFor('2026-11-01T00:00:00.000Z', '2026-11-07T08:00:00.000Z');
    service.getStatistics.mockReturnValue(of(scoped));
    await router.navigate(['/platform/webhooks'], {
      queryParams: { view: 'deliveries', subscriptionId: 'sub-1' },
    });
    await fixture.whenStable();
    expect(vi.mocked(service.getStatistics).mock.calls.at(-1)?.[0]?.subscriptionId).toBe('sub-1');

    statisticsPanel().showFailed.emit();
    await fixture.whenStable();

    expect(router.parseUrl(router.url).queryParams).toEqual({
      view: 'deliveries',
      status: 'failed',
      subscriptionId: 'sub-1',
      startTime: scoped.range.start,
      endTime: scoped.range.end,
    });
    expect(vi.mocked(service.getDeliveries).mock.calls.at(-1)?.[0]).toMatchObject({
      subscriptionId: 'sub-1',
      status: 'failed',
      startTime: scoped.range.start,
      endTime: scoped.range.end,
    });
  });

  // 订阅 A → B，B 的统计未返回时卡片仍是 A 的数据：跳转带 A 的区间与 A 的订阅，摘要也仍写 A
  it('opens the failed deliveries of the subscription whose statistics are still shown', async () => {
    await setup(manage);
    vi.mocked(service.getSubscription).mockImplementation((id: string) => of(subscription(id)));
    const statisticsOfA = statisticsFor('2026-11-01T00:00:00.000Z', '2026-11-07T06:00:00.000Z');
    service.getStatistics.mockReturnValue(of(statisticsOfA));
    await router.navigate(['/platform/webhooks'], {
      queryParams: { view: 'deliveries', subscriptionId: 'sub-A' },
    });
    await settle();
    expect(statisticsPanel().subscriptionName()).toBe('Name sub-A');

    service.getStatistics.mockReturnValue(new Subject<WebhookStatisticsOutputDto>());
    component.onFilterChange('subscriptionId', 'sub-B');
    await settle();
    expect(vi.mocked(service.getStatistics).mock.calls.at(-1)?.[0]?.subscriptionId).toBe('sub-B');
    expect(component.statisticsLoading()).toBe(true);
    expect(component.selectedSubscriptionName()).toBe('Name sub-B');
    expect(statisticsPanel().subscriptionName()).toBe('Name sub-A');

    statisticsPanel().showFailed.emit();
    await settle();

    expect(router.parseUrl(router.url).queryParams).toEqual({
      view: 'deliveries',
      status: 'failed',
      subscriptionId: 'sub-A',
      startTime: statisticsOfA.range.start,
      endTime: statisticsOfA.range.end,
    });
  });

  it('uses the range of the statistics still on screen while they reload', async () => {
    await setup(manage);
    // 新区间的统计还没返回：卡片仍是旧数据，跳转跟着旧数据的区间
    service.getStatistics.mockReturnValue(new Subject<WebhookStatisticsOutputDto>());
    component.setPreset('30d');
    await fixture.whenStable();
    expect(component.statisticsLoading()).toBe(true);

    statisticsPanel().showFailed.emit();
    await fixture.whenStable();

    const url = router.parseUrl(router.url).queryParams;
    expect(url['startTime']).toBe(STATISTICS.range.start);
    expect(url['endTime']).toBe(STATISTICS.range.end);
    expect(url['range']).toBe('30d');
  });

  describe('time range and toolbar', () => {
    const root = () => fixture.nativeElement as HTMLElement;
    const actions = () =>
      root().querySelector<HTMLElement>('[data-testid="webhook-toolbar-actions"]')!;
    const refresh = () =>
      root().querySelector<HTMLButtonElement>('[data-testid="webhook-refresh"]')!;
    const queryParam = (name: string) => router.parseUrl(router.url).queryParamMap.get(name);
    const lastDeliveriesQuery = () => vi.mocked(service.getDeliveries).mock.calls.at(-1)?.[0];

    async function openView(queryParams: Record<string, string>): Promise<void> {
      await router.navigate(['/platform/webhooks'], { queryParams });
      await settle();
    }

    it('orders the delivery log actions as time range then refresh', async () => {
      await setup(manage);
      await openView({ view: 'deliveries' });

      const children = [...actions().children].filter((child) => !child.matches('label'));
      expect(children.map((child) => child.localName)).toEqual(['hlm-date-range-picker', 'button']);
      expect(children[1]).toBe(refresh());
      const label = actions().querySelector('label')!;
      expect(label.classList).toContain('sr-only');
      expect(label.htmlFor).toBe('webhook-delivery-date-range');
      expect(actions().querySelector('#webhook-delivery-date-range')).not.toBeNull();
    });

    it('shows no time range on subscriptions and keeps creating last', async () => {
      await setup(manage);

      expect(actions().querySelector('hlm-date-range-picker')).toBeNull();
      const buttons = [...actions().querySelectorAll('button')];
      expect(buttons[0]).toBe(refresh());
      expect(buttons.at(-1)!.querySelector('ng-icon[name="lucidePlus"]')).not.toBeNull();
    });

    it('keeps the secondary refresh enabled and swaps its icon for a spinner while loading', async () => {
      await setup(manage);
      expect(refresh().getAttribute('variant')).toBe('secondary');
      service.getSubscriptions.mockReturnValue(new Subject());

      component.reload();
      await settle();

      expect(component.loading()).toBe(true);
      expect(refresh().disabled).toBe(false);
      expect(refresh().querySelector('hlm-spinner')).not.toBeNull();
      expect(refresh().querySelector('ng-icon[name="lucideRefreshCw"]')).toBeNull();
    });

    it('writes the picked days as UTC bounds, resets the page and refetches the list', async () => {
      await setup(manage);
      await openView({ view: 'deliveries', page: '3' });
      const calls = vi.mocked(service.getDeliveries).mock.calls.length;
      const picked: [Date, Date] = [new Date(2026, 10, 1), new Date(2026, 10, 7)];
      const bounds = dateRangeToQuery(picked, component.timeZone());

      component.onRangeChange(picked);
      await settle();

      expect(queryParam('startTime')).toBe(bounds.startTime);
      expect(queryParam('endTime')).toBe(bounds.endTime);
      expect(queryParam('page')).toBe('1');
      // 去重键含时间参数：只改时间范围也会重查列表
      expect(vi.mocked(service.getDeliveries).mock.calls.length).toBe(calls + 1);
      expect(lastDeliveriesQuery()).toMatchObject({
        offset: 0,
        startTime: bounds.startTime,
        endTime: bounds.endTime,
      });
      expect(component.hasActiveFilters()).toBe(true);
      const [from, to] = component.selectedRange()!;
      expect([from.getFullYear(), from.getMonth(), from.getDate()]).toEqual([2026, 10, 1]);
      expect([to.getFullYear(), to.getMonth(), to.getDate()]).toEqual([2026, 10, 7]);

      component.onRangeChange(null);
      await settle();
      expect(queryParam('startTime')).toBeNull();
      expect(queryParam('endTime')).toBeNull();
      expect(lastDeliveriesQuery()?.startTime).toBeUndefined();
    });

    it('treats a one-sided, malformed or reversed range in the URL as no filter', async () => {
      await setup(manage);
      for (const range of [
        { startTime: '2026-11-01T00:00:00.000Z' } as Record<string, string>,
        { startTime: 'not-a-date', endTime: '2026-11-07T00:00:00.000Z' },
        { startTime: '2026-11-08T00:00:00.000Z', endTime: '2026-11-01T00:00:00.000Z' },
        // JS 能解析、接口会拒绝：不带时区
        { startTime: '2026-11-01', endTime: '2026-11-07' },
        { startTime: '2026-11-01T00:00:00', endTime: '2026-11-07T23:59:59' },
      ]) {
        await openView({ view: 'deliveries', ...range });

        expect(component.selectedRange(), JSON.stringify(range)).toBeUndefined();
        expect(
          root().querySelector<HTMLInputElement>('#webhook-delivery-date-range')!.value,
          JSON.stringify(range),
        ).toBe('');
        expect(component.hasActiveFilters()).toBe(false);
        expect(lastDeliveriesQuery()).toMatchObject({ startTime: undefined, endTime: undefined });
      }
    });

    it('sends a range written with an explicit offset as UTC', async () => {
      await setup(manage);
      await openView({
        view: 'deliveries',
        startTime: '2026-11-01T08:00:00+08:00',
        endTime: '2026-11-08T07:59:59.999+08:00',
      });

      expect(lastDeliveriesQuery()).toMatchObject({
        startTime: '2026-11-01T00:00:00.000Z',
        endTime: '2026-11-07T23:59:59.999Z',
      });
      expect(component.selectedRange()).toBeDefined();
    });

    it('clears the time range when switching views or jumping from a subscription', async () => {
      await setup(manage);
      const range = { startTime: '2026-11-01T00:00:00.000Z', endTime: '2026-11-07T23:59:59.999Z' };
      await openView({ view: 'deliveries', ...range });

      component.setView('subscriptions');
      await settle();
      expect(queryParam('startTime')).toBeNull();
      expect(queryParam('endTime')).toBeNull();

      await openView({ view: 'deliveries', ...range });
      component.viewDeliveries('sub-1');
      await settle();
      expect(router.parseUrl(router.url).queryParams).toEqual({
        view: 'deliveries',
        subscriptionId: 'sub-1',
      });
      expect(lastDeliveriesQuery()?.startTime).toBeUndefined();
    });
  });

  describe('status filter', () => {
    const items = () =>
      [
        ...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>(
          '[data-testid="webhook-status-filter"] button',
        ),
      ].map((button) => ({ value: button.getAttribute('value'), button }));
    const status = () => router.parseUrl(router.url).queryParamMap.get('status');

    it('toggles one delivery status and clears it on a second click', async () => {
      await setup(manage);
      await router.navigate(['/platform/webhooks'], {
        queryParams: { view: 'deliveries', page: '2' },
      });
      await settle();
      expect(items().map((item) => item.value)).toEqual(['succeeded', 'pending', 'failed']);

      items()
        .find((item) => item.value === 'failed')!
        .button.click();
      await settle();
      expect(status()).toBe('failed');
      expect(router.parseUrl(router.url).queryParamMap.get('page')).toBe('1');
      expect(vi.mocked(service.getDeliveries).mock.calls.at(-1)?.[0]?.status).toBe('failed');
      expect(
        items()
          .find((item) => item.value === 'failed')!
          .button.getAttribute('data-state'),
      ).toBe('on');

      items()
        .find((item) => item.value === 'failed')!
        .button.click();
      await settle();
      expect(status()).toBeNull();
      expect(vi.mocked(service.getDeliveries).mock.calls.at(-1)?.[0]?.status).toBeUndefined();
    });

    it('toggles the subscription status between enabled, disabled and all', async () => {
      await setup(manage);
      expect(items().map((item) => item.value)).toEqual(['enabled', 'disabled']);

      items()[1].button.click();
      await settle();
      expect(status()).toBe('disabled');
      expect(vi.mocked(service.getSubscriptions).mock.calls.at(-1)?.[0]?.isEnabled).toBe(false);

      items()[1].button.click();
      await settle();
      expect(status()).toBeNull();
      expect(vi.mocked(service.getSubscriptions).mock.calls.at(-1)?.[0]?.isEnabled).toBeUndefined();
    });
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

  /**
   * 投递详情抽屉与浏览器历史（frontend-ui §2.7）：打开新增一条历史、后退即关闭；从列表打开的条目（含页面重建后还原）
   * 关闭时退回打开前那条，外部深链移除参数并替换当前条目；关闭后回焦打开它的行入口。
   */
  describe('delivery sheet history', () => {
    let location: Location;
    let platform: PlatformLocation;

    const deliveryParam = () => router.parseUrl(router.url).queryParams['delivery'] ?? null;
    const sheet = () =>
      document.querySelector<HTMLElement>('[data-testid="webhook-delivery-sheet"]');
    const detailsButton = (id: string) =>
      fixture.nativeElement.querySelector(
        `[data-delivery-id="${id}"] [data-testid="webhook-delivery-details"]`,
      ) as HTMLElement | null;

    /** 停在投递日志（可带参数），渲染列表的 @defer 块。 */
    async function openDeliveryLog(queryParams: Record<string, string> = {}): Promise<void> {
      vi.mocked(service.getDeliveries).mockReturnValue(
        of({ items: [delivery('delivery-1'), delivery('delivery-2')], totalCount: 2 }),
      );
      await router.navigate(['/platform/webhooks'], {
        queryParams: { view: 'deliveries', ...queryParams },
      });
      await settle();
      const [table] = await fixture.getDeferBlocks();
      await table.render(DeferBlockState.Complete);
      await settle();
      location = TestBed.inject(Location);
      platform = TestBed.inject(PlatformLocation);
    }

    async function closeSheet(): Promise<void> {
      sheet()!.querySelector<HTMLElement>('[hlmsheetclose]')!.click();
      await vi.waitFor(() => expect(sheet()).toBeNull());
      await settle();
    }

    beforeEach(async () => {
      await setup(manage);
      // 应用启动时由初始导航挂上；测试只调 navigate，后退、前进要自己挂
      router.setUpLocationChangeListener();
    });

    it('goes back to the entry before it when closing a sheet opened on the page', async () => {
      await openDeliveryLog();
      detailsButton('delivery-2')!.click();
      await settle();
      await vi.waitFor(() => expect(sheet()).not.toBeNull());
      expect(deliveryParam()).toBe('delivery-2');

      const push = vi.spyOn(platform, 'pushState');
      const back = vi.spyOn(location, 'back');
      await closeSheet();

      await vi.waitFor(() => expect(deliveryParam()).toBeNull());
      expect(back).toHaveBeenCalledTimes(1);
      expect(push).not.toHaveBeenCalled();
      expect(document.activeElement).toBe(detailsButton('delivery-2'));

      // 关闭没有新增条目：前进回到打开的那条，抽屉重开
      location.forward();
      await settle();
      await vi.waitFor(() => expect(deliveryParam()).toBe('delivery-2'));
      await vi.waitFor(() => expect(sheet()).not.toBeNull());
    });

    it('still goes back when the page is rebuilt on an entry opened from the list', async () => {
      await openDeliveryLog();
      component.openDelivery('delivery-1');
      await settle();
      await vi.waitFor(() => expect(sheet()).not.toBeNull());

      // 刷新或离开再前进回来：页面重建，条目上的标记还在
      fixture.destroy();
      await vi.waitFor(() => expect(sheet()).toBeNull());
      fixture = TestBed.createComponent(Webhooks);
      component = fixture.componentInstance;
      await settle();
      const [table] = await fixture.getDeferBlocks();
      await table.render(DeferBlockState.Complete);
      await settle();
      await vi.waitFor(() => expect(sheet()).not.toBeNull());

      const push = vi.spyOn(platform, 'pushState');
      const back = vi.spyOn(location, 'back');
      await closeSheet();

      await vi.waitFor(() => expect(deliveryParam()).toBeNull());
      expect(back).toHaveBeenCalledTimes(1);
      expect(push).not.toHaveBeenCalled();
      expect(document.activeElement).toBe(detailsButton('delivery-1'));
    });

    it('closes with the navigation on back without moving the history again', async () => {
      await openDeliveryLog();
      component.openDelivery('delivery-1');
      await settle();
      await vi.waitFor(() => expect(sheet()).not.toBeNull());

      const push = vi.spyOn(platform, 'pushState');
      const back = vi.spyOn(location, 'back');
      location.back();
      await vi.waitFor(() => expect(sheet()).toBeNull());
      await vi.waitFor(() => expect(deliveryParam()).toBeNull());
      await settle();

      // 只有测试自己的那一次后退：关闭时没有再退一步，也没有新增条目
      expect(back).toHaveBeenCalledTimes(1);
      expect(push).not.toHaveBeenCalled();
      expect(location.path()).toBe('/platform/webhooks?view=deliveries');
      expect(document.activeElement).toBe(detailsButton('delivery-1'));
    });

    it('removes the parameter in place when closing a deep-linked sheet', async () => {
      await openDeliveryLog({ delivery: 'delivery-1', status: 'succeeded' });
      await vi.waitFor(() => expect(sheet()).not.toBeNull());

      const push = vi.spyOn(platform, 'pushState');
      const replace = vi.spyOn(platform, 'replaceState');
      const back = vi.spyOn(location, 'back');
      await closeSheet();

      await vi.waitFor(() => expect(deliveryParam()).toBeNull());
      expect(replace.mock.calls.at(-1)?.[2]).toBe(
        '/platform/webhooks?view=deliveries&status=succeeded',
      );
      expect(back).not.toHaveBeenCalled();
      expect(push).not.toHaveBeenCalled();
      // 其他查询参数保留
      expect(router.parseUrl(router.url).queryParams['status']).toBe('succeeded');
      expect(document.activeElement).toBe(detailsButton('delivery-1'));
    });

    it('returns focus to the delivery log tab when the deep-linked delivery is not listed', async () => {
      await openDeliveryLog({ delivery: 'delivery-9' });
      await vi.waitFor(() => expect(sheet()).not.toBeNull());

      await closeSheet();

      await vi.waitFor(() => expect(deliveryParam()).toBeNull());
      expect(document.activeElement?.id).toBe('brn-tabs-label-deliveries');
    });
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
