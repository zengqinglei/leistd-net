//#if (!LocalIdentity)
import { MockRequest } from '../core/models';
//#endif
import { MockUser, USERS } from '../data/user';

// Mock session state — BFF 模式下使用浏览器会话存储模拟 Cookie 会话
const MOCK_SESSION_STORAGE_KEY = 'mock_session_user_id';
const MOCK_SESSION_TENANT_STORAGE_KEY = 'mock_session_tenant_key';
const MOCK_SESSION_SUBJECT_STORAGE_KEY = 'mock_session_subject_id';

export let MOCK_SESSION_USER_ID: string | null = readMockSessionUserId();

export function setMockSessionUserId(id: string | null) {
  MOCK_SESSION_USER_ID = id;
  if (id) {
    sessionStorage.setItem(MOCK_SESSION_STORAGE_KEY, id);
  } else {
    sessionStorage.removeItem(MOCK_SESSION_STORAGE_KEY);
    sessionStorage.removeItem(MOCK_SESSION_TENANT_STORAGE_KEY);
    sessionStorage.removeItem(MOCK_SESSION_SUBJECT_STORAGE_KEY);
  }
}

/**
 * 主体标识：按用户隔离数据时用它，而不是用 Mock persona 的 id。
 *
 * 有本地身份时两者相同（会话用户就是 Mock 用户）；Resource 形态下主体标识是令牌里的
 * 原始 `sub`（GUID），persona 只是界面上借用的那个测试用户——混用会让同租户下的两个
 * 真实用户共用一份用户级数据。
 */
export function getMockSessionSubjectId(): string | null {
  return sessionStorage.getItem(MOCK_SESSION_SUBJECT_STORAGE_KEY) ?? MOCK_SESSION_USER_ID;
}

/**
 * 登录时把租户一起定案进会话。
 *
 * 真实后端的租户解析链首位是「已认证主体的租户声明」，且主体一经处理就终止解析——
 * 请求头改不了已登录用户的租户（见 CurrentPrincipalTenantResolveContributor）。
 * 所以 X-Tenant-Id 只在匿名阶段（登录、注册、验证码）起作用，认证后的接口一律读会话。
 * Mock 若继续每次从请求头取租户，锁定的就是生产环境不存在的行为。
 */
export function setMockSessionTenantKey(tenantKey: string | null) {
  if (tenantKey) {
    sessionStorage.setItem(MOCK_SESSION_TENANT_STORAGE_KEY, tenantKey);
  } else {
    sessionStorage.removeItem(MOCK_SESSION_TENANT_STORAGE_KEY);
  }
}

/** 当前会话的租户键；宿主上下文为 `host`。 */
export function getMockSessionTenantKey(): string {
  return sessionStorage.getItem(MOCK_SESSION_TENANT_STORAGE_KEY) ?? 'host';
}

function readMockSessionUserId() {
  return sessionStorage.getItem(MOCK_SESSION_STORAGE_KEY);
}

/** 返回当前会话用户；未登录时返回 null（不抛异常，供权限接口自行决定 401/403）。 */
export function getCurrentUser(): MockUser | null {
  const userId = MOCK_SESSION_USER_ID ?? readMockSessionUserId();
  return userId ? (USERS.find((item) => item.id === userId) ?? null) : null;
}
//#if (!LocalIdentity)

/**
 * 按当前请求的 Bearer 令牌重建 Mock 主体。
 *
 * 没有本地身份的形态（Resource）登录走远端 OIDC，不经 HttpClient，因此不会有任何
 * Mock 请求去建立会话——而 user / authorization / setting 这些 Mock 都按会话取主体，
 * 结果是「OIDC 已登录，开着 useMock 访问业务接口却一律 401」，前端没法脱离后端跑。
 *
 * 这里从浏览器已持有的真实令牌里取声明，把主体补进会话。**只解析不验签**：
 * Mock 不是安全边界，验签、过期校验都是 Resource 服务端的事。
 *
 * 语义是**每请求覆盖**，不是「有则补上」：令牌缺失、解码失败或没有可用的 `sub` 时
 * 必须清空主体。否则一次合法请求之后，登出或令牌损坏的请求会继承上一个主体，
 * Mock 的 `[Authorize]` 行为就与当前请求脱节了。
 *
 * 三个概念分开，不要混：
 * - **主体标识**：令牌里的原始 `sub`，用于按用户隔离数据（设置、偏好等）。
 *   真实 `sub` 是 GUID，几乎不会出现在 Mock 用户表里。
 * - **展示 persona**：`sub` 匹配不到 Mock 用户时借用第一个测试用户，只为让界面
 *   有名字、头像和一套固定权限可演示。
 * - **权限**：仍由 persona 决定，不解析令牌里的角色声明——在前端复刻一遍后端的
 *   权限决策必然与后端漂移，漂移的 Mock 比没有 Mock 更危险。
 *
 * 把主体标识也换成 persona 的 id 会让同租户下的两个真实用户共用一份用户级数据。
 */
function setMockSessionSubject(subjectId: string | null, personaUserId: string | null): void {
  setMockSessionUserId(personaUserId);
  if (subjectId) {
    sessionStorage.setItem(MOCK_SESSION_SUBJECT_STORAGE_KEY, subjectId);
  } else {
    sessionStorage.removeItem(MOCK_SESSION_SUBJECT_STORAGE_KEY);
  }
}

export function syncMockSubjectFromBearer(req: MockRequest): void {
  const claims = decodeBearerClaims(req);
  const sub = typeof claims?.['sub'] === 'string' && claims['sub'] ? claims['sub'] : null;

  if (!sub) {
    // 无主体即未认证：各 Mock 照常返回 401。
    setMockSessionSubject(null, null);
    setMockSessionTenantKey(null);
    return;
  }

  // 主体标识用原始 sub；persona 只在 sub 不在 Mock 用户表里时借用第一个测试用户。
  const persona = USERS.find((item) => item.id === sub) ?? USERS[0];
  setMockSessionSubject(sub, persona.id);

  const tenantId = claims?.['tenant_id'];
  setMockSessionTenantKey(typeof tenantId === 'string' && tenantId ? tenantId : null);
}

function decodeBearerClaims(req: MockRequest): Record<string, unknown> | null {
  const authorization = req.headers.get('Authorization');
  const token = authorization?.startsWith('Bearer ') ? authorization.slice(7) : null;
  if (!token) {
    return null;
  }

  const payload = token.split('.')[1];
  if (!payload) {
    return null;
  }

  try {
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'));
    return JSON.parse(json) as Record<string, unknown>;
  } catch {
    // 令牌不是 JWT（或被截断）时当作没有主体。
    return null;
  }
}
//#endif
