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
  username: string;
  displayName?: string;
  isActive: boolean;
  creationTime: string;
  roles: RoleBriefDto[];
}
//#endif
