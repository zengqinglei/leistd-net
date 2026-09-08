import { TenantLookupOutputDto, TenantOutputDto } from '../../src/app/shared/dtos/tenant.dto';

export interface MockTenant {
  id: string;
  name: string;
  displayName?: string;
  isActive: boolean;
  creationTime: string;
}

// 一启用一停用：覆盖登录页「租户已停用」与登录 403 的演示路径。
export const TENANTS: MockTenant[] = [
  {
    id: 'tenant_acme',
    name: 'acme',
    displayName: 'Acme Corp',
    isActive: true,
    creationTime: '2025-03-01T00:00:00Z',
  },
  {
    id: 'tenant_globex',
    name: 'globex',
    displayName: 'Globex Inc',
    isActive: false,
    creationTime: '2025-04-15T00:00:00Z',
  },
];

export function toTenantOutput(tenant: MockTenant): TenantOutputDto {
  return {
    id: tenant.id,
    name: tenant.name,
    displayName: tenant.displayName,
    isActive: tenant.isActive,
    creationTime: tenant.creationTime,
  };
}

export function toTenantLookup(tenant: MockTenant): TenantLookupOutputDto {
  return {
    id: tenant.id,
    name: tenant.name,
    displayName: tenant.displayName,
    isActive: tenant.isActive,
  };
}
