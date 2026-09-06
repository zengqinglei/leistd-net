# 多租户

多租户家族负责解析当前租户、校验租户状态、建立租户上下文，并为数据隔离和动态连接提供契约。

## 何时使用

| 场景 | 组合 |
| --- | --- |
| SaaS 服务按租户隔离数据 | `Core` + `AspNetCore` + `EntityFrameworkCore` |
| Resource 服务只信任已验证 token 中的租户 claim | `Core` + `AspNetCore`，关闭本地租户校验 |
| 后台任务需在指定租户下执行 | `ICurrentTenant.Change()`；带主体时用 `IAmbientContext.Begin()` 一次建立各维度（贡献者在 `AspNetCore` 包） |
| 服务间需传递租户 | 使用 `Leistd.ServiceClient`；本家族恢复并解析上下文 |
| 非多租户项目 | 不引用、不注册 |

`Core` 供 Domain/Application 引用，`AspNetCore` 供 Web 宿主引用，`EntityFrameworkCore` 供 Infrastructure 引用。

## 安装

```bash
dotnet add package Leistd.MultiTenancy.Core
dotnet add package Leistd.MultiTenancy.AspNetCore
dotnet add package Leistd.MultiTenancy.EntityFrameworkCore
```

## 注册

Identity 持有租户注册表，解析匿名请求并校验租户状态：

```csharp
builder.Services.AddMultiTenancy(builder.Configuration);
builder.Services.AddMultiTenancyEfCore<IdentityControlDbContext>();

var app = builder.Build();
app.UseAuthentication();
app.UseMultiTenancy();
app.UseAuthorization();
```

Resource 不复制租户注册表，只从已验证主体恢复租户：

```csharp
builder.Services.AddMultiTenancy(options =>
{
    builder.Configuration.GetSection("Leistd:MultiTenancy").Bind(options);
    options.ValidateResolvedTenant = false;
});
```

`ValidateResolvedTenant = false` 会同时停止查询 `ITenantStore` 并将解析链强制收窄为主体 claim。这防止未经校验的请求头、查询串或域名选择租户。两种角色的中间件顺序相同。

控制面 DbContext 映射租户表：

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ConfigureMultiTenancy();
}
```

`AddMultiTenancyEfCore<TDbContext>()` 注册租户与连接配置的 Store/Manager，均可通过 `TryAdd` 替换。它不注册业务实体的租户落值拦截器；该行为属于 DDD 基座的 `BaseDbContext`。

普通 `DbContext` 若需为 `TenantConnectionRecord` 记录创建审计，应显式启用创建审计：

```csharp
public IdentityControlDbContext(
    DbContextOptions<IdentityControlDbContext> options,
    IServiceProvider? serviceProvider)
    : base(options)
{
    ChangeTracker.EnableCreationAuditing(serviceProvider);
}
```

该调用只落创建审计，不落租户归属；修改审计仍由 `AuditSaveChangesInterceptor` 处理。

## 使用

### 隔离业务实体

实体通过 `IMultiTenant` 声明租户归属：

```csharp
public class Order : IMultiTenant
{
    public Guid Id { get; private set; }
    public Guid? TenantId { get; private set; }
}
```

`IMultiTenant` 本身不实施 EF Core 隔离。使用 [DDD 基座](../ddd-struct/ddd-struct.md) 时，`BaseDbContext` 会：

- 为查询添加 `TenantId == 当前租户` 过滤器，并与软删除过滤器叠加。
- 为新实体填充尚未赋值的 `TenantId`。
- 在启动时检查每个已映射的多租户实体是否存在租户过滤器。

不使用 DDD 基座时，宿主必须自行实现查询过滤和写入落值；只实现接口不会产生隔离。

`TenantId` 在实体进入跟踪时落定，而非在 `SaveChanges` 时落定。因此实体即使在工作单元完成前离开 `ICurrentTenant.Change()` 作用域，也不会被误归为宿主数据。显式赋值不会被覆盖。

### 切换租户上下文

```csharp
using (currentTenant.Change(tenantId))
{
    var orders = await db.Orders.ToListAsync();
}
```

`Change()` 支持嵌套，释放时恢复父上下文。两种跨越方式语义不同：

| 方式 | 可见数据 |
| --- | --- |
| `currentTenant.Change(null)` | 仅 `TenantId == null` 的宿主数据 |
| `IDataFilter.Disable<IMultiTenant>()` | 全部租户与宿主数据 |

两者都只应用于可审计的系统能力。

### 隔离数据库之外的标识

数据库里的隔离由查询过滤器负责，**缓存键、分布式锁键这类租户外部的标识要自己带租户段**——同一个逻辑名在两个租户下必须是两条记录：

```csharp
// 产出 "{租户 Id:N}:catalog:category:{id:N}"
var cacheKey = currentTenant.ScopeKey($"catalog:category:{id:N}");
```

拿到的键直接交给宿主选用的缓存、[分布式锁](./lock.md)或幂等存储——本组件不包装它们。

`ScopeKey` 产出 `{租户 Id:N}:{key}`，宿主视角为 `host:{key}`；它是 `Leistd.MultiTenancy.Extensions` 命名空间下的 `this ICurrentTenant` 扩展。

**不是所有标识都该调它**：用来判断「你是哪个租户」的数据本身（租户注册表、租户连接配置）在任何上下文下都指向同一份，加租户段反而会按调用时机分裂成多份。这类标识保持原样。

它刻意做成显式调用，而不是藏进某个缓存包装类型里自动拼——自动拼就必须再补一个「本次不要拼」的开关，调用方仍要逐处判断，却多了一层不透明。

### 解析与校验

`AddMultiTenancy` 在解析链为空时按以下顺序装配：

| 顺序 | 来源 | 契约 |
| --- | --- | --- |
| 1 | 已认证主体 | `tenant_id` claim 定案；无 claim 也定案为宿主 |
| 2 | 子域名 | 仅配置 `DomainFormat` 时启用；受管域内定案 |
| 3 | 请求头 | 默认 `X-Tenant-Id` |
| 4 | 查询串 | 默认 `tenant` |

主体排在首位，因此请求头与查询串无法改写已登录用户的租户。租户 claim 必须是单个非空 Guid；多个 claim 即使值相同也抛 `AmbiguousTenantClaimException`。非法 claim 失败关闭，不回退到宿主。

未解析出租户表示宿主上下文。启用校验时，不存在或已删除的租户返回 404，已停用的租户返回 403。

### 配置子域名

```csharp
builder.Services.AddMultiTenancy(options =>
    options.DomainFormat = "{0}.example.com");
```

`DomainFormat` 必须包含一个 `{0}`、使用纯 ASCII 主机名、不含 scheme/路径/端口，且占位符之后必须有固定基础域。错误配置在启动时抛 `OptionsValidationException`。

受管域内的基础域、多级子域或不匹配前后缀的主机名定案为宿主，不再读请求头。受管域外的内部服务名继续交给后续贡献者。

反向代理使用 `X-Forwarded-Host` 时，必须通过 `KnownProxies`/`KnownIPNetworks` 限定可信代理。同时需配置授权服务器 issuer、资源服务验签、出站链接与 DNS；框架只负责入站解析。

### 管理租户

```csharp
var tenant = await tenantManager.CreateAsync(
    name,
    displayName,
    isActive: false);

using (currentTenant.Change(tenant.Id, tenant.Name))
{
    await SeedAsync(tenant);
}

await tenantManager.SetActiveAsync(tenant.Id, true);
```

`isActive` 没有默认值。需要初始化角色、管理员或业务数据时，先以停用状态创建，初始化完成后再启用。

租户名在未删除行内唯一；名称冲突抛 `DuplicateTenantNameException`。租户修改与连接配置共享 `TenantRecord.Version`，并发冲突抛 `TenantConcurrencyConflictException`。

### 配置租户连接

| 模式 | 连接目标 | 隔离 |
| --- | --- | --- |
| `SharedDatabase` | 宿主的默认连接 | 固定 schema + `TenantId` 行隔离 |
| `DedicatedDatabase` | 租户专属数据库 | 每个服务仍使用固定 schema + `TenantId` |

`TenantConnectionRecord` 只保存模式、Runtime/Migration Secret Reference 和版本，不保存明文连接字符串。`SharedDatabase` 不允许 Secret Reference；`DedicatedDatabase` 要求同时提供 DML 和 DDL 引用。

```csharp
await connectionManager.SetAsync(
    tenantId,
    TenantDatabaseMode.DedicatedDatabase,
    runtimeSecretReference: "vault://runtime/tenant-a",
    migrationSecretReference: "vault://migration/tenant-a",
    expectedVersion);
```

`expectedVersion: null` 表示预期配置不存在；更新时必须传入读到的版本。预期与实际不一致时抛 `TenantConnectionVersionConflictException`，不提供跳过并发检查的入口。

修改已有连接或轮换 Secret 前必须停用租户，否则抛 `TenantConnectionChangeRequiresInactiveTenantException`。标准流程是：

1. 停用租户。
2. 等待 `max(Access Token 有效期, 路由缓存 TTL)` 以排空旧路由。
3. 迁移并校验数据。
4. 更新连接配置。
5. 重新启用租户。

最终连接解析由 `Leistd.Data.Abstractions.IConnectionStringResolver` 完成。宿主实现应遵循：

1. 宿主级专属连接始终优先，不看当前租户。
2. 宿主上下文或 `SharedDatabase` 使用宿主配置。
3. `DedicatedDatabase` 解析当前租户的 Runtime Secret。
4. 未知租户、缺失配置或 Secret 无法解析时失败关闭，不回退到共享库。

固定在控制库的 DbContext 可声明专属连接名：

```csharp
[ConnectionStringName("IdentityControl")]
public sealed class IdentityControlDbContext : DbContext;
```

## 接口参考

| 类型 | 用途 |
| --- | --- |
| `Leistd.MultiTenancy.Abstractions.IMultiTenant` | 通过 `Guid? TenantId` 声明数据归属；`null` 表示宿主 |
| `Leistd.MultiTenancy.Abstractions.ICurrentTenant` | 读取或临时切换租户上下文 |
| `CurrentTenantKeyExtensions.ScopeKey(currentTenant, key)` | 把缓存键、锁键等租户外部标识限定到当前租户；`this ICurrentTenant` 扩展 |
| `Leistd.MultiTenancy.Stores.ITenantStore` | 按 Id 或归一化名称查找租户 |
| `Leistd.MultiTenancy.Stores.ITenantManager` | 创建、修改、启停、软删除和分页查询租户 |
| `ITenantConnectionConfigurationStore` | 读取租户连接配置，并为 DbMigrator 枚举目标 |
| `ITenantConnectionConfigurationManager` | 维护数据库模式、Secret Reference 和版本 |
| `Leistd.MultiTenancy.Abstractions.MultiTenancySides` | 表示权限属于 `Tenant`、`Host` 或 `Both` |

不持有注册表的测试或宿主可使用 `AddInMemoryTenantStore(configure)` 注册只读内存 Store。

宿主若必须绕过 Store 直接查询控制面表，必须从以下扩展起查：

| 扩展 | 结果 |
| --- | --- |
| `DbContext.UndeletedTenants()` | 未删除的 `TenantRecord` |
| `DbContext.ConnectionsOfUndeletedTenants()` | 未删除租户的连接配置 |

控制面上下文通常不带软删除或租户过滤器，直接 `Set<T>()` 可能让已删除租户继续解析连接。

## 配置项（`Leistd:MultiTenancy`）

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `HeaderName` | `X-Tenant-Id` | 租户请求头名 |
| `QueryStringParameterName` | `tenant` | 租户查询参数名 |
| `TenantClaimType` | `tenant_id` | 主体租户 claim 类型 |
| `ValidateResolvedTenant` | `true` | 是否查租户注册表校验存在且启用 |
| `DomainFormat` | `null` | 子域名解析格式，如 `{0}.example.com` |

## 注意事项

- 中间件顺序必须是 `UseAuthentication()` → `UseMultiTenancy()` → `UseAuthorization()`。
- 认证端必须向租户用户签发单一、有效的 `tenant_id` claim；漏写会将 Resource 请求当作宿主上下文。
- `IgnoreQueryFilters()` 与 raw SQL 会绕过租户隔离。合法的跨租户操作使用 `IDataFilter.Disable<IMultiTenant>()` 显式表达。
- 租户实体的唯一索引需分别覆盖 `TenantId IS NULL` 的宿主行和 `TenantId IS NOT NULL` 的租户行；单一 `(TenantId, X)` 索引无法限制多个 NULL。
- **承载租户业务数据**的外部标识（缓存 key、锁 key 等）必须带 tenant/host 作用域，用 `ScopeKey` 产出；**控制面标识**（租户注册表、租户连接配置这类「用来判断你是哪个租户」的数据）保持全局——加作用域反而会按调用时机分裂成多份。
- 超级管理员只可旁路功能权限，不可旁路租户数据隔离。
- 多服务系统的租户注册表应归 Identity 所有。Resource 在 token 有效期内信任经验证的 claim，不复制租户状态。
- `EfCoreTenantStore` 默认不缓存，使启停和删除在提交后立即生效。自行加缓存时必须显式承担撤销延迟。

## 相关

- [DDD 基座](../ddd-struct/ddd-struct.md)：查询过滤、写入落值与启动检查
- [审计](./auditing.md)：创建与修改审计
- [授权](./authorization.md)：权限的多租户侧别
- [服务调用](./service-client.md)：跨服务租户传递
