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

/** 连接名的合法形态，与后端 `TenantConnectionConfiguration.NamePattern` 同源。 */
const CONNECTION_NAME_PATTERN = /^[a-z0-9-]{1,64}$/;

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

function findTenantOrThrow(tenantId: string): MockTenant {
  const tenant = TENANTS.find((t) => t.id === tenantId);
  if (!tenant) {
    throw new MockException(404, { code: 'Error:NotFound', message: 'Tenant not found' });
  }
  return tenant;
}

/** 连接名大小写不敏感：后端归一化为小写后存取，Mock 同样先归一化再匹配。 */
function normalizeConnectionName(name: string): string {
  return decodeURIComponent(name).trim().toLowerCase();
}

/**
 * 列出该租户已登记的连接。空数组即该租户不单独分库。
 *
 * 真实后端要求 App.Tenants.Update，Mock 不做权限，仅复刻投影形状。
 */
export function getTenantConnections(tenantId: string) {
  const tenant = findTenantOrThrow(tenantId);
  return tenant.connections.map((connection) => toTenantConnection(tenant, connection));
}

/**
 * 登记或更新一条连接。
 *
 * `expectedVersion` 必须显式给出（首次登记传 `null`）：后端把"缺这个字段"当 400 处理，
 * 而不是按后写者胜出。这条不复刻的话，Mock 下"忘了带版本"会一路成功，换到真实后端才炸。
 */
export function setTenantConnection(tenantId: string, rawName: string, value: any) {
  const tenant = findTenantOrThrow(tenantId);
  const name = normalizeConnectionName(rawName);
  if (!CONNECTION_NAME_PATTERN.test(name)) {
    throw new MockException(400, {
      code: 'Error:BadRequest',
      message: 'Connection name must match ^[a-z0-9-]{1,64}$.',
    });
  }

  if (!value || !('expectedVersion' in value)) {
    throw new MockException(400, {
      code: 'Error:BadRequest',
      message: 'Expected version is required; pass null for the first registration.',
    });
  }

  // 想让某个名字回到"用服务自己的库"，删掉这一条，而不是提交空连接串。
  if (!String(value.connectionString ?? '').trim()) {
    throw new MockException(400, {
      code: 'Error:BadRequest',
      message: 'Connection string is required.',
    });
  }

  const expectedVersion = value.expectedVersion === null ? null : Number(value.expectedVersion);
  const existing = tenant.connections.find((connection) => connection.name === name);

  // 首次登记预期这一条尚不存在，改已有的那条则必须带上读到的版本；两种落空都是 409。
  if (existing ? expectedVersion !== existing.version : expectedVersion !== null) {
    throw new MockException(409, {
      code: 'Error:Conflict',
      message: 'Tenant connection version mismatch',
    });
  }

  // 连接串不保存，只递增版本：真实后端加密存储且从不返回
  const connection = existing ?? { name, version: 0 };
  connection.version += 1;
  if (!existing) {
    tenant.connections.push(connection);
  }

  return toTenantConnection(tenant, connection);
}

/** 删除一条连接：这个名字之后回落到服务自己配置的数据库。 */
export function removeTenantConnection(
  tenantId: string,
  rawName: string,
  expectedVersion: string | undefined,
) {
  const tenant = findTenantOrThrow(tenantId);
  const name = normalizeConnectionName(rawName);
  const index = tenant.connections.findIndex((connection) => connection.name === name);
  if (index < 0) {
    throw new MockException(404, {
      code: 'Error:NotFound',
      message: 'Tenant connection not found',
    });
  }

  if (Number(expectedVersion) !== tenant.connections[index].version) {
    throw new MockException(409, {
      code: 'Error:Conflict',
      message: 'Tenant connection version mismatch',
    });
  }

  tenant.connections.splice(index, 1);
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
    // 分库在建租户这一步定案：连接与租户一起落库（后端是同一个事务），之后不能再补。
    // 名字与重名的校验复刻后端，否则 Mock 下能过、换真实后端才 400。
    connections: normalizeCreateConnections(value.connections),
  } satisfies MockTenant;
  TENANTS.push(newTenant);
  return toTenantOutput(newTenant);
}

// 复刻后端创建时对连接数组的整批校验：名字归一化后按模式校验、连接串非空、名字不得重复。
function normalizeCreateConnections(raw: unknown) {
  const connections: { name: string; connectionString: string; version: number }[] = [];
  for (const entry of Array.isArray(raw) ? raw : []) {
    const name = normalizeConnectionName(String((entry as any)?.name ?? ''));
    if (!CONNECTION_NAME_PATTERN.test(name)) {
      throw new MockException(400, {
        code: 'TenantConnection:NameInvalid',
        message: 'Connection name must match ^[a-z0-9-]{1,64}$.',
      });
    }
    if (connections.some((connection) => connection.name === name)) {
      throw new MockException(400, {
        code: 'TenantConnection:NameDuplicated',
        message: `The connection name '${name}' was given more than once.`,
      });
    }
    const connectionString = String((entry as any)?.connectionString ?? '').trim();
    if (!connectionString) {
      throw new MockException(400, {
        code: 'TenantConnection:ConnectionStringInvalid',
        message: 'Connection string is required.',
      });
    }
    connections.push({ name, connectionString, version: 1 });
  }
  return connections;
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
    getTenantConnections(req.params.tenantId),
  'PUT /api/v1/tenant-connections/:tenantId/:name': (req: MockRequest) =>
    setTenantConnection(req.params.tenantId, req.params.name, req.body),
  'DELETE /api/v1/tenant-connections/:tenantId/:name': (req: MockRequest) =>
    removeTenantConnection(
      req.params.tenantId,
      req.params.name,
      getQueryValue(req.queryParams['expectedVersion']),
    ),
};
