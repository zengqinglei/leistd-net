import { TenantByHostOutputDto } from '../../src/app/shared/dtos/tenant.dto';
import { PagedResultDto } from '../../src/app/shared/models/paged-result.dto';
import { MockException, MockRequest } from '../core/models';
import { ensureAcceptablePassword } from '../data/password-policy';
import {
  MockTenant,
  TENANTS,
  toTenantConnection,
  toTenantLookup,
  toTenantOutput,
} from '../data/tenant';

function getQueryValue(value: unknown) {
  const normalized = Array.isArray(value) ? value[0] : value;
  return normalized === undefined || normalized === null || normalized === ''
    ? undefined
    : String(normalized);
}

export function getTenants(params: any): PagedResultDto<any> {
  let tenants = [...TENANTS];
  const offset = +(getQueryValue(params.offset) ?? 0);
  const limit = +(getQueryValue(params.limit) ?? 10);
  const keyword = getQueryValue(params.keyword)?.toLowerCase();

  if (keyword) {
    tenants = tenants.filter(
      (tenant) =>
        tenant.name.toLowerCase().includes(keyword) ||
        tenant.displayName?.toLowerCase().includes(keyword),
    );
  }

  tenants.sort((a, b) => a.name.localeCompare(b.name));

  return {
    totalCount: tenants.length,
    items: tenants.slice(offset, offset + limit).map(toTenantOutput),
  };
}

export function getTenantById(id: string) {
  const tenant = TENANTS.find((t) => t.id === id);
  if (!tenant) {
    throw new MockException(404, { code: 'Error:NotFound', message: 'Tenant not found' });
  }
  return toTenantOutput(tenant);
}

/** 匿名按名称解析（登录页租户选择）：404 = 不存在；停用租户照常返回，由前端提示。 */
export function getTenantByName(name: string) {
  const normalized = decodeURIComponent(name).toLowerCase();
  const tenant = TENANTS.find((t) => t.name.toLowerCase() === normalized);
  if (!tenant) {
    throw new MockException(404, { code: 'Error:NotFound', message: 'Tenant not found' });
  }
  return toTenantLookup(tenant);
}

/**
 * 按主机名探测租户（匿名）。
 *
 * Mock 里没有 `DomainFormat` 这类部署配置，`localhost` 也不是任何受管域，
 * 因此**恒定回"域名不表态"**——这正是真实后端在同一条件下的回答，
 * 登录页据此保留记住的租户并允许手选。
 */
export function getTenantByHost(): TenantByHostOutputDto {
  return { decision: 'undecided' };
}

/** 租户连接配置：真实后端要求 App.Tenants.Update，Mock 不做权限，仅复刻投影形状。 */
export function getTenantConnection(tenantId: string) {
  const tenant = TENANTS.find((t) => t.id === tenantId);
  if (!tenant) {
    throw new MockException(404, { code: 'Error:NotFound', message: 'Tenant not found' });
  }
  return toTenantConnection(tenant);
}

export function createTenant(value: any) {
  const name = String(value.name ?? '').trim();
  // 名称冲突复刻后端 409 形状。
  if (TENANTS.some((t) => t.name.toLowerCase() === name.toLowerCase())) {
    throw new MockException(409, { code: 'Error:Conflict', message: 'Tenant name already exists' });
  }
  // 复刻后端租户播种路径的口令策略（主体措辞与后端一致）。
  ensureAcceptablePassword(value.adminPassword, 'Tenant admin password');

  const newTenant = {
    id: crypto.randomUUID(),
    name,
    displayName: value.displayName?.trim() || undefined,
    description: value.description?.trim() || undefined,
    isActive: true,
    creationTime: new Date().toISOString(),
    databaseMode:
      value.databaseMode === 'dedicatedDatabase' ? 'dedicatedDatabase' : 'sharedDatabase',
    runtimeSecretReference: value.runtimeSecretReference || undefined,
    migrationSecretReference: value.migrationSecretReference || undefined,
    connectionVersion: 1,
  } satisfies MockTenant;
  TENANTS.push(newTenant);
  return toTenantOutput(newTenant);
}

export function updateTenant(id: string, value: any) {
  const tenant = TENANTS.find((t) => t.id === id);
  if (!tenant) {
    throw new MockException(404, { code: 'Error:NotFound', message: 'Tenant not found' });
  }

  const name = String(value.name ?? '').trim();
  if (TENANTS.some((t) => t.id !== id && t.name.toLowerCase() === name.toLowerCase())) {
    throw new MockException(409, { code: 'Error:Conflict', message: 'Tenant name already exists' });
  }

  tenant.name = name;
  tenant.displayName = value.displayName?.trim() || undefined;
  // 整体覆盖，与后端一致：省略与显式 null 都清空描述，没有"不传即保留"这一档。
  tenant.description = value.description?.trim() || undefined;
  return toTenantOutput(tenant);
}

export function setTenantActivation(id: string, value: any) {
  const tenant = TENANTS.find((t) => t.id === id);
  if (!tenant) {
    throw new MockException(404, { code: 'Error:NotFound', message: 'Tenant not found' });
  }
  tenant.isActive = value.isActive === true;
  return toTenantOutput(tenant);
}

export function deleteTenant(id: string) {
  const index = TENANTS.findIndex((t) => t.id === id);
  if (index < 0) {
    throw new MockException(404, { code: 'Error:NotFound', message: 'Tenant not found' });
  }
  TENANTS.splice(index, 1);
}

export const TENANT_API = {
  'GET /api/v1/tenants': (req: MockRequest) => getTenants(req.queryParams),
  // by-host / by-name 必须排在 :id 之前，否则会被当成一个 id 走到按 id 查询那条上
  'GET /api/v1/tenants/by-host': () => getTenantByHost(),
  'GET /api/v1/tenants/by-name/:name': (req: MockRequest) => getTenantByName(req.params.name),
  'GET /api/v1/tenants/:id': (req: MockRequest) => getTenantById(req.params.id),
  'POST /api/v1/tenants': (req: MockRequest) => createTenant(req.body),
  'PUT /api/v1/tenants/:id/activation': (req: MockRequest) =>
    setTenantActivation(req.params.id, req.body),
  'PUT /api/v1/tenants/:id': (req: MockRequest) => updateTenant(req.params.id, req.body),
  'DELETE /api/v1/tenants/:id': (req: MockRequest) => deleteTenant(req.params.id),
  'GET /api/v1/tenant-connections/:tenantId': (req: MockRequest) =>
    getTenantConnection(req.params.tenantId),
};
