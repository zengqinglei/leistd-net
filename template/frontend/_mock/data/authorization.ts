import { PERMISSIONS } from '../../src/app/shared/models/permission';

export interface MockRole {
  id: string;
  name: string;
  displayName: string;
  description?: string;
  isStatic: boolean;
  isDefault: boolean;
  sort: number;
  creationTime: string;
  lastModificationTime?: string;
}

export const ROLES: MockRole[] = [
  {
    id: 'role_admin',
    name: 'Admin',
    displayName: 'Administrator',
    description: 'System administrator with all permissions',
    isStatic: true,
    isDefault: false,
    sort: 1,
    creationTime: '2025-01-01T00:00:00Z',
  },
  {
    id: 'role_member',
    name: 'Member',
    displayName: 'Member',
    description: 'Default role automatically assigned to new users',
    isStatic: true,
    isDefault: true,
    sort: 100,
    creationTime: '2025-01-01T00:00:00Z',
  },
];

/** 全部权限名，顺序与后端定义树一致。 */
export const ALL_PERMISSIONS: string[] = [
  PERMISSIONS.users.default,
  PERMISSIONS.users.create,
  PERMISSIONS.users.update,
  PERMISSIONS.users.delete,
  PERMISSIONS.users.manageRoles,
  PERMISSIONS.users.managePermissions,
  PERMISSIONS.roles.default,
  PERMISSIONS.roles.create,
  PERMISSIONS.roles.update,
  PERMISSIONS.roles.delete,
  PERMISSIONS.roles.managePermissions,
  PERMISSIONS.permissions.default,
];

/**
 * 权限定义树，形状与 `GET /api/v1/permissions/definitions` 一致。
 *
 * Mock 只复刻端点形状，不复刻后端的决策引擎（三态组合、写时祖先归一化）——
 * 在前端重写一遍那套规则必然与后端漂移，漂移的 Mock 比没有 Mock 更危险。
 */
export const PERMISSION_DEFINITIONS = [
  {
    name: 'App',
    displayName: 'Application Permissions',
    permissions: [
      {
        name: PERMISSIONS.users.default,
        displayName: 'User Management',
        parentName: undefined,
        children: [
          leaf(PERMISSIONS.users.create, 'Create User', PERMISSIONS.users.default),
          leaf(PERMISSIONS.users.update, 'Update User', PERMISSIONS.users.default),
          leaf(PERMISSIONS.users.delete, 'Delete User', PERMISSIONS.users.default),
          leaf(PERMISSIONS.users.manageRoles, 'Manage User Roles', PERMISSIONS.users.default),
          leaf(
            PERMISSIONS.users.managePermissions,
            'Manage User Permissions',
            PERMISSIONS.users.default,
          ),
        ],
      },
      {
        name: PERMISSIONS.roles.default,
        displayName: 'Role Management',
        parentName: undefined,
        children: [
          leaf(PERMISSIONS.roles.create, 'Create Role', PERMISSIONS.roles.default),
          leaf(PERMISSIONS.roles.update, 'Update Role', PERMISSIONS.roles.default),
          leaf(PERMISSIONS.roles.delete, 'Delete Role', PERMISSIONS.roles.default),
          leaf(
            PERMISSIONS.roles.managePermissions,
            'Manage Role Permissions',
            PERMISSIONS.roles.default,
          ),
        ],
      },
      {
        name: PERMISSIONS.permissions.default,
        displayName: 'View Permission Definitions',
        parentName: undefined,
        children: [],
      },
    ],
  },
];

function leaf(name: string, displayName: string, parentName: string) {
  return { name, displayName, parentName, children: [] as never[] };
}

/**
 * 每个主体的固定授予集合与版本。
 *
 * 演示账号各自代表一种典型场景：`role_admin` 全权，`role_member` 只读用户列表，
 * 直授与显式拒绝则用于演示"用户例外覆盖角色继承"。
 */
export const PERMISSION_GRANTS: Record<
  string,
  { revision: number; grants: { name: string; effect: 'Granted' | 'Prohibited' }[] }
> = {
  'Role/role_admin': {
    revision: 1,
    grants: ALL_PERMISSIONS.map((name) => ({ name, effect: 'Granted' as const })),
  },
  'Role/role_member': {
    revision: 1,
    grants: [{ name: PERMISSIONS.users.default, effect: 'Granted' }],
  },
};

export function grantKey(providerName: string, providerKey: string): string {
  return `${providerName}/${providerKey}`;
}
