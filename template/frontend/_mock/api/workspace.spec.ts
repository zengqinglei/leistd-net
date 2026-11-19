//#if (TenantWorkspace)
import { HttpHeaders, HttpRequest } from '@angular/common/http';

import { MockRequest } from '../core/models';
import { withinWorkspace } from '../core/request-workspace';
import { USERS } from '../data/user';
import { STAFF_ACCESS } from '../data/workspace';
import {
  getCurrentUser,
  setMockSessionTenantKey,
  setMockSessionUserId,
} from '../utils/current-user';
import { getCurrentPermissions } from './authorization';
import { WORKSPACE_API } from './workspace';

function request(workspace: string, url = '/api/v1/permissions/current'): MockRequest {
  const headers = new HttpHeaders({ 'X-Workspace': workspace });
  return {
    url,
    headers,
    original: new HttpRequest('GET', url, { headers }),
    body: null,
    params: {},
    queryParams: {},
  };
}

describe('workspace mock', () => {
  const users = structuredClone(USERS);
  const access = structuredClone(STAFF_ACCESS);
  beforeEach(() => setMockSessionUserId('user_admin'));
  afterEach(() => {
    USERS.splice(0, USERS.length, ...structuredClone(users));
    STAFF_ACCESS.splice(0, STAFF_ACCESS.length, ...structuredClone(access));
    setMockSessionUserId(null);
  });

  it('returns distinct forbidden and unavailable errors for rejected workspace requests', () => {
    setMockSessionTenantKey('tenant_acme');
    expect(() => withinWorkspace(request('globex'), () => getCurrentUser())).toThrowError(
      expect.objectContaining({ status: 403, error: { code: 'Workspace:Forbidden' } }),
    );
    setMockSessionTenantKey('host');
    expect(() => withinWorkspace(request('missing'), () => getCurrentUser())).toThrowError(
      expect.objectContaining({ status: 403, error: { code: 'Workspace:Unavailable' } }),
    );
  });

  it('isolates permissions for same-named linked accounts without changing the home session', () => {
    WORKSPACE_API['GET /api/v1/auth/me/workspaces']();
    const acme = withinWorkspace(request('acme'), () => ({
      user: getCurrentUser(),
      permissions: getCurrentPermissions(),
    }));
    const globex = withinWorkspace(request('globex'), () => ({
      user: getCurrentUser(),
      permissions: getCurrentPermissions(),
    }));
    expect(acme.user?.id).not.toBe(globex.user?.id);
    expect(acme.user?.username).toBe(globex.user?.username);
    expect(acme.permissions.permissions).toContain('App.Users');
    expect(globex.permissions.permissions).toEqual([]);
    expect(acme.permissions.permissions).not.toContain('App.Tenants');
    //#if (IncludeDistributedEvents)
    expect(acme.permissions.permissions).toContain('App.DistributedEvents');
    //#endif
    expect(getCurrentUser()?.id).toBe('user_admin');
    expect(withinWorkspace(request('missing', '/api/v1/auth/me'), () => getCurrentUser()?.id)).toBe(
      'user_admin',
    );
  });
  //#if (LocalIdentity)

  it('lists the stored linked account username instead of deriving it from the home username', () => {
    const list = () =>
      WORKSPACE_API['GET /api/v1/tenants/:tenantId/staff']({
        ...request('acme'),
        params: { tenantId: 'tenant_acme' },
      });
    expect(list()).toEqual([
      expect.objectContaining({ username: 'admin', linkedUsername: 'platform.admin' }),
    ]);
    WORKSPACE_API['GET /api/v1/auth/me/workspaces']();
    USERS.find((user) => user.id === 'platform-tenant_acme-user_admin')!.username =
      'platform.admin-renamed';
    expect(list()).toEqual([
      expect.objectContaining({ username: 'admin', linkedUsername: 'platform.admin-renamed' }),
    ]);
  });
  //#endif
});
//#endif
