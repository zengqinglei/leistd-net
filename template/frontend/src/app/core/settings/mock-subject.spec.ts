//#if (!LocalIdentity)
import {
  HttpHeaders,
  HttpInterceptorFn,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import { TestBed } from '@angular/core/testing';

import { SettingService } from './setting-service';
import { SETTING_API } from '../../../../_mock/api/setting';
import { MOCK_APIS, mockInterceptor } from '../../../../_mock/core/interceptor';
import { MockException } from '../../../../_mock/core/models';
import { USER_SETTING_VALUES } from '../../../../_mock/data/settings';
import {
  setMockSessionTenantKey,
  setMockSessionUserId,
  syncMockSubjectFromBearer,
} from '../../../../_mock/utils/current-user';
import { environment } from '../../../environments/environment';

function jwt(claims: Record<string, unknown>): string {
  const encode = (value: unknown) =>
    btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_');
  return `${encode({ alg: 'none' })}.${encode(claims)}.signature`;
}

/**
 * 没有本地身份的形态（Resource）下的 Mock 主体来源。
 *
 * 这里的登录走远端 OIDC，不经 HttpClient，所以没有任何 Mock 请求会建立会话——
 * 而 user / authorization / setting 这些 Mock 都按会话取主体。缺了这一步，
 * 「OIDC 已登录、开着 useMock 访问业务接口却一律 401」，前端就没法脱离后端跑。
 */
describe('mock subject from bearer claims', () => {
  const api = SETTING_API as unknown as Record<string, (req: { body: unknown }) => unknown>;

  function statusOf(run: () => unknown): number {
    try {
      run();
    } catch (error: unknown) {
      if (error instanceof MockException) return error.status;
      throw error;
    }
    return 200;
  }

  beforeEach(() => {
    USER_SETTING_VALUES.clear();
    // 会话用户 id 有模块级缓存，清 sessionStorage 不够，得走 setter 复位。
    setMockSessionUserId(null);
    setMockSessionTenantKey(null);
  });

  afterEach(() => {
    setMockSessionUserId(null);
    setMockSessionTenantKey(null);
  });

  it('stays unauthenticated without a token', () => {
    expect(statusOf(() => api['GET /api/v1/settings']({ body: null }))).toBe(401);
  });

  it('establishes the subject and tenant from the token', () => {
    syncMockSubjectFromBearer({
      headers: new HttpHeaders({
        Authorization: `Bearer ${jwt({ sub: 'user_admin', tenant_id: 'tenant_acme' })}`,
      }),
    } as never);

    api['PUT /api/v1/settings/current-user']({
      body: { name: 'Display.TimeZone', value: 'UTC' },
    });

    // 主体与租户都来自令牌声明，写入落在该租户下，键用的是原始 sub
    expect(USER_SETTING_VALUES.get('tenant_acme:user_admin:Display.TimeZone')).toBe('UTC');
  });

  // 上面两条直接调解析函数，证明的是解析本身。这一条走 HttpClient → Mock 拦截器 → handler
  // 的完整链路：拦截器忘了在分发前同步主体的话，前两条照样全绿，而实际访问仍然 401。
  it('is wired into the mock interceptor', async () => {
    const originalUseMock = environment.useMock;
    environment.useMock = true;

    // Authorization 在真实环境由认证拦截器附上；这里用最小替身，好让链路其余部分按真实顺序跑。
    const attachBearer: HttpInterceptorFn = (req, next) =>
      next(req.clone({ setHeaders: { Authorization: `Bearer ${jwt({ sub: 'user_admin' })}` } }));

    try {
      TestBed.configureTestingModule({
        providers: [
          SettingService,
          provideHttpClient(withInterceptors([attachBearer, mockInterceptor])),
          { provide: MOCK_APIS, useValue: SETTING_API },
        ],
      });

      const rows = await new Promise<unknown[]>((resolve, reject) =>
        TestBed.inject(SettingService)
          .getSettings()
          .subscribe({ next: resolve as never, error: reject }),
      );

      expect(rows.length).toBeGreaterThan(0);
    } finally {
      environment.useMock = originalUseMock;
    }
  });

  // 同步是「每请求覆盖」，不是「有则补上」：一次合法请求之后，登出、令牌丢失或损坏的
  // 请求必须重新变成未认证。粘住上一个主体的话，Mock 的 [Authorize] 行为就与当前请求脱节。
  it('drops the subject when the next request has no usable token', () => {
    const cases: (HttpHeaders | undefined)[] = [
      undefined, // 无 Authorization 头（登出后）
      new HttpHeaders({ Authorization: 'Bearer not-a-jwt' }), // 损坏令牌
      new HttpHeaders({ Authorization: `Bearer ${jwt({ tenant_id: 'tenant_acme' })}` }), // 缺 sub
      new HttpHeaders({ Authorization: `Bearer ${jwt({ sub: '' })}` }), // sub 为空串
    ];

    for (const headers of cases) {
      // 先建立一个合法主体
      syncMockSubjectFromBearer({
        headers: new HttpHeaders({ Authorization: `Bearer ${jwt({ sub: 'user_admin' })}` }),
      } as never);
      expect(statusOf(() => api['GET /api/v1/settings']({ body: null }))).toBe(200);

      // 再用一个不可用的令牌发请求
      syncMockSubjectFromBearer({ headers: headers ?? new HttpHeaders() } as never);
      expect(statusOf(() => api['GET /api/v1/settings']({ body: null }))).toBe(401);
    }
  });

  // 真实 sub 是 GUID，几乎不会出现在 Mock 用户表里，于是都借用同一个 persona 展示。
  // 但隔离键必须是原始 sub——否则同租户下两个真实用户会互相看到个人偏好。
  it('isolates two real subjects that share one persona', () => {
    const first = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';
    const second = '3f2504e0-4f89-11d3-9a0c-0305e82c3302';

    syncMockSubjectFromBearer({
      headers: new HttpHeaders({ Authorization: `Bearer ${jwt({ sub: first })}` }),
    } as never);
    api['PUT /api/v1/settings/current-user']({
      body: { name: 'Display.TimeZone', value: 'UTC' },
    });

    syncMockSubjectFromBearer({
      headers: new HttpHeaders({ Authorization: `Bearer ${jwt({ sub: second })}` }),
    } as never);
    const secondRows = api['GET /api/v1/settings']({ body: null }) as {
      name: string;
      userValue: string | null;
    }[];
    expect(secondRows.find((s) => s.name === 'Display.TimeZone')?.userValue).toBeNull();

    // 两个主体各自成键，都不是 persona 的 id
    expect(USER_SETTING_VALUES.get(`host:${first}:Display.TimeZone`)).toBe('UTC');
    expect(USER_SETTING_VALUES.get('host:user_admin:Display.TimeZone')).toBeUndefined();
  });
});
//#endif
