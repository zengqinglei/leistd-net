//#if (!IncludeMultiTenancy)
import { MockException } from '../core/models';
//#endif
import { MockUser, USERS } from '../data/user';

// Mock session state — BFF 模式下使用浏览器会话存储模拟 Cookie 会话
const MOCK_SESSION_STORAGE_KEY = 'mock_session_user_id';
const MOCK_SESSION_TENANT_STORAGE_KEY = 'mock_session_tenant_key';
const MOCK_SESSION_SUBJECT_STORAGE_KEY = 'mock_session_subject_id';
//#if (Impersonation)
const MOCK_SESSION_IMPERSONATOR_STORAGE_KEY = 'mock_session_impersonator';
//#endif

export let MOCK_SESSION_USER_ID: string | null = readMockSessionUserId();

export function setMockSessionUserId(id: string | null) {
  MOCK_SESSION_USER_ID = id;
  if (id) {
    sessionStorage.setItem(MOCK_SESSION_STORAGE_KEY, id);
  } else {
    sessionStorage.removeItem(MOCK_SESSION_STORAGE_KEY);
    sessionStorage.removeItem(MOCK_SESSION_TENANT_STORAGE_KEY);
    sessionStorage.removeItem(MOCK_SESSION_SUBJECT_STORAGE_KEY);
    //#if (Impersonation)
    sessionStorage.removeItem(MOCK_SESSION_IMPERSONATOR_STORAGE_KEY);
    //#endif
  }
}
//#if (Impersonation)

/** 模拟登录的发起人，对应真实会话里的发起人声明；随会话一起保存、退出登录时一起清掉。 */
export interface MockImpersonator {
  userId: string;
  name: string;
  /** 发起人所在的租户键，结束模拟时回到这里。 */
  tenantKey: string;
}

export function getMockImpersonator(): MockImpersonator | null {
  const value = sessionStorage.getItem(MOCK_SESSION_IMPERSONATOR_STORAGE_KEY);
  return value ? (JSON.parse(value) as MockImpersonator) : null;
}

export function setMockImpersonator(impersonator: MockImpersonator | null): void {
  if (impersonator) {
    sessionStorage.setItem(MOCK_SESSION_IMPERSONATOR_STORAGE_KEY, JSON.stringify(impersonator));
  } else {
    sessionStorage.removeItem(MOCK_SESSION_IMPERSONATOR_STORAGE_KEY);
  }
}
//#endif

/**
 * 主体标识：按用户隔离数据时用它，而不是用 Mock persona 的 id。
 *
 * 有本地身份时两者相同（会话用户就是 Mock 用户）；Resource 形态下主体标识是服务端会话里的
 * 主体 ID（GUID），persona 只是界面上借用的那个测试用户——混用会让同租户下的两个
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
 * 所以租户提示头只在匿名阶段（登录、注册、验证码）起作用，认证后的接口一律读会话。
 * Mock 若继续每次从请求头取租户，锁定的就是生产环境不存在的行为。
 */
export function setMockSessionTenantKey(tenantKey: string | null) {
  //#if (!IncludeMultiTenancy)
  if (tenantKey && tenantKey !== 'host') {
    throw new MockException(401, {
      code: 'Tenant:InvalidScope',
      message: 'A host session is required',
    });
  }
  //#endif
  if (tenantKey) {
    sessionStorage.setItem(MOCK_SESSION_TENANT_STORAGE_KEY, tenantKey);
  } else {
    sessionStorage.removeItem(MOCK_SESSION_TENANT_STORAGE_KEY);
  }
}

/** 当前会话的租户键；宿主上下文为 `host`。 */
export function getMockSessionTenantKey(): string {
  const tenantKey = sessionStorage.getItem(MOCK_SESSION_TENANT_STORAGE_KEY) ?? 'host';
  //#if (!IncludeMultiTenancy)
  if (tenantKey !== 'host') {
    throw new MockException(401, {
      code: 'Tenant:InvalidScope',
      message: 'A host session is required',
    });
  }
  //#endif
  return tenantKey;
}

function readMockSessionUserId() {
  return sessionStorage.getItem(MOCK_SESSION_STORAGE_KEY);
}

/** 返回当前会话用户；未登录时返回 null（不抛异常，供权限接口自行决定 401/403）。 */
export function getCurrentUser(): MockUser | null {
  const userId = MOCK_SESSION_USER_ID ?? readMockSessionUserId();
  if (userId) getMockSessionTenantKey();
  return userId ? (USERS.find((item) => item.id === userId) ?? null) : null;
}
/** 为 Mock 会话记录主体与展示 persona，二者可以不同。 */
export function setMockSessionIdentity(
  subjectId: string,
  personaUserId: string,
  tenantId: string | null,
): void {
  // Validate the scope before publishing the authenticated subject or persona.
  setMockSessionTenantKey(tenantId);
  setMockSessionUserId(personaUserId);
  sessionStorage.setItem(MOCK_SESSION_SUBJECT_STORAGE_KEY, subjectId);
}
