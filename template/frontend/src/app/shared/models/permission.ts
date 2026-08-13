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
  //#if (IncludeTenancy)
  tenants: {
    default: 'App.Tenants',
    create: 'App.Tenants.Create',
    update: 'App.Tenants.Update',
    delete: 'App.Tenants.Delete',
  },
  //#endif
  //#if (IncludeOpenIddict)
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
} as const;

/** 当前用户的有效权限。 */
export interface CurrentPermissionsOutputDto {
  permissions: string[];
  isSuperAdmin: boolean;
  /** 授权版本，变化即表示本地缓存已过期。 */
  revision: string;
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
  revision: number;
  grants: PermissionGrantStateDto[];
}

export interface ReplacePermissionGrantsInputDto {
  expectedRevision: number;
  /** 目标权限名集合，未出现的权限视为撤销。 */
  permissionNames: string[];
}
