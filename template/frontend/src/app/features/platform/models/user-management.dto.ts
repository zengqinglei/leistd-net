import { RoleBriefDto } from './role.dto';
import { PagedRequestDto } from '../../../shared/models/paged-request.dto';

export interface UserManagementOutputDto {
  id: string;
  username: string;
  email: string;
  displayName?: string;
  avatar?: string;
  isActive: boolean;
  isEmailVerified: boolean;
  /** 已分配角色。Id 用于提交，name/displayName 只用于展示；未启用角色时后端不返回该字段。 */
  roles?: RoleBriefDto[];
  isSuperAdmin: boolean;
  creationTime: string;
  lastLoginTime?: string;
}

export interface GetUsersInputDto extends PagedRequestDto {
  keyword?: string;
  isActive?: boolean;
  isEmailVerified?: boolean;
  roles?: string[];
}

export interface CreateUserInputDto {
  username: string;
  email: string;
  displayName?: string;
  avatar?: string;
  password: string;
  isActive: boolean;
  isEmailVerified: boolean;
  /**
   * 初始角色 Id 集合。非空时后端额外要求 App.Users.ManageRoles，
   * 只持有创建权限的主体无法在创建时造出管理员。
   */
  roleIds: string[];
}

/** 替换用户角色。与资料更新是两个独立命令、两个独立权限。 */
export interface UpdateUserRolesInputDto {
  roleIds: string[];
}

/** 不含角色字段：角色分配走 PUT /api/v1/users/{id}/roles 并要求 App.Users.ManageRoles。 */
export interface UpdateUserInputDto {
  email: string;
  displayName?: string;
  avatar?: string;
  isActive: boolean;
  isEmailVerified: boolean;
}

export interface ResetUserPasswordInputDto {
  password: string;
}
