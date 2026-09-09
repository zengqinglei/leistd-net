import {
  createTenant,
  getTenantByHost,
  getTenantById,
  getTenantConnection,
  TENANT_API,
  updateTenant,
} from './tenant';
import { MockTenant, TENANTS } from '../data/tenant';

/**
 * Mock 与真实后端的契约对齐。
 *
 * Mock 是模板的一个交付面（`useMock` 模式下整套界面都跑在它上面），真实后端的 E2E
 * 替代不了它：契约加了字段而 Mock 没跟上时，Mock 模式下的表现是"填了保存、值没了"，
 * 而所有针对真实后端的测试全绿。
 */
describe('租户 Mock', () => {
  let snapshot: MockTenant[];

  beforeEach(() => {
    snapshot = TENANTS.map((tenant) => ({ ...tenant }));
  });

  afterEach(() => {
    TENANTS.length = 0;
    TENANTS.push(...snapshot);
  });

  function create(description?: string) {
    return createTenant({
      name: `probe-${Math.random().toString(36).slice(2, 8)}`,
      displayName: 'Probe Inc.',
      description,
      adminEmail: 'probe@example.test',
      adminPassword: 'MockTenant!Adm1n',
      databaseMode: 'sharedDatabase',
    });
  }

  it('创建时带上的描述会落地并在读取时回显', () => {
    const created = create('华东区自营资金账户');

    expect(created.description).toBe('华东区自营资金账户');
    expect(getTenantById(created.id).description).toBe('华东区自营资金账户');
  });

  it('更新可以改描述', () => {
    const created = create('原始描述');

    const updated = updateTenant(created.id, { name: created.name, description: '改过的描述' });

    expect(updated.description).toBe('改过的描述');
  });

  // 与后端一致：PUT 是整体覆盖，省略描述等于清空，没有"不传即保留原值"这一档。
  // 两者不一致时，Mock 模式下会看到"清空没生效"，真实后端下却生效。
  it('更新时省略描述等于清空', () => {
    const created = create('原始描述');

    expect(updateTenant(created.id, { name: created.name }).description).toBeUndefined();
    expect(getTenantById(created.id).description).toBeUndefined();
  });

  it('空白描述按清空处理', () => {
    const created = create('原始描述');

    expect(
      updateTenant(created.id, { name: created.name, description: '   ' }).description,
    ).toBeUndefined();
  });

  // 路由顺序：by-host / by-name 必须排在 :id 之前，否则会被当成一个 id 走错分支。
  it('注册了按主机名探测与连接配置两条路由，且排在按 id 查询之前', () => {
    const routes = Object.keys(TENANT_API);

    expect(routes).toContain('GET /api/v1/tenants/by-host');
    expect(routes).toContain('GET /api/v1/tenant-connections/:tenantId');
    expect(routes.indexOf('GET /api/v1/tenants/by-host')).toBeLessThan(
      routes.indexOf('GET /api/v1/tenants/:id'),
    );
  });

  // 本机开发不是任何受管域，真实后端在同一条件下也回"域名不表态"——
  // 登录页据此保留记住的租户并允许手选。
  it('按主机名探测回"域名不表态"', () => {
    expect(getTenantByHost()).toEqual({ decision: 'undecided' });
  });

  it('连接配置只给密钥引用名，不给连接串', () => {
    const dedicated = TENANTS.find((tenant) => tenant.databaseMode === 'dedicatedDatabase');
    expect(dedicated).toBeTruthy();

    const connection = getTenantConnection(dedicated!.id);

    expect(connection.databaseMode).toBe('dedicatedDatabase');
    expect(connection.runtimeSecretReference).toBeTruthy();
    expect(JSON.stringify(connection)).not.toMatch(/Host=|Password=|User ID=/i);
  });
});
