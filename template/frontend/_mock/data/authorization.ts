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
  PERMISSIONS.roles.default,
  PERMISSIONS.roles.create,
  PERMISSIONS.roles.update,
  PERMISSIONS.roles.delete,
  PERMISSIONS.roles.managePermissions,
  //#if (OpenIddictServer)
  PERMISSIONS.openApplications.default,
  PERMISSIONS.openApplications.create,
  PERMISSIONS.openApplications.update,
  PERMISSIONS.openApplications.delete,
  PERMISSIONS.openApplications.resetSecret,
  //#endif
  PERMISSIONS.permissions.default,
  PERMISSIONS.settings.default,
];

/**
 * 权限定义树，形状与 `GET /api/v1/permissions/definitions` 一致。
 *
 * Mock 只复刻端点形状，不复刻后端的决策引擎（多来源并集、写时祖先归一化）——
 * 在前端重写一遍那套规则必然与后端漂移，漂移的 Mock 比没有 Mock 更危险。
 *
 * 分组标识符写字面量而不引用 PERMISSIONS：分组不是权限，不参与路由 Guard 与按钮裁剪，
 * 放进那份契约常量会让人误以为可以拿它做鉴权判断。
 */
export const PERMISSION_DEFINITIONS = [
  {
    name: 'Group.Identity',
    displayName: 'Identity and access',
    permissions: [
      {
        name: PERMISSIONS.users.default,
        displayName: 'User management',
        parentName: undefined,
        children: [
          leaf(PERMISSIONS.users.create, 'Create', PERMISSIONS.users.default),
          leaf(PERMISSIONS.users.update, 'Edit', PERMISSIONS.users.default),
          leaf(PERMISSIONS.users.delete, 'Delete', PERMISSIONS.users.default),
          leaf(PERMISSIONS.users.manageRoles, 'Assign roles', PERMISSIONS.users.default),
        ],
      },
      {
        name: PERMISSIONS.roles.default,
        displayName: 'Role management',
        parentName: undefined,
        children: [
          leaf(PERMISSIONS.roles.create, 'Create', PERMISSIONS.roles.default),
          leaf(PERMISSIONS.roles.update, 'Edit', PERMISSIONS.roles.default),
          leaf(PERMISSIONS.roles.delete, 'Delete', PERMISSIONS.roles.default),
          leaf(
            PERMISSIONS.roles.managePermissions,
            'Configure permissions',
            PERMISSIONS.roles.default,
          ),
        ],
      },
      //#if (OpenIddictServer)
      {
        name: PERMISSIONS.openApplications.default,
        displayName: 'Developer applications',
        parentName: undefined,
        children: [
          leaf(PERMISSIONS.openApplications.create, 'Create', PERMISSIONS.openApplications.default),
          leaf(PERMISSIONS.openApplications.update, 'Edit', PERMISSIONS.openApplications.default),
          leaf(PERMISSIONS.openApplications.delete, 'Delete', PERMISSIONS.openApplications.default),
          leaf(
            PERMISSIONS.openApplications.resetSecret,
            'Reset client secret',
            PERMISSIONS.openApplications.default,
          ),
        ],
      },
      //#endif
    ],
  },
  {
    name: 'Group.System',
    displayName: 'System',
    permissions: [
      {
        name: PERMISSIONS.permissions.default,
        displayName: 'Permission catalog',
        parentName: undefined,
        children: [],
      },
      {
        name: PERMISSIONS.settings.default,
        displayName: 'Settings',
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
 * 用于演示"有效权限是各来源授予的并集"。
 */
export const PERMISSION_GRANTS: Record<string, { version: number; permissionNames: string[] }> = {
  'Role/role_admin': { version: 1, permissionNames: [...ALL_PERMISSIONS] },
  'Role/role_member': { version: 1, permissionNames: [PERMISSIONS.users.default] },
};

export function grantKey(providerName: string, providerKey: string): string {
  return `${providerName}/${providerKey}`;
}
