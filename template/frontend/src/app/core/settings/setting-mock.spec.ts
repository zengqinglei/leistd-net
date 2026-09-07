//#if (LocalIdentity)
import { HttpHeaders } from '@angular/common/http';

import { AUTH_API } from '../../../../_mock/api/auth';
//#endif
import { SETTING_API } from '../../../../_mock/api/setting';
import { MockException } from '../../../../_mock/core/models';
import { TENANT_SETTING_VALUES, USER_SETTING_VALUES } from '../../../../_mock/data/settings';
//#if (ExternalLogin)
import { USERS } from '../../../../_mock/data/user';
//#endif
import {
  setMockSessionTenantKey,
  setMockSessionUserId,
} from '../../../../_mock/utils/current-user';

type MockHandler = (req: { body: unknown }) => unknown;

function request(body: unknown = null) {
  return { body };
}

/** 模拟登录：真实环境下用户与租户在登录那一刻一起定案。 */
function signIn(userId: string, tenantKey: string | null = null): void {
  setMockSessionUserId(userId);
  setMockSessionTenantKey(tenantKey);
}

interface SettingRow {
  name: string;
  userValue: string | null;
  tenantValue: string | null;
}

/**
 * Mock 契约。
 *
 * Mock 是 `useMock` 开发模式下的唯一后端，它与真实 API 的每一处差异都会在联调时才暴露。
 * 因此这里断言的不只是路由存在，还有身份、权限、错误状态与主体隔离——这些恰恰是
 * 「直接调 handler 看它抛不抛」测不出来的东西。
 */
describe('settings mock', () => {
  const api = SETTING_API as unknown as Record<string, MockHandler>;
  const get = 'GET /api/v1/settings';
  const putUser = 'PUT /api/v1/settings/current-user';
  const putTenant = 'PUT /api/v1/settings/current-tenant';
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
    TENANT_SETTING_VALUES.clear();
    setMockSessionUserId(null);
    setMockSessionTenantKey(null);
  });

  afterEach(() => {
    setMockSessionUserId(null);
    setMockSessionTenantKey(null);
  });

  it('registers the three settings endpoints', () => {
    expect(Object.keys(SETTING_API).sort()).toEqual([get, putTenant, putUser]);
  });

  // Controller 整体 [Authorize]：匿名一律 401，不是空列表。
  it('requires authentication', () => {
    expect(statusOf(() => api[get](request()))).toBe(401);
    expect(statusOf(() => api[putUser](request({ name: 'Display.TimeZone', value: 'UTC' })))).toBe(
      401,
    );
  });

  // 改自己的偏好只要登录，改系统默认值要 App.Settings——两个端点存在的理由就是这层差异。
  it('separates personal preferences from system defaults by permission', () => {
    signIn('user_demo');
    expect(statusOf(() => api[putUser](request({ name: 'Display.TimeZone', value: 'UTC' })))).toBe(
      200,
    );
    expect(
      statusOf(() => api[putTenant](request({ name: 'Display.TimeZone', value: 'UTC' }))),
    ).toBe(403);

    signIn('user_admin');
    expect(
      statusOf(() => api[putTenant](request({ name: 'Display.TimeZone', value: 'UTC' }))),
    ).toBe(200);
  });

  // 用户级覆盖按主体键控：共用一份全局值会让切换用户后看到上一个人的偏好。
  it('keeps user overrides separate per user', () => {
    signIn('user_admin');
    api[putUser](request({ name: 'Display.TimeZone', value: 'UTC' }));

    signIn('user_demo');
    const demoRows = api[get](request()) as SettingRow[];
    expect(demoRows.find((s) => s.name === 'Display.TimeZone')?.userValue).toBeNull();

    signIn('user_admin');
    const adminRows = api[get](request()) as SettingRow[];
    expect(adminRows.find((s) => s.name === 'Display.TimeZone')?.userValue).toBe('UTC');
  });

  it('applies layered writes and null clears an override', () => {
    signIn('user_admin');
    api[putUser](request({ name: 'Display.TimeZone', value: 'UTC' }));
    api[putTenant](request({ name: 'Display.TimeZone', value: 'America/New_York' }));

    let row = (api[get](request()) as SettingRow[]).find((s) => s.name === 'Display.TimeZone');
    expect(row?.userValue).toBe('UTC');
    expect(row?.tenantValue).toBe('America/New_York');

    api[putUser](request({ name: 'Display.TimeZone', value: null }));
    row = (api[get](request()) as SettingRow[]).find((s) => s.name === 'Display.TimeZone');
    expect(row?.userValue).toBeNull();
    expect(row?.tenantValue).toBe('America/New_York');
  });

  // 设置行带租户归属，用户级也一样：同一个人在 Acme 设的偏好不该在 Globex 里出现。
  // 真实 Store 的键是 `{tenant}:u:{userId}`，Mock 少了租户这一维就会串值。
  //
  // 租户随登录一起切换——这也是真实环境唯一能换租户的方式：已认证主体的租户声明定案后，
  // 请求头就改不动它了。
  it('keeps overrides separate per tenant', () => {
    const acme = 'tenant_acme';
    const globex = 'tenant_globex';

    signIn('user_admin', acme);
    api[putUser](request({ name: 'Display.TimeZone', value: 'UTC' }));
    api[putTenant](request({ name: 'Display.TimeZone', value: 'Europe/London' }));

    signIn('user_admin', globex);
    const inGlobex = (api[get](request()) as SettingRow[]).find(
      (s) => s.name === 'Display.TimeZone',
    );
    expect(inGlobex?.userValue).toBeNull();
    expect(inGlobex?.tenantValue).toBeNull();

    signIn('user_admin', acme);
    const backInAcme = (api[get](request()) as SettingRow[]).find(
      (s) => s.name === 'Display.TimeZone',
    );
    expect(backInAcme?.userValue).toBe('UTC');
    expect(backInAcme?.tenantValue).toBe('Europe/London');

    // 宿主上下文（登录时没有租户）同样与任何租户隔离
    signIn('user_admin');
    const inHost = (api[get](request()) as SettingRow[]).find((s) => s.name === 'Display.TimeZone');
    expect(inHost?.userValue).toBeNull();
    expect(inHost?.tenantValue).toBeNull();
  });

  //#if (LocalIdentity)
  // 前面几条都是直接摆好会话再测设置，证明的是「会话建立之后」的行为。
  // 这一条从登录入口进：租户在登录那一刻由请求头定案、写进会话，之后设置读写按会话走。
  // 认证到会话这段接线断了的话，上面那些用例照样全绿。
  it('picks up the tenant pinned by the login endpoint', () => {
    const auth = AUTH_API as unknown as Record<
      string,
      (req: { body: unknown; headers: HttpHeaders }) => unknown
    >;
    const acme = 'tenant_acme';

    auth['POST /api/v1/auth/session-login']({
      body: { usernameOrEmail: 'admin', password: 'Admin@123456' },
      headers: new HttpHeaders({ 'X-Tenant-Id': acme }),
    });

    api[putUser](request({ name: 'Display.TimeZone', value: 'UTC' }));

    // 写入落在登录时定案的那个租户下，而不是宿主
    expect(USER_SETTING_VALUES.get(`${acme}:user_admin:Display.TimeZone`)).toBe('UTC');
    expect(USER_SETTING_VALUES.get('host:user_admin:Display.TimeZone')).toBeUndefined();
  });
  //#if (ExternalLogin)

  // 外部登录是另一条入口：回调仍是匿名请求、会带上登录前选定的租户头，真实后端在这一步
  // 进入该租户上下文。回调把租户固定成宿主的话，密码登录那条用例照样全绿。
  it('picks up the tenant pinned by the external login callback', () => {
    const auth = AUTH_API as unknown as Record<
      string,
      (req: { body: unknown; headers: HttpHeaders; params?: Record<string, string> }) => unknown
    >;
    const acme = 'tenant_acme';

    auth['POST /api/v1/external-auth/:provider/callback']({
      body: { code: 'code', state: 'state' },
      headers: new HttpHeaders({ 'X-Tenant-Id': acme }),
      params: { provider: 'github' },
    });

    api[putUser](request({ name: 'Display.TimeZone', value: 'Europe/London' }));

    const subject = USERS[0].id;
    expect(USER_SETTING_VALUES.get(`${acme}:${subject}:Display.TimeZone`)).toBe('Europe/London');
    expect(USER_SETTING_VALUES.get(`host:${subject}:Display.TimeZone`)).toBeUndefined();
  });
  //#endif

  //#endif
  it('rejects the values and names the backend rejects', () => {
    signIn('user_admin');

    // 未定义的设置名 → 404；先查定义再校验值，顺序与后端一致
    expect(statusOf(() => api[putUser](request({ name: 'Nope.Missing', value: 'x' })))).toBe(404);
    // 空串不是清除协议——清除只有 null
    expect(statusOf(() => api[putUser](request({ name: 'Display.TimeZone', value: '' })))).toBe(
      400,
    );
    // Windows 时区 ID：后端认、浏览器不认，两端必须统一只收 IANA
    expect(
      statusOf(() =>
        api[putUser](request({ name: 'Display.TimeZone', value: 'China Standard Time' })),
      ),
    ).toBe(400);
  });
});
