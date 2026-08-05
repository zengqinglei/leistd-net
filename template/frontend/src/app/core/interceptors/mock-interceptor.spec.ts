import {
  HTTP_INTERCEPTORS,
  HttpClient,
  provideHttpClient,
  withInterceptorsFromDi,
} from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { MOCK_APIS, MockInterceptor } from '../../../../_mock/core/interceptor';
import { environment } from '../../../environments/environment';

describe('MockInterceptor', () => {
  const originalUseMock = environment.useMock;

  beforeEach(() => {
    environment.useMock = true;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptorsFromDi()),
        provideHttpClientTesting(),
        { provide: HTTP_INTERCEPTORS, useClass: MockInterceptor, multi: true },
        {
          provide: MOCK_APIS,
          useValue: {
            'GET /api/items': [{ id: 'all' }],
            'GET /api/items/:id': (request: { params: Record<string, string> }) => ({
              id: request.params['id'],
            }),
          },
        },
      ],
    });
  });

  afterEach(() => {
    environment.useMock = originalUseMock;
  });

  it('resolves an exact route to its mocked payload', (done) => {
    TestBed.inject(HttpClient)
      .get<{ id: string }[]>('/api/items')
      .subscribe({
        next: (response) => {
          expect(response).toEqual([{ id: 'all' }]);
          done();
        },
        error: done.fail,
      });
  });

  it('matches a parameterized route and decodes the path parameter', (done) => {
    TestBed.inject(HttpClient)
      .get<{ id: string }>('/api/items/item-42')
      .subscribe({
        next: (response) => {
          expect(response).toEqual({ id: 'item-42' });
          done();
        },
        error: done.fail,
      });
  });

  it('fails with 501 when no mock route matches an /api request', (done) => {
    TestBed.inject(HttpClient)
      .get('/api/unknown')
      .subscribe({
        next: () => done.fail('expected the request to error'),
        error: (error: { status: number }) => {
          expect(error.status).toBe(501);
          done();
        },
      });
  });
});
