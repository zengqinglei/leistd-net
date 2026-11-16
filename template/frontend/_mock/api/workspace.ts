//#if (TenantWorkspace)
// prettier-ignore
import { MockException,
  //#if (LocalIdentity)
  MockRequest,
  //#endif
} from '../core/models';
//#if (LocalIdentity)
import { ROLES } from '../data/authorization';
//#endif
import { TENANTS } from '../data/tenant';
//#if (OpenIddictServer)
import { toTenantOutput } from '../data/tenant';
//#endif
//#if (LocalIdentity)
import { USERS } from '../data/user';
//#endif
import { STAFF_ACCESS } from '../data/workspace';
import { getCurrentUser, getWorkspaceMember, getMockSessionTenantKey } from '../utils/current-user';

function home() {
  const user = getCurrentUser();
  if (!user) throw new MockException(401);
  return user;
}
//#if (LocalIdentity)
function staff(tenantId: string) {
  return STAFF_ACCESS.filter((access) => access.tenantId === tenantId).map((access) => {
    const user = USERS.find((u) => u.id === access.homeUserId)!;
    const member = USERS.find((u) => u.homeUserId === user.id && u.workspaceTenantId === tenantId);
    return {
      homeUserId: user.id,
      memberId: member?.id ?? `platform-${tenantId}-${user.id}`,
      username: user.username,
      displayName: user.displayName,
      isActive: member?.isActive ?? true,
      creationTime: access.creationTime,
      roles: ROLES.filter((r) => access.roleIds.includes(r.id)),
    };
  });
}
//#endif
export const WORKSPACE_API = {
  'GET /api/v1/auth/me/workspaces': () => {
    const user = home();
    const ownTenant = getMockSessionTenantKey();
    if (ownTenant !== 'host')
      return TENANTS.filter(
        (tenant) => tenant.isActive && (tenant.id === ownTenant || tenant.name === ownTenant),
      ).map((tenant) => ({
        userId: user.id,
        tenantId: tenant.id,
        name: tenant.name,
        displayName: tenant.displayName,
      }));
    return TENANTS.filter(
      (t) =>
        t.isActive && STAFF_ACCESS.some((a) => a.homeUserId === user.id && a.tenantId === t.id),
    )
      .filter((tenant) => getWorkspaceMember(user, tenant.id).isActive)
      .map((t) => ({
        userId: getWorkspaceMember(user, t.id).id,
        tenantId: t.id,
        name: t.name,
        displayName: t.displayName,
      }));
  },
  //#if (LocalIdentity)
  'GET /api/v1/tenants/:tenantId/staff': (req: MockRequest) => {
    home();
    return staff(req.params.tenantId);
  },
  'GET /api/v1/tenants/:tenantId/staff/candidates': (req: MockRequest) => {
    home();
    const keyword = String(req.queryParams['keyword'] ?? '').toLowerCase();
    return USERS.filter(
      (user) =>
        !user.homeUserId &&
        (!keyword || `${user.username} ${user.displayName ?? ''}`.toLowerCase().includes(keyword)),
    )
      .slice(0, 200)
      .map(({ id, username, displayName }) => ({ id, username, displayName }));
  },
  'GET /api/v1/tenants/:tenantId/staff/roles': () => {
    home();
    return ROLES;
  },
  'POST /api/v1/tenants/:tenantId/staff': (req: MockRequest) => {
    home();
    const id = req.params.tenantId;
    const body = req.body;
    if (!body.homeUserId || !body.roleIds?.length) throw new MockException(400);
    const existing = STAFF_ACCESS.find(
      (a) => a.tenantId === id && a.homeUserId === body.homeUserId,
    );
    if (!existing) {
      const member = USERS.find(
        (user) => user.homeUserId === body.homeUserId && user.workspaceTenantId === id,
      );
      if (member?.workspaceAccessRemoved) {
        member.workspaceAccessRemoved = false;
        member.isActive = true;
      }
    }
    if (existing) existing.roleIds = body.roleIds;
    else
      STAFF_ACCESS.push({
        tenantId: id,
        homeUserId: body.homeUserId,
        roleIds: body.roleIds,
        creationTime: new Date().toISOString(),
      });
    return null;
  },
  'DELETE /api/v1/tenants/:tenantId/staff/:homeUserId': (req: MockRequest) => {
    home();
    const index = STAFF_ACCESS.findIndex(
      (a) => a.tenantId === req.params.tenantId && a.homeUserId === req.params.homeUserId,
    );
    if (index >= 0) {
      STAFF_ACCESS.splice(index, 1);
      const member = USERS.find(
        (user) =>
          user.homeUserId === req.params.homeUserId &&
          user.workspaceTenantId === req.params.tenantId,
      );
      if (member) member.workspaceAccessRemoved = true;
    }
    return null;
  },
  //#endif
  //#if (OpenIddictServer)
  'GET /api/v1/workspace-tenants/:id': (req: MockRequest) =>
    TENANTS.find((t) => t.id === req.params.id) ?? { status: 204 },
  'GET /api/v1/workspace-tenants/by-name/:name': (req: MockRequest) => {
    const tenant = TENANTS.find((t) => t.name.toLowerCase() === req.params.name.toLowerCase());
    return tenant
      ? { ...toTenantOutput(tenant), normalizedName: tenant.name.toUpperCase() }
      : { status: 204 };
  },
  //#endif
};
//#endif
