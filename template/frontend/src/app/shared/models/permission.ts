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
    managePermissions: 'App.Users.ManagePermissions',
  },
  roles: {
    default: 'App.Roles',
    create: 'App.Roles.Create',
    update: 'App.Roles.Update',
    delete: 'App.Roles.Delete',
    managePermissions: 'App.Roles.ManagePermissions',
  },
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

/** 授予效果。未出现在授予集合中即为"继承 / 未设置"。 */
export type PermissionGrantEffect = 'Granted' | 'Prohibited';

/** 权限编辑器中单个权限的三态选择值。 */
export type PermissionGrantState = 'Inherit' | PermissionGrantEffect;

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

export interface PermissionGrantStateDto {
  name: string;
  /** 该主体自身的直接授予；未设置时为 null。 */
  direct: PermissionGrantEffect | null;
  /** 从角色继承而来的授予（仅用户主体有值）。 */
  inherited: PermissionGrantEffect | null;
  /** 综合直接授予与继承后的最终结果。 */
  effective: boolean;
}

export interface PermissionGrantsOutputDto {
  providerName: string;
  providerKey: string;
  /** 乐观并发版本，保存时原样回传。 */
  revision: number;
  grants: PermissionGrantStateDto[];
}

export interface PermissionGrantInputDto {
  name: string;
  effect: PermissionGrantEffect;
}

export interface ReplacePermissionGrantsInputDto {
  expectedRevision: number;
  grants: PermissionGrantInputDto[];
}
