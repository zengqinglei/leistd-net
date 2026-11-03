import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { WebhookService } from './webhook-service';

describe('WebhookService', () => {
  let service: WebhookService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(WebhookService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('maps the subscription filters to query parameters, including a false status', () => {
    service
      .getSubscriptions({ offset: 20, limit: 20, keyword: 'erp', isEnabled: false, eventType: '' })
      .subscribe();

    const request = http.expectOne((req) => req.url === '/api/v1/webhooks/subscriptions');
    // 空串不进查询串：服务端会把它当成筛选值
    expect(request.request.params.toString()).toBe(
      'offset=20&limit=20&keyword=erp&isEnabled=false',
    );
    request.flush({ items: [], totalCount: 0 });
  });

  it('maps the delivery filters to query parameters', () => {
    service
      .getDeliveries({ offset: 0, limit: 50, subscriptionId: 'sub-1', status: 'failed' })
      .subscribe();

    const request = http.expectOne((req) => req.url === '/api/v1/webhooks/deliveries');
    expect(request.request.params.toString()).toBe(
      'offset=0&limit=50&subscriptionId=sub-1&status=failed',
    );
    request.flush({ items: [], totalCount: 0 });
  });

  it('sends retryFailed in the query string only when requested', () => {
    service.enableSubscription('sub-1', true).subscribe();
    service.enableSubscription('sub-2', false).subscribe();

    const [withRetry, withoutRetry] = http.match((req) => req.url.endsWith('/enable'));
    expect(withRetry.request.url).toBe('/api/v1/webhooks/subscriptions/sub-1/enable');
    expect(withRetry.request.params.toString()).toBe('retryFailed=true');
    expect(withoutRetry.request.params.keys()).toEqual([]);
    withRetry.flush({});
    withoutRetry.flush({});
  });

  it('sends the calendar range, time zone and optional subscription to the statistics endpoint', () => {
    service
      .getStatistics({
        startDate: '2026-11-01',
        endDate: '2026-11-07',
        timeZone: 'Asia/Tokyo',
        subscriptionId: 'sub-1',
      })
      .subscribe();

    const request = http.expectOne((req) => req.url === '/api/v1/webhooks/statistics');
    expect(request.request.params.toString()).toBe(
      'startDate=2026-11-01&endDate=2026-11-07&timeZone=Asia/Tokyo&subscriptionId=sub-1',
    );
    request.flush({});
  });
});
