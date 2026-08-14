//#if (TenancyEnabled)
import { PagedRequestDto } from '../models/paged-request.dto';

export interface TenantOutputDto {
  id: string;
  /** 租户名称，唯一，作为稳定的业务标识（登录时按名称解析租户）。 */
  name: string;
  displayName?: string;
  /** 停用的租户其用户无法登录。 */
  isActive: boolean;
  creationTime: string;
}

/** 匿名按名称解析租户的返回体（登录页租户选择用）。 */
export interface TenantBriefOutputDto {
  id: string;
  name: string;
  displayName?: string;
  isActive: boolean;
}

export interface GetTenantsInputDto extends PagedRequestDto {
  keyword?: string;
}

export interface CreateTenantInputDto {
  name: string;
  displayName?: string;
  /** 租户初始管理员账号。 */
  adminEmail: string;
  adminPassword: string;
}

export interface UpdateTenantInputDto {
  name: string;
  displayName?: string;
}

export interface SetTenantActivationInputDto {
  isActive: boolean;
}
//#endif
