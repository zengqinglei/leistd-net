import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Observable } from 'rxjs';

import { DistributedEventService } from './distributed-event-service';

describe('DistributedEventService', () => {
  let service: DistributedEventService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(DistributedEventService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it.each([
    ['getOutboxList', '/api/v1/distributed-events/outbox'],
    ['getInboxList', '/api/v1/distributed-events/inbox'],
    ['getRejectionList', '/api/v1/distributed-events/rejections'],
  ] as const)('sends the time range next to the loading cutoff from %s', (method, url) => {
    const list = service[method].bind(service) as (input: object) => Observable<unknown>;
    list({
      startTime: '2026-11-11T16:00:00.000Z',
      endTime: '2026-11-19T15:59:59.999Z',
      before: '2026-11-19T10:00:00.000Z',
      offset: 10,
      limit: 10,
    }).subscribe();

    const request = http.expectOne((req) => req.url === url);
    expect(request.request.params.toString()).toBe(
      'startTime=2026-11-11T16:00:00.000Z&endTime=2026-11-19T15:59:59.999Z' +
        '&before=2026-11-19T10:00:00.000Z&offset=10&limit=10',
    );
    request.flush({ items: [], totalCount: 0 });
  });

  it('omits an unset time range and empty filters from the query string', () => {
    service
      .getOutboxList({ startTime: null, endTime: undefined, keyword: '', offset: 0 })
      .subscribe();

    const request = http.expectOne((req) => req.url === '/api/v1/distributed-events/outbox');
    expect(request.request.params.toString()).toBe('offset=0');
    request.flush({ items: [], totalCount: 0 });
  });

  it('posts the time range inside the recovery filter', () => {
    const filter = {
      before: '2026-11-19T10:00:00.000Z',
      startTime: '2026-11-11T16:00:00.000Z',
      endTime: '2026-11-19T15:59:59.999Z',
    };
    service.redeliver({ filter }).subscribe();

    const request = http.expectOne('/api/v1/distributed-events/outbox/redeliver');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ filter });
    request.flush({ affectedCount: 0, deferredCount: 0, skipped: [] });
  });
});
