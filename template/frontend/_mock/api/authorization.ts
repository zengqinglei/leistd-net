import { PagedResultDto } from '../../src/app/shared/models/paged-result.dto';
import { MockException, MockRequest } from '../core/models';
import {
  ALL_PERMISSIONS,
  MockRole,
  PERMISSION_DEFINITIONS,
  PERMISSION_DESCENDANTS,
  PERMISSION_GRANTS,
  ROLES,
  grantKey,
} from '../data/authorization';
import { USERS } from '../data/user';
import { getCurrentUser } from '../utils/current-user';

/**
 * 角色与权限 Mock。
 *
 * 只复刻端点形状、401/403 和每个演示账号的固定权限集；三态组合规则、写时祖先归一化
 * 与定义树遍历以后端集成测试为唯一事实来源，这里不重复实现。
 */

function requireUser() {
  const user = getCurrentUser();
  if (!user) {
    throw new MockException(401, { code: 40100, message: 'Not authenticated' });
  }
  return user;
}

/** 计算当前用户的有效权限：超管全量，否则取其角色的固定授予集合并去掉显式拒绝。 */
function effectivePermissionsOf(username: string): { permissions: string[]; revision: string } {
  const user = USERS.find((candidate) => candidate.username === username);
  if (!user) {
    return { permissions: [], revision: 'anonymous' };
  }

  if (user.isSuperAdmin) {
    return { permissions: [...ALL_PERMISSIONS], revision: 'super-admin' };
  }

  const granted = new Set<string>();
  const prohibited = new Set<string>();
  const parts: string[] = [];

  for (const roleName of user.roles) {
    const role = ROLES.find((candidate) => candidate.name === roleName);
    if (!role) {
      continue;
    }

    const entry = PERMISSION_GRANTS[grantKey('Role', role.id)];
    parts.push(`${role.id}:${entry?.revision ?? 0}`);
    for (const grant of entry?.grants ?? []) {
      (grant.effect === 'Prohibited' ? prohibited : granted).add(grant.name);
    }
  }

  const userEntry = PERMISSION_GRANTS[grantKey('User', user.id)];
  for (const grant of userEntry?.grants ?? []) {
    (grant.effect === 'Prohibited' ? prohibited : granted).add(grant.name);
  }

  // 拒绝沿定义树向下传播：跨来源合并会产生「父拒子允」，与后端一样在此收口。
  for (const name of [...prohibited]) {
    for (const descendant of PERMISSION_DESCENDANTS[name] ?? []) {
      prohibited.add(descendant);
    }
  }

  return {
    permissions: [...granted].filter((name) => !prohibited.has(name)).sort(),
    revision: `u${userEntry?.revision ?? 0}|r${parts.sort().join(',')}`,
  };
}

/** 端点级 403：与后端的策略保护逐个对应。 */
export function requirePermission(permission: string) {
  const user = requireUser();
  const { permissions } = effectivePermissionsOf(user.username);
  if (!permissions.includes(permission)) {
    throw new MockException(403, { code: 40300, message: `Missing permission: ${permission}` });
  }
  return user;
}

export function getCurrentPermissions() {
  const user = requireUser();
  const { permissions, revision } = effectivePermissionsOf(user.username);
  const target = USERS.find((candidate) => candidate.username === user.username);
  return { permissions, isSuperAdmin: target?.isSuperAdmin ?? false, revision };
}

export function getPermissionDefinitions() {
  requirePermission('App.Permissions');
  return PERMISSION_DEFINITIONS;
}

export function getRoles(params: Record<string, unknown>): PagedResultDto<unknown> {
  requirePermission('App.Roles');

  const offset = Number(params['offset'] ?? 0);
  const limit = Number(params['limit'] ?? 10);
  const keyword = String(params['keyword'] ?? '').toLowerCase();

  const matched = ROLES.filter(
    (role) =>
      !keyword ||
      role.name.toLowerCase().includes(keyword) ||
      role.displayName.toLowerCase().includes(keyword),
  ).map((role) => ({
    ...role,
    userCount: USERS.filter((user) => user.roles.includes(role.name)).length,
    permissionCount: PERMISSION_GRANTS[grantKey('Role', role.id)]?.grants.length ?? 0,
  }));

  sortRoles(matched, String(params['sorting'] ?? ''));
  const items = matched.slice(offset, offset + limit);

  return { totalCount: matched.length, items };
}

type MockRoleRow = MockRole & { userCount: number; permissionCount: number };

/** 复刻列表页的列排序；未识别的字段回落到默认的排序号 + 名称。 */
function sortRoles(rows: MockRoleRow[], sorting: string): void {
  const [field, direction] = sorting.trim().split(/\s+/);
  const descending = direction === 'desc';
  const pick = (row: MockRoleRow): string | number => {
    switch (field) {
      case 'displayName':
        return row.displayName;
      case 'creationTime':
        return row.creationTime;
      default:
        return row.sort;
    }
  };

  rows.sort((left, right) => {
    const leftValue = pick(left);
    const rightValue = pick(right);
    const compared =
      typeof leftValue === 'number' && typeof rightValue === 'number'
        ? leftValue - rightValue
        : String(leftValue).localeCompare(String(rightValue));
    return (
      (compared !== 0 ? compared : left.name.localeCompare(right.name)) * (descending ? -1 : 1)
    );
  });
}

export function getRoleOptions() {
  requirePermission('App.Users.ManageRoles');
  return ROLES.sort((left, right) => left.sort - right.sort).map((role) => ({
    id: role.id,
    name: role.name,
    displayName: role.displayName,
  }));
}

export function createRole(body: Record<string, unknown>) {
  requirePermission('App.Roles.Create');

  const name = String(body['name'] ?? '').trim();
  if (ROLES.some((role) => role.name === name)) {
    throw new MockException(400, { code: 40000, message: `Role '${name}' already exists.` });
  }

  const role = {
    id: `role_${Date.now()}`,
    name,
    displayName: String(body['displayName'] ?? name),
    description: (body['description'] as string) || undefined,
    isStatic: false,
    isDefault: Boolean(body['isDefault']),
    sort: Number(body['sort'] ?? 0),
    creationTime: new Date().toISOString(),
  };
  ROLES.push(role);
  PERMISSION_GRANTS[grantKey('Role', role.id)] = { revision: 0, grants: [] };

  return { ...role, userCount: 0, permissionCount: 0 };
}

export function updateRole(id: string, body: Record<string, unknown>) {
  requirePermission('App.Roles.Update');

  const role = ROLES.find((candidate) => candidate.id === id);
  if (!role) {
    throw new MockException(404, { code: 40400, message: 'Role does not exist' });
  }

  role.displayName = String(body['displayName'] ?? role.displayName);
  role.description = (body['description'] as string) || undefined;
  role.sort = Number(body['sort'] ?? role.sort);
  role.isDefault = Boolean(body['isDefault']);
  role.lastModificationTime = new Date().toISOString();

  return {
    ...role,
    userCount: USERS.filter((user) => user.roles.includes(role.name)).length,
    permissionCount: PERMISSION_GRANTS[grantKey('Role', role.id)]?.grants.length ?? 0,
  };
}

export function deleteRole(id: string) {
  requirePermission('App.Roles.Delete');

  const index = ROLES.findIndex((candidate) => candidate.id === id);
  if (index < 0) {
    throw new MockException(404, { code: 40400, message: 'Role does not exist' });
  }

  const role = ROLES[index];
  if (role.isStatic) {
    throw new MockException(400, {
      code: 40000,
      message: `Built-in role '${role.name}' cannot be deleted.`,
    });
  }

  const assigned = USERS.filter((user) => user.roles.includes(role.name)).length;
  if (assigned > 0) {
    throw new MockException(400, {
      code: 40000,
      message: `Role '${role.name}' still has ${assigned} assigned user(s).`,
    });
  }

  ROLES.splice(index, 1);
  delete PERMISSION_GRANTS[grantKey('Role', role.id)];
}

/**
 * 用户从其角色继承而来的授予。拒绝优先：任一角色拒绝即为拒绝。
 *
 * 角色主体没有上游主体，因此只有 User 会有继承值。
 */
function inheritedEffectsOf(userId: string): Map<string, 'Granted' | 'Prohibited'> {
  const inherited = new Map<string, 'Granted' | 'Prohibited'>();
  const user = USERS.find((candidate) => candidate.id === userId);
  if (!user) {
    return inherited;
  }

  for (const roleName of user.roles) {
    const role = ROLES.find((candidate) => candidate.name === roleName);
    if (!role) {
      continue;
    }

    for (const grant of PERMISSION_GRANTS[grantKey('Role', role.id)]?.grants ?? []) {
      if (grant.effect === 'Prohibited' || !inherited.has(grant.name)) {
        inherited.set(grant.name, grant.effect);
      }
    }
  }

  // 与运行时一致：被拒绝权限的子孙同样视为拒绝。
  for (const [name, effect] of [...inherited]) {
    if (effect !== 'Prohibited') {
      continue;
    }
    for (const descendant of PERMISSION_DESCENDANTS[name] ?? []) {
      inherited.set(descendant, 'Prohibited');
    }
  }

  return inherited;
}

/** 直授中是否有祖先被显式拒绝。 */
function prohibitedByAncestor(
  name: string,
  direct: Map<string, 'Granted' | 'Prohibited'>,
): boolean {
  return Object.entries(PERMISSION_DESCENDANTS).some(
    ([ancestor, descendants]) =>
      direct.get(ancestor) === 'Prohibited' && descendants.includes(name),
  );
}

function buildGrantsResponse(providerName: string, providerKey: string) {
  const entry = PERMISSION_GRANTS[grantKey(providerName, providerKey)] ?? {
    revision: 0,
    grants: [],
  };
  const direct = new Map(entry.grants.map((grant) => [grant.name, grant.effect]));
  const inherited =
    providerName === 'User'
      ? inheritedEffectsOf(providerKey)
      : new Map<string, 'Granted' | 'Prohibited'>();

  return {
    providerName,
    providerKey,
    revision: entry.revision,
    grants: ALL_PERMISSIONS.map((name) => {
      const directEffect = direct.get(name) ?? null;
      const inheritedEffect = inherited.get(name) ?? null;

      return {
        name,
        direct: directEffect,
        inherited: inheritedEffect,
        // 拒绝优先：任一侧拒绝（含被祖先拒绝而传播下来的）即不生效。
        effective:
          directEffect !== 'Prohibited' &&
          inheritedEffect !== 'Prohibited' &&
          !prohibitedByAncestor(name, direct) &&
          (directEffect === 'Granted' || inheritedEffect === 'Granted'),
      };
    }),
  };
}

export function getRoleGrants(roleId: string) {
  requirePermission('App.Roles.ManagePermissions');
  return buildGrantsResponse('Role', roleId);
}

export function replaceRoleGrants(roleId: string, body: Record<string, unknown>) {
  requirePermission('App.Roles.ManagePermissions');

  const key = grantKey('Role', roleId);
  const entry = PERMISSION_GRANTS[key] ?? { revision: 0, grants: [] };
  const expected = Number(body['expectedRevision'] ?? 0);

  // 复刻乐观并发：版本不匹配返回 409，而不是静默覆盖对方的修改。
  if (expected !== entry.revision) {
    throw new MockException(409, {
      code: 40900,
      message: 'The permissions were changed by someone else. Reload and try again.',
    });
  }

  const grants = (body['grants'] as { name: string; effect: 'Granted' | 'Prohibited' }[]) ?? [];
  const unknown = grants.filter((grant) => !ALL_PERMISSIONS.includes(grant.name));
  if (unknown.length > 0) {
    throw new MockException(400, {
      code: 40000,
      message: `Permission '${unknown[0].name}' is not defined or is disabled.`,
    });
  }

  PERMISSION_GRANTS[key] = { revision: entry.revision + 1, grants: [...grants] };
  return buildGrantsResponse('Role', roleId);
}

export function getUserGrants(userId: string) {
  requirePermission('App.Users.ManagePermissions');
  return buildGrantsResponse('User', userId);
}

export function replaceUserGrants(userId: string, body: Record<string, unknown>) {
  requirePermission('App.Users.ManagePermissions');

  const key = grantKey('User', userId);
  const entry = PERMISSION_GRANTS[key] ?? { revision: 0, grants: [] };
  const expected = Number(body['expectedRevision'] ?? 0);
  if (expected !== entry.revision) {
    throw new MockException(409, {
      code: 40900,
      message: 'The permissions were changed by someone else. Reload and try again.',
    });
  }

  const grants = (body['grants'] as { name: string; effect: 'Granted' | 'Prohibited' }[]) ?? [];
  PERMISSION_GRANTS[key] = { revision: entry.revision + 1, grants: [...grants] };
  return buildGrantsResponse('User', userId);
}

export function getUserRoles(userId: string) {
  requirePermission('App.Users.ManageRoles');

  const user = USERS.find((candidate) => candidate.id === userId);
  if (!user) {
    throw new MockException(404, { code: 40400, message: 'User does not exist' });
  }

  return ROLES.filter((role) => user.roles.includes(role.name)).map((role) => ({
    id: role.id,
    name: role.name,
    displayName: role.displayName,
  }));
}

export function replaceUserRoles(userId: string, body: Record<string, unknown>) {
  requirePermission('App.Users.ManageRoles');

  const user = USERS.find((candidate) => candidate.id === userId);
  if (!user) {
    throw new MockException(404, { code: 40400, message: 'User does not exist' });
  }

  const roleIds = (body['roleIds'] as string[]) ?? [];
  user.roles = ROLES.filter((role) => roleIds.includes(role.id)).map((role) => role.name);

  return getUserRoles(userId);
}

export const AUTHORIZATION_API = {
  'GET /api/v1/permissions/current': () => getCurrentPermissions(),
  'GET /api/v1/permissions/definitions': () => getPermissionDefinitions(),
  'GET /api/v1/permissions/grants/roles/:roleId': (req: MockRequest) =>
    getRoleGrants(req.params.roleId),
  'PUT /api/v1/permissions/grants/roles/:roleId': (req: MockRequest) =>
    replaceRoleGrants(req.params.roleId, req.body),
  'GET /api/v1/permissions/grants/users/:userId': (req: MockRequest) =>
    getUserGrants(req.params.userId),
  'PUT /api/v1/permissions/grants/users/:userId': (req: MockRequest) =>
    replaceUserGrants(req.params.userId, req.body),
  'GET /api/v1/roles': (req: MockRequest) => getRoles(req.queryParams),
  'GET /api/v1/roles/options': () => getRoleOptions(),
  'POST /api/v1/roles': (req: MockRequest) => createRole(req.body),
  'PUT /api/v1/roles/:id': (req: MockRequest) => updateRole(req.params.id, req.body),
  'DELETE /api/v1/roles/:id': (req: MockRequest) => deleteRole(req.params.id),
  'GET /api/v1/users/:id/roles': (req: MockRequest) => getUserRoles(req.params.id),
  'PUT /api/v1/users/:id/roles': (req: MockRequest) => replaceUserRoles(req.params.id, req.body),
};
