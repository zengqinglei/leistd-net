//#if (TenantWorkspace)
export interface MockStaffAccess {
  homeUserId: string;
  tenantId: string;
  roleIds: string[];
  creationTime: string;
}
export const STAFF_ACCESS: MockStaffAccess[] = [
  {
    homeUserId: 'user_admin',
    tenantId: 'tenant_acme',
    roleIds: ['role_admin'],
    creationTime: '2026-11-12T10:20:00Z',
  },
  {
    homeUserId: 'user_admin',
    tenantId: 'tenant_globex',
    roleIds: ['role_empty'],
    creationTime: '2026-11-13T10:20:00Z',
  },
];
//#endif
