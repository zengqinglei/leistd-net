//#if (TenantWorkspace)
export interface WorkspaceOutputDto {
  userId: string;
  tenantId: string;
  name: string;
  displayName?: string;
}
//#endif
