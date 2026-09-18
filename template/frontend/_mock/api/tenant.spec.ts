import {
  createTenant,
  getTenantByHost,
  getTenantById,
  getTenantConnections,
  removeTenantConnection,
  setTenantConnection,
  TENANT_API,
  updateTenant,
} from './tenant';
import { MockException } from '../core/models';
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
    // 连接数组必须逐条复制：浅拷贝下 connections 仍是同一个数组，
    // 一个用例登记的连接会漏给下一个用例，断言"列表为空"就再也测不准了。
    snapshot = TENANTS.map((tenant) => ({
      ...tenant,
      connections: tenant.connections.map((connection) => ({ ...connection })),
    }));
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
    });
  }

  /** 断言抛出的是带指定状态码的 MockException——状态码就是契约的一部分。 */
  function expectStatus(action: () => unknown, status: number): void {
    expect(action).toThrowMatching(
      (error: unknown) => error instanceof MockException && error.status === status,
    );
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
  it('注册了按主机名探测与连接增删查三条路由，且探测排在按 id 查询之前', () => {
    const routes = Object.keys(TENANT_API);

    expect(routes).toContain('GET /api/v1/tenants/by-host');
    expect(routes).toContain('GET /api/v1/tenant-connections/:tenantId');
    expect(routes).toContain('PUT /api/v1/tenant-connections/:tenantId/:name');
    expect(routes).toContain('DELETE /api/v1/tenant-connections/:tenantId/:name');
    expect(routes.indexOf('GET /api/v1/tenants/by-host')).toBeLessThan(
      routes.indexOf('GET /api/v1/tenants/:id'),
    );
  });

  // 本机开发不是任何受管域，真实后端在同一条件下也回"域名不表态"——
  // 登录页据此保留记住的租户并允许手选。
  it('按主机名探测回"域名不表态"', () => {
    expect(getTenantByHost()).toEqual({ decision: 'undecided' });
  });

  // 新建的租户一条登记都没有，这一档就是"不单独分库，各服务用自己配置的库"。
  it('新建的租户连接列表为空', () => {
    expect(getTenantConnections(create().id)).toEqual([]);
  });

  it('登记一条后出现在列表里，版本从 1 开始', () => {
    const created = create();

    const connection = setTenantConnection(created.id, 'crm', {
      expectedVersion: null,
      connectionString: 'Host=db;Database=probe;Password=probe-secret',
    });

    expect(connection).toEqual({ tenantId: created.id, name: 'crm', version: 1 });
    expect(getTenantConnections(created.id)).toEqual([
      { tenantId: created.id, name: 'crm', version: 1 },
    ]);
  });

  // 名字归一化为小写：填 Crm 与 crm 命中同一条，不会变成看着两条、写进去一条。
  it('连接名按小写归一化', () => {
    const created = create();

    expect(
      setTenantConnection(created.id, 'Crm', {
        expectedVersion: null,
        connectionString: 'Host=db;Password=probe-secret',
      }).name,
    ).toBe('crm');

    // 同一个名字再首次登记一次，预期"这条不存在"已经落空
    expectStatus(
      () =>
        setTenantConnection(created.id, 'crm', {
          expectedVersion: null,
          connectionString: 'Host=db;Password=probe-secret',
        }),
      409,
    );
  });

  it('版本不匹配时拒绝更新', () => {
    const created = create();
    setTenantConnection(created.id, 'crm', {
      expectedVersion: null,
      connectionString: 'Host=db;Password=probe-secret',
    });

    expectStatus(
      () =>
        setTenantConnection(created.id, 'crm', {
          expectedVersion: 99,
          connectionString: 'Host=db;Password=probe-secret',
        }),
      409,
    );
    expect(getTenantConnections(created.id)[0].version).toBe(1);
  });

  it('删除后列表变空，版本不匹配时不删', () => {
    const created = create();
    const connection = setTenantConnection(created.id, 'crm', {
      expectedVersion: null,
      connectionString: 'Host=db;Password=probe-secret',
    });

    expectStatus(() => removeTenantConnection(created.id, 'crm', '99'), 409);
    expect(getTenantConnections(created.id).length).toBe(1);

    removeTenantConnection(created.id, 'crm', String(connection.version));

    expect(getTenantConnections(created.id)).toEqual([]);
  });

  // 连接串只写：Mock 不保存它，登记返回、列表与库里的数据都不能出现它
  it('连接串不落进 Mock 数据，也不出现在任何返回里', () => {
    const created = create();

    const connection = setTenantConnection(created.id, 'crm', {
      expectedVersion: null,
      connectionString: 'Host=db;Database=probe;Password=probe-secret',
    });

    const stored = TENANTS.find((tenant) => tenant.id === created.id);
    expect(JSON.stringify(connection)).not.toContain('probe-secret');
    expect(JSON.stringify(getTenantConnections(created.id))).not.toContain('probe-secret');
    expect(JSON.stringify(stored)).not.toContain('probe-secret');
  });

  // 建租户这一步不带连接：请求里夹带的连接字段一概不读，与后端入口 DTO 一致。
  it('创建租户时夹带的连接串既不落地也不变成一条连接', () => {
    const created = createTenant({
      name: `probe-${Math.random().toString(36).slice(2, 8)}`,
      adminEmail: 'probe@example.test',
      adminPassword: 'MockTenant!Adm1n',
      connectionString: 'Host=db;Database=probe;Password=probe-secret',
    });

    expect(getTenantConnections(created.id)).toEqual([]);
    expect(JSON.stringify(TENANTS.find((tenant) => tenant.id === created.id))).not.toContain(
      'probe-secret',
    );
    expect(JSON.stringify(created)).not.toContain('probe-secret');
  });
});
