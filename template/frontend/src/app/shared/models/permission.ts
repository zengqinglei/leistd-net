/**
 * 功能权限名常量。
 *
 * 必须与后端 `PermissionConstant` 逐字一致：路由 Guard、菜单、按钮裁剪和后端
 * `[Authorize(Policy = ...)]` 使用同一组值，任何一处漂移都会造成"能看见但调不通"。
 *
 * 前端裁剪只影响体验，不构成安全边界——服务端对每个请求仍会独立校验。
 */
export const PERMISSIONS = {
  users: {
    default: 'App.Users',
    create: 'App.Users.Create',
    update: 'App.Users.Update',
    delete: 'App.Users.Delete',
    manageRoles: 'App.Users.ManageRoles',
  },
  roles: {
    default: 'App.Roles',
    create: 'App.Roles.Create',
    update: 'App.Roles.Update',
    delete: 'App.Roles.Delete',
    managePermissions: 'App.Roles.ManagePermissions',
  },
  //#if (LocalIdentity)
  tenants: {
    default: 'App.Tenants',
    create: 'App.Tenants.Create',
    update: 'App.Tenants.Update',
    delete: 'App.Tenants.Delete',
  },
  //#endif
  //#if (OpenIddictServer)
  openApplications: {
    default: 'App.OpenApplications',
    create: 'App.OpenApplications.Create',
    update: 'App.OpenApplications.Update',
    delete: 'App.OpenApplications.Delete',
    resetSecret: 'App.OpenApplications.ResetSecret',
  },
  //#endif
  permissions: {
    default: 'App.Permissions',
  },
  settings: {
    default: 'App.Settings',
  },
} as const;

/**
 * 进入 `/platform` 所需的权限集：**任一**命中即可放行。
 *
 * 这是唯一来源，`/platform` 父路由的 `data.permissions` 与
 * `AuthorizationService.canAccessPlatform` 都必须引用它，不得各自硬编码。
 *
 * 两处曾是两份清单，在多租户场景下并不等价——路由含 `tenants.default`、
 * `canAccessPlatform` 不含，于是只有租户管理权限的平台运营账号：菜单里看不到入口、
 * 登录后被重定向到别处，但直接敲 `/platform/tenants` 却能进。
 * 表现为"后端通、前端不通"，最容易被误判成权限没生效。
 *
 * 新增平台模块时只改这里；`authorization-service.spec.ts` 里有一条断言锁住两处同源。
 */
export const PLATFORM_ENTRY_PERMISSIONS = [
  PERMISSIONS.users.default,
  PERMISSIONS.roles.default,
  //#if (LocalIdentity)
  PERMISSIONS.tenants.default,
  //#endif
  //#if (OpenIddictServer)
  PERMISSIONS.openApplications.default,
  //#endif
  PERMISSIONS.permissions.default,
] as const;

/** 当前用户的有效权限。 */
export interface CurrentPermissionsOutputDto {
  permissions: string[];
  isSuperAdmin: boolean;
  /** 有效权限的版本标记，变化即表示本地缓存已过期。 */
  versionToken: string;
}

export interface PermissionDefinitionOutputDto {
  name: string;
  displayName: string;
  parentName?: string;
  children: PermissionDefinitionOutputDto[];
}

export interface PermissionDefinitionGroupOutputDto {
  name: string;
  displayName: string;
  permissions: PermissionDefinitionOutputDto[];
}

/** 单个权限在某角色上的授予状态。授予是纯加法，只有已授予与未授予两种。 */
export interface PermissionGrantStateDto {
  name: string;
  granted: boolean;
}

export interface PermissionGrantsOutputDto {
  providerName: string;
  providerKey: string;
  /** 乐观并发版本，保存时原样回传。 */
  version: number;
  grants: PermissionGrantStateDto[];
}

export interface ReplacePermissionGrantsInputDto {
  expectedVersion: number;
  /** 目标权限名集合，未出现的权限视为撤销。 */
  permissionNames: string[];
}
