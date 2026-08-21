import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

//#if (IdentityService)
import { OPEN_APPLICATION_API } from '../../../../_mock/api/open-application';
//#endif
import { MOCK_APIS, mockInterceptor } from '../../../../_mock/core/interceptor';
//#if (IdentityService)
import { USERS } from '../../../../_mock/data/user';
import { setMockSessionUserId } from '../../../../_mock/utils/current-user';
//#endif
import { environment } from '../../../environments/environment';
//#if (IdentityService)
import {
  CreateOpenApplicationInputDto,
  OpenApplicationOutputDto,
} from '../../features/platform/models/open-application.dto';
//#endif

describe('mockInterceptor', () => {
  const originalUseMock = environment.useMock;

  beforeEach(() => {
    environment.useMock = true;
    //#if (IdentityService)
    // 开放应用接口有权限门（与后端策略一一对应），本组用例关注的是拦截器本身，
    // 因此先建立一个有权限的会话，避免被 401 挡在门外。
    setMockSessionUserId(USERS.find((user) => user.isSuperAdmin)!.id);
    //#endif
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([mockInterceptor])),
        provideHttpClientTesting(),
        {
          provide: MOCK_APIS,
          // prettier-ignore
          useValue: {
            //#if (IdentityService)
            ...OPEN_APPLICATION_API,
            //#endif
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
    //#if (IdentityService)
    setMockSessionUserId(null);
    //#endif
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
  //#if (IdentityService)

  it('returns a confidential client secret only in the create response', (done) => {
    const clientId = `spec-confidential-${crypto.randomUUID()}`;
    const input: CreateOpenApplicationInputDto = {
      clientId,
      displayName: 'Spec confidential client',
      applicationType: 'service',
      clientType: 'confidential',
      consentType: 'systematic',
      redirectUris: [],
      postLogoutRedirectUris: [],
      permissions: ['ept:token', 'gt:client_credentials'],
      requirements: [],
    };
    const http = TestBed.inject(HttpClient);

    http.post<OpenApplicationOutputDto>('/api/v1/open-applications', input).subscribe({
      next: (created) => {
        expect(created.clientSecret).toContain('mock-secret-');
        expect(created.hasClientSecret).toBeTrue();

        http.get<OpenApplicationOutputDto>(`/api/v1/open-applications/${clientId}`).subscribe({
          next: (stored) => {
            expect(stored.clientSecret).toBeUndefined();
            done();
          },
          error: done.fail,
        });
      },
      error: done.fail,
    });
  });
  //#endif
});
