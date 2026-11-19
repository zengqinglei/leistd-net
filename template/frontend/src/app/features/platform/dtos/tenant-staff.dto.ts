//#if (TenantWorkspace)
import { RoleBriefDto } from './role.dto';

export interface TenantStaffCandidateOutputDto {
  id: string;
  username: string;
  displayName?: string;
}

export interface TenantStaffOutputDto {
  homeUserId: string;
  memberId: string;
  /** 宿主账号的用户名。 */
  username: string;
  /** 租户内关联账号的真实用户名：首次授予为 `platform.<username>`，宿主改名后重新授予仍沿用原值。 */
  linkedUsername: string;
  displayName?: string;
  /** 关联账号在租户内的启用状态。 */
  isActive: boolean;
  creationTime: string;
  roles: RoleBriefDto[];
}
//#endif
