//#if (RemoteTokenAuth)
import { RESOURCE_AUTH_API } from '../../../../_mock/api/resource-auth';
import { MockException } from '../../../../_mock/core/models';
import { USERS } from '../../../../_mock/data/user';
import {
  getMockSessionSubjectId,
  getMockSessionTenantKey,
  setMockSessionIdentity,
  setMockSessionUserId,
} from '../../../../_mock/utils/current-user';

describe('Resource mock server session', () => {
  beforeEach(() => setMockSessionUserId(null));
  afterEach(() => setMockSessionUserId(null));

  it('requires a session and removes it on logout', () => {
    expect(() => RESOURCE_AUTH_API['GET /api/v1/auth/me']()).toThrow(MockException);
    RESOURCE_AUTH_API['POST /api/v1/auth/login']();
    expect(RESOURCE_AUTH_API['GET /api/v1/auth/me']().id).toBe(USERS[0].id);
    RESOURCE_AUTH_API['POST /api/v1/auth/logout']();
    expect(() => RESOURCE_AUTH_API['GET /api/v1/auth/me']()).toThrow(MockException);
  });

  it('keeps the server subject distinct from its display persona and pins the tenant', () => {
    const subject = crypto.randomUUID();
    const tenant = crypto.randomUUID();
    setMockSessionIdentity(subject, USERS[0].id, tenant);
    expect(getMockSessionSubjectId()).toBe(subject);
    expect(getMockSessionTenantKey()).toBe(tenant);
    expect(RESOURCE_AUTH_API['GET /api/v1/auth/me']().tenantId).toBe(tenant);
  });
});
//#endif
