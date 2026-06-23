import { MockException, MockRequest } from '../core/models';
import { MockUser, USERS } from '../data/user';

// Mock session state — BFF 模式下使用浏览器会话存储模拟 Cookie 会话
const MOCK_SESSION_STORAGE_KEY = 'mock_session_user_id';

export let MOCK_SESSION_USER_ID: string | null = readMockSessionUserId();

export function setMockSessionUserId(id: string | null) {
  MOCK_SESSION_USER_ID = id;
  if (id) {
    sessionStorage.setItem(MOCK_SESSION_STORAGE_KEY, id);
  } else {
    sessionStorage.removeItem(MOCK_SESSION_STORAGE_KEY);
  }
}

export function getUserByToken(_req: MockRequest): MockUser {
  const userId = MOCK_SESSION_USER_ID ?? readMockSessionUserId();
  MOCK_SESSION_USER_ID = userId;
  if (!userId) {
    throw new MockException(401, { code: 40100, message: '未提供认证令牌' });
  }
  const user = USERS.find(item => item.id === userId);

  if (!user) {
    throw new MockException(401, { code: 40100, message: '无效的认证令牌' });
  }

  return user;
}

export function getCurrentUserId(req: MockRequest): string {
  return getUserByToken(req).id;
}

function readMockSessionUserId() {
  return sessionStorage.getItem(MOCK_SESSION_STORAGE_KEY);
}
