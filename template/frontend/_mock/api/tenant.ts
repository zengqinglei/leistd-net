//#if (TenancyEnabled)
import { PagedResultDto } from '../../src/app/shared/models/paged-result.dto';
import { MockException, MockRequest } from '../core/models';
import { TENANTS, toTenantBrief, toTenantOutput } from '../data/tenant';

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
    throw new MockException(404, { code: 40400, message: 'Tenant not found' });
  }
  return toTenantOutput(tenant);
}

/** 匿名按名称解析（登录页租户选择）：404 = 不存在；停用租户照常返回，由前端提示。 */
export function getTenantByName(name: string) {
  const normalized = decodeURIComponent(name).toLowerCase();
  const tenant = TENANTS.find((t) => t.name.toLowerCase() === normalized);
  if (!tenant) {
    throw new MockException(404, { code: 40400, message: 'Tenant not found' });
  }
  return toTenantBrief(tenant);
}

export function createTenant(value: any) {
  const name = String(value.name ?? '').trim();
  // 名称冲突复刻后端 409 形状。
  if (TENANTS.some((t) => t.name.toLowerCase() === name.toLowerCase())) {
    throw new MockException(409, { code: 40900, message: 'Tenant name already exists' });
  }

  const newTenant = {
    id: crypto.randomUUID(),
    name,
    displayName: value.displayName?.trim() || undefined,
    isActive: true,
    creationTime: new Date().toISOString(),
  };
  TENANTS.push(newTenant);
  return toTenantOutput(newTenant);
}

export function updateTenant(id: string, value: any) {
  const tenant = TENANTS.find((t) => t.id === id);
  if (!tenant) {
    throw new MockException(404, { code: 40400, message: 'Tenant not found' });
  }

  const name = String(value.name ?? '').trim();
  if (TENANTS.some((t) => t.id !== id && t.name.toLowerCase() === name.toLowerCase())) {
    throw new MockException(409, { code: 40900, message: 'Tenant name already exists' });
  }

  tenant.name = name;
  tenant.displayName = value.displayName?.trim() || undefined;
  return toTenantOutput(tenant);
}

export function setTenantActivation(id: string, value: any) {
  const tenant = TENANTS.find((t) => t.id === id);
  if (!tenant) {
    throw new MockException(404, { code: 40400, message: 'Tenant not found' });
  }
  tenant.isActive = value.isActive === true;
  return toTenantOutput(tenant);
}

export function deleteTenant(id: string) {
  const index = TENANTS.findIndex((t) => t.id === id);
  if (index < 0) {
    throw new MockException(404, { code: 40400, message: 'Tenant not found' });
  }
  TENANTS.splice(index, 1);
}

export const TENANT_API = {
  'GET /api/v1/tenants': (req: MockRequest) => getTenants(req.queryParams),
  'GET /api/v1/tenants/by-name/:name': (req: MockRequest) => getTenantByName(req.params.name),
  'GET /api/v1/tenants/:id': (req: MockRequest) => getTenantById(req.params.id),
  'POST /api/v1/tenants': (req: MockRequest) => createTenant(req.body),
  'PUT /api/v1/tenants/:id/activation': (req: MockRequest) =>
    setTenantActivation(req.params.id, req.body),
  'PUT /api/v1/tenants/:id': (req: MockRequest) => updateTenant(req.params.id, req.body),
  'DELETE /api/v1/tenants/:id': (req: MockRequest) => deleteTenant(req.params.id),
};
//#endif
