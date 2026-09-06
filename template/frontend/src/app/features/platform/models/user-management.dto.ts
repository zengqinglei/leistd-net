import { RoleBriefDto } from './role.dto';
import { PagedRequestDto } from '../../../shared/models/paged-request.dto';

export interface UserManagementOutputDto {
  id: string;
  username: string;
  email: string;
  displayName?: string;
  avatar?: string;
  isActive: boolean;
  //#if (LocalIdentity)
  isEmailVerified: boolean;
  //#endif
  /** 已分配角色。Id 用于提交，name/displayName 只用于展示。 */
  roles?: RoleBriefDto[];
  isSuperAdmin: boolean;
  creationTime: string;
  //#if (LocalIdentity)
  lastLoginTime?: string;
  //#endif
}

export interface GetUsersInputDto extends PagedRequestDto {
  keyword?: string;
  isActive?: boolean;
  //#if (LocalIdentity)
  isEmailVerified?: boolean;
  //#endif
  roles?: string[];
}

export interface CreateUserInputDto {
  //#if (!LocalIdentity)
  subjectId: string;
  //#endif
  username: string;
  email: string;
  displayName?: string;
  avatar?: string;
  //#if (LocalIdentity)
  password: string;
  //#endif
  isActive: boolean;
  //#if (LocalIdentity)
  isEmailVerified: boolean;
  //#endif
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
/** 不含启用状态：改变账号可用性只有启用/禁用两个命令一个入口，保护规则也只写在那里。 */
export interface UpdateUserInputDto {
  email: string;
  displayName?: string;
  avatar?: string;
  //#if (LocalIdentity)
  isEmailVerified: boolean;
  //#endif
}
//#if (LocalIdentity)
export interface ResetUserPasswordInputDto {
  password: string;
}
//#endif
