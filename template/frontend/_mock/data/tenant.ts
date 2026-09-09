import { TenantConnectionOutputDto } from '../../src/app/shared/dtos/tenant-connection.dto';
import {
  TenantDatabaseMode,
  TenantLookupOutputDto,
  TenantOutputDto,
} from '../../src/app/shared/dtos/tenant.dto';

export interface MockTenant {
  id: string;
  name: string;
  displayName?: string;
  description?: string;
  isActive: boolean;
  creationTime: string;
  /** 详情弹窗的「数据库放置」段要读它。 */
  databaseMode: TenantDatabaseMode;
  /** 专属库模式下的密钥**引用名**；接口从不返回连接串。 */
  runtimeSecretReference?: string;
  migrationSecretReference?: string;
  /** 连接配置版本，与 TenantRecord.Version 同源。 */
  connectionVersion: number;
}

// 一启用一停用：覆盖登录页「租户已停用」与登录 403 的演示路径。
// 数据库模式也一共享一专属：详情弹窗只在专属库模式下显示密钥引用名，
// 两个租户都是共享库时那条分支在 Mock 下永远走不到。
export const TENANTS: MockTenant[] = [
  {
    id: 'tenant_acme',
    name: 'acme',
    displayName: 'Acme Corp',
    description: '示例租户：共享库',
    isActive: true,
    creationTime: '2025-03-01T00:00:00Z',
    databaseMode: 'sharedDatabase',
    connectionVersion: 1,
  },
  {
    id: 'tenant_globex',
    name: 'globex',
    displayName: 'Globex Inc',
    isActive: false,
    creationTime: '2025-04-15T00:00:00Z',
    databaseMode: 'dedicatedDatabase',
    runtimeSecretReference: 'TenantSecrets__globex__Runtime',
    migrationSecretReference: 'TenantSecrets__globex__Migration',
    connectionVersion: 3,
  },
];

export function toTenantOutput(tenant: MockTenant): TenantOutputDto {
  return {
    id: tenant.id,
    name: tenant.name,
    displayName: tenant.displayName,
    description: tenant.description,
    isActive: tenant.isActive,
    creationTime: tenant.creationTime,
  };
}

/** 连接配置投影：只给密钥**引用名**，任何情况下都不返回连接串。 */
export function toTenantConnection(tenant: MockTenant): TenantConnectionOutputDto {
  return {
    tenantId: tenant.id,
    databaseMode: tenant.databaseMode,
    runtimeSecretReference: tenant.runtimeSecretReference,
    migrationSecretReference: tenant.migrationSecretReference,
    version: tenant.connectionVersion,
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
