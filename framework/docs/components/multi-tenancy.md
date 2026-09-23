# 多租户

多租户家族负责解析当前租户、校验租户状态、建立租户上下文，并为数据隔离和动态连接提供契约。

## 何时使用

| 场景 | 组合 |
| --- | --- |
| SaaS 服务按租户隔离数据 | `Core` + `AspNetCore` + `EntityFrameworkCore` |
| Resource 服务只信任已验证 token 中的租户 claim | `Core` + `AspNetCore`，关闭本地租户校验 |
| 后台任务需在指定租户下执行 | `ICurrentTenant.Change()`；带主体时用 `IAmbientContext.Begin()` 一次建立各维度（贡献者在 `AspNetCore` 包） |
| 服务间需传递租户 | 使用 `Leistd.ServiceClient`；本家族恢复并解析上下文 |
| 租户管理界面（查询、创建开通与补偿、启停、删除、连接登记） | `MapTenantManagement()`、`MapTenantConnections()`，宿主实现 `ITenantProvisioner` |
| 资源服务回源控制面取租户连接 | `Leistd.MultiTenancy.ServiceClient` 的 `AddRemoteTenantConnectionStore()` |
| 非多租户项目 | 不引用、不注册 |

`Core` 供 Domain/Application 引用，`AspNetCore` 供 Web 宿主引用，`EntityFrameworkCore` 供 Infrastructure 引用，`ServiceClient` 供回源控制面的资源服务引用。

## 安装

```bash
dotnet add package Leistd.MultiTenancy.Core
dotnet add package Leistd.MultiTenancy.AspNetCore
dotnet add package Leistd.MultiTenancy.EntityFrameworkCore
dotnet add package Leistd.MultiTenancy.ServiceClient   # 资源服务回源控制面
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

使用全局异常处理的 HTTP 宿主，须显式组合本组件的非默认状态；仅调用 `AddMultiTenancy` 不会登记异常映射：

```csharp
builder.Services.AddGlobalExceptionHandler(options =>
    MultiTenancyExceptionMappings.Configure(options));
```

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

**宿主全局的数据不要靠"关掉过滤器"表达，而要靠"不映射进租户上下文"表达。** 控制面表、第三方库自带的表（OpenIddict 这类）本就没有租户维度：把它们放进独立的、钉死宿主库的 `DbContext`，能力边界交给权限侧别（`MultiTenancySides.Host`）把守。反过来，一个上下文只要映射了 `IMultiTenant` 实体就必须带租户过滤器，启动期检查会拒绝其它形态。


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

### 解析与校验

`AddMultiTenancy` 在解析链为空时按以下顺序装配：

| 顺序 | 来源 | 契约 |
| --- | --- | --- |
| 1 | 已认证主体 | `tenant_id` claim 定案；无 claim 也定案为宿主 |
| 2 | 子域名 | 仅配置 `DomainFormat` 时启用；受管域内定案 |
| 3 | 请求头 | 默认 `X-Tenant-Id` |
| 4 | 查询串 | 默认 `tenant` |

主体排在首位，因此请求头与查询串无法改写已登录用户的租户。租户 claim 必须是单个非空 Guid；多个 claim 即使值相同也抛 `AmbiguousTenantClaimException`。非法 claim 失败关闭，不回退到宿主。

未解析出租户表示宿主上下文。启用校验时的响应按请求是否已认证分两档：

- **未认证请求**：不存在、已删除、已停用一律返回 404 `Tenant:NotFound`，三者的**状态码、错误码与响应形状一致**（`traceId` 这类请求级字段本来每次就不同，不在此列）。
  区分它们等于把"这个租户存不存在、是不是被停用了"告诉任何人——停用状态尤其敏感，
  它能让外部观察者看出某个租户被暂停了。
- **已认证请求**：不存在返回 404 `Tenant:NotFound`，已停用返回 403 `Tenant:NotActive`。
  已认证主体只探得到自己的租户，明确报错对运维有价值。

**这一档做到的是"不暴露启用状态与租户属性"，没有做到"不暴露存在性"。** 匿名调用方仍能判断
某个租户名是否存在：带上该名字打任意一个匿名端点，租户存在时走到业务逻辑（登录返回 401 InvalidCredentials、security-config 返回 200），不存在时在中间件就被挡成 404。
一次请求即可判定，不需要爆破。

要把存在性也藏起来，必须让每个匿名端点对不存在的租户返回**与存在时无法区分**的响应——
包括给不存在的租户编造一份注册策略和验证码。那会让登录页按虚构的策略渲染，
比泄露存在性更糟。因此这是**有意接受的残留**，不是待修缺陷：租户名本来就要由用户在登录页
输入，属于半公开信息；真正敏感的启用状态与租户属性（id、展示名）匿名侧一概拿不到。
批量探测由部署层限流处置。

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

管理界面直接用组件的用例与端点，宿主只实现开通与启用前置条件：

```csharp
builder.Services.AddScoped<ITenantProvisioner, TenantSeeder>();          // 在新租户里写初始数据，失败时清掉
builder.Services.AddScoped<ITenantActivationGuard, TenantHasUsersGuard>(); // 可选，可注册多个

app.MapGroup("/api/v1/tenants").MapTenantManagement<CreateTenantWithAdminInputDto>(options =>
{
    options.ReadPolicy = "App.Tenants";
    options.CreatePolicy = "App.Tenants.Create";
    options.UpdatePolicy = "App.Tenants.Update";
    options.DeletePolicy = "App.Tenants.Delete";
});
```

`ITenantManagementService.CreateAsync` 的顺序是硬的：先**整批校验** `Connections`（名字归一化后查重、连接串语法），再以停用态登记租户并在**同一控制面工作单元**里把**全部命名连接**一次登记（分库在开通之前定案，开通钩子第一次执行时看到的就是完整集合），然后在新租户上下文与新工作单元里调用 `ITenantProvisioner.ProvisionAsync`，最后启用。多服务部署一次给多条（`default`、`crm`……），不要建完租户再逐条补登记——那会留下"租户已启用、某条连接还没登记"的中间状态，那一刻用该名字的服务解析到的是回落库。`connections` 传空数组或显式 `null` 都表示不分库；数组里有 `null` 元素按输入校验返回 400，重名返回带 `TenantConnection:NameDuplicated` 的 400。任一步失败按"`PurgeAsync` → **逐条**删连接登记 → 删租户"补偿（删连接必须在删租户之前，每条独立捕获），每步独立作用域、独立令牌、各自记录失败；数据库原因经 `ITenantDatabaseErrorDescriber` 翻成带码的 400，不回显连接串；**默认实现不翻译**（错误码表是各数据库的方言），宿主注册自己的实现把本引擎的码映射到 `MultiTenancyErrorCodes` 的四个码（不可达、库不存在、未迁移、凭据被拒）。认不出来的错误返回 `null` 走统一 5xx——库重启、连接数耗尽、序列化失败不是调用方改连接串能解决的，报成 400 会让客户端既不重试也不告警。开通需要更多信息（如管理员邮箱）时派生 `CreateTenantInputDto`，端点按派生类型绑定请求体，开通器从 `TenantProvisioningContext.Input` 取回。成功的创建、更新、启停、删除发布 `TenantChangedEvent`（带显示名快照），宿主据此记审计。

租户名在未删除行内唯一；名称冲突抛 `DuplicateTenantNameException`。租户修改与连接配置共享 `TenantRecord.Version`，并发冲突抛 `TenantConcurrencyConflictException`。

`displayName`（≤128）与 `description`（≤256）都是可选的展示字段，只供管理界面呈现，不参与解析与唯一性判定。两者按传入值覆盖——传 `null` 即清空，因此调用方要区分"不改"与"清空"时必须自己先读一次当前值。

### 配置租户连接

**连接按 `(租户, 连接名)` 逐行登记，没有模式标志位**：连接名就是使用方 DbContext 的 `[ConnectionStringName]`，因此同一个租户可以在 identity、foundation、crm 各有一条。**行的存在本身就是判据**——一条都没有即该租户不单独分库，各服务使用自己配置的数据库。

**这个判据在租户创建时定案。**第一条登记决定的是"该租户的数据活在哪个库"，不只是路由：租户建出来时已经有种子（角色、权限授予、租户管理员），事后再补第一条连接会让解析改指一个空库，而那些数据不会跟着走——租户当场登不上，旧库里还留着一份带口令散列的孤儿账号。`SetAsync` 因此对**在用**租户拒绝这一类写入：

| 本次写入 | 在用租户 | 理由 |
| --- | --- | --- |
| 该租户一条登记都没有，登记第一条 | **拒绝** | 数据落点变了，既有数据不会跟着走 |
| 改或删已有的那一行 | **拒绝** | 热实例与冷实例会同时写入不同物理库 |
| 已是分库租户，补一个此前没有的名字 | 放行 | 那个服务此前就失败关闭，回落库里没有它的数据，补登是修复动作 |

要把一个已建租户改成分库，走停用 → 等待排空 → 自行迁移数据 → 登记 → 重新启用。

| 登记情况 | 连接目标 | 隔离 |
| --- | --- | --- |
| 一条都没有 | 该服务自己配置的连接 | 固定 schema + `TenantId` 行隔离 |
| 登记了这个名字 | 该行的连接串 | 每个服务仍使用固定 schema + `TenantId` |
| 只登记了默认名 | 默认名那一行（一租户一库，各服务不同 schema） | 同上 |

连接串运行时与迁移共用，因此它需要 DDL 权限。名字归一化为小写后须匹配 `TenantConnectionConfiguration.NamePattern`（`^[a-z0-9-]{1,64}$`），管理员填 `Crm` 与 `crm` 命中同一行；连接串最长 `TenantConnectionConfiguration.MaxConnectionStringLength`（2048）个字符。

```csharp
// 一租户一库、各服务不同 schema：只登记默认名这一条，所有服务都回落到它
await connectionManager.SetAsync(
    tenantId,
    name: "default",
    connectionString: "Host=db-tenant-a;Database=app;Username=app;Password=...",
    expectedVersion);

// 某个服务单独用另一个库：再给它登记一条
await connectionManager.SetAsync(tenantId, "crm", "Host=db-tenant-a-crm;...", expectedVersion: null);

// 改回"用服务自己的库"就是删掉这一行，而不是提交空连接串
await connectionManager.RemoveAsync(tenantId, "crm", expectedVersion);
```

连接串加密存储：写入时用 ASP.NET Core Data Protection（`IDataProtectionProvider` + 固定 purpose）加密，读取时解密。表 `TenantConnectionRecord` 的主键是 `(TenantId, Name)`，连接列只有密文 `ProtectedConnectionString`（非空，最长 4096）。数据库、备份和只读账号里看不到明文。`TenantConnectionConfiguration` 携带明文 `ConnectionString`，它与 `TenantConnectionLookupResult`、记录实体的 `ToString` 都不输出连接串；校验与解密失败的异常消息也不回显它。

本组件不管理密钥，宿主要做两件事：

1. 用 `AddDataProtection()` 配置**持久化、可共享**的密钥环（如存 Redis），并固定应用名。读写同一控制库的所有进程（API、迁移作业）必须使用同一密钥环；密钥丢失等于所有独立库连接串丢失，要纳入备份。
2. 在租户管理里填写连接串。

解不开（密钥环不同或密钥已丢失）时抛 `InvalidOperationException`，不回退共享库。

`expectedVersion: null` 表示预期配置不存在；更新时必须传入读到的版本。预期与实际不一致时抛 `TenantConnectionVersionConflictException`，不提供跳过并发检查的入口。

修改已有连接（包括轮换数据库密码）前必须停用租户，否则抛 `TenantConnectionChangeRequiresInactiveTenantException`。宿主还需在变更前排空仍使用旧路由的请求。

名字与连接串通常来自管理员输入：不合法时抛带码的 `BusinessException`（`TenantConnection:NameInvalid`、`TenantConnection:ConnectionStringInvalid`），由宿主映射为 400，也不回显连接串。

管理面与机器端点：

```csharp
app.MapGroup("/api/v1/tenant-connections").MapTenantConnections(options =>
{
    options.ManagePolicy = "App.Tenants.Update";
    // 只在签发机器令牌的部署里配置：为空则不映射对应的机器端点
    options.RuntimeReadPolicy = "TenantConnection.RuntimeRead";
    options.MigrationReadPolicy = "TenantConnection.MigrationRead";
});
```

**机器端点的部署边界**：`GET /migration?name=` 下发**全部租户的明文连接串**，只给一次性迁移作业的身份（`MigrationReadPolicy`），**永远不要**把这个 scope 给常驻 API——那等于让长期运行的进程随时能拉取所有租户的库凭据。常驻服务的逐库作业用 `GET /databases?name=`（`RuntimeReadPolicy`）：它只回指纹与租户归属，连接由各租户的正常解析链取得。

管理面（`GET /{tenantId}`、`PUT /{tenantId}/{name}`、`DELETE /{tenantId}/{name}?expectedVersion=`）只回名字与版本，列表经 `ITenantConnectionDirectory` 读控制库、不取密文。机器端点 `GET /runtime/{tenantId}?name=` 与 `GET /migration?name=` 按名字下发解密后的连接串，只能对机器主体开放。

登记、改写、删除成功后发布 `TenantConnectionChangedEvent(TenantId, TenantDisplayName, Name, Change, Version)`，宿主据此留痕。**不含连接串**（它是凭据，事件会进日志与订阅者的存储）。`Name` 是连接名（`default`、`crm`），`TenantDisplayName` 才是租户的显示名快照——要写"给谁改的"时用后者，拿 `Name` 顶替会在审计里显示成"为租户 default 登记了连接"。写入失败时不发事件。

### 解析租户连接

最终连接由 `Leistd.Data.Connections.IConnectionStringResolver` 决定。本家族按宿主形态提供两种实现，二选一注册：

| 宿主形态 | 注册 | 宿主提供 |
| --- | --- | --- |
| 自己持有控制库（租户注册表与连接配置就在本服务） | `AddLocalTenantConnectionResolution<TControlDbContext>(o => o.ControlPlaneConnectionStringName = "IdentityControl")`（EF 包） | 持久化的 Data Protection 密钥环 |
| 连接配置在另一个服务的控制库里 | `AddRemoteTenantConnectionResolution()`（Core 包）+ `AddRemoteTenantConnectionStore(serviceName, configuration)`（ServiceClient 包） | 机器身份（如 client credentials）、`TenantRouting:CacheLifetime` |

远端存储回源控制面经 `MapTenantConnections` 映射的机器端点，与端点共用 Core 里的线上 DTO；路由前缀默认 `/api/v1/tenant-connections`，经 `Leistd:ServiceClients:{serviceName}:RoutePrefix` 改。控制面下发**解密后**的连接串，远端服务不持有控制面的密钥环。鉴权与弹性策略加在返回的构建器上：

```csharp
builder.Services.AddRemoteTenantConnectionResolution();
builder.Services.AddRemoteTenantConnectionStore("Identity", builder.Configuration)
    .AddClientCredentials(builder.Configuration)
    .AddStandardResilienceHandler();
```

只有 404 且错误码为 `Tenant:NotFound` 的响应翻成"租户不存在"（`null`，由解析器失败关闭）；路由配错之类的其它 404 与一切其它错误原样抛出——吞成 `null` 会让"控制面不可达"表现成"这个租户不存在"。

两种解析的路由判定一致：

| 情形 | 结果 |
| --- | --- |
| 连接名是控制库连接名（仅本地解析） | 宿主配置，不参与租户路由 |
| 没有当前租户 | `ConnectionStrings:{名称}`，未配置则回落 `ConnectionStrings:Default` |
| 租户一条连接都没登记 | 同上——该租户不单独分库 |
| 命中这个名字的登记 | 该行的连接串（本地解析时为解密后的值） |
| 命中默认名的登记 | 默认名那一行的连接串 |
| 登记过连接、却缺这个名字且无默认名 | 抛 `InvalidOperationException`，**不回落本服务的库** |
| 租户不存在或已删除 | 抛 `TenantNotFoundException`（带 `Tenant:NotFound` 错误码） |
| 本地解析时解密失败 | 抛 `InvalidOperationException`，不回落 |
| 连接名不合法（不匹配 `NamePattern`，来自 `[ConnectionStringName]` 的编程错误） | 抛 `ArgumentException` |

失败关闭只有一处：倒数第四行。租户明明登记过连接（说明它是分库租户），却偏偏缺了这个服务的、也没有默认名可回落——这时静默连到本服务的公共库是事故，那个库里没有它的数据，它的写入会落进别人的库。宿主连接的 `?? ConnectionStrings:Default` 回落则是有意的：业务项目把上下文改名为 `Crm` 之后，部署里仍然只配 `ConnectionStrings__Default`，不用改部署。

- **本地解析**：控制库连接名解析为 `ConnectionStrings:{名称}`，未配置时回落 `Default`；租户配置从"未删除租户"入口查询，已软删或无配置的租户抛 `TenantNotFoundException`。控制库上下文直接注入、不经 `IDbContextProvider`。
- **远端解析**：结果按 `TenantRouting:CacheLifetime` 缓存；同租户并发请求合并为一次回源；响应的 `TenantId` 与请求不一致时抛 `InvalidOperationException`，校验在使用连接串和写缓存之前。
- **迁移目标**：两种注册都附带 `ITenantMigrationTargetProvider`，按迁移作业所属服务的连接名枚举。结果按物理库去重（同一连接串只出现一次，代表租户取标识最小者）。**它与运行时逐库作业不是同一份清单，也不是同一档权限**：迁移目标带明文连接串、要 DDL 身份（`MigrationReadPolicy`），运行时清单只回指纹与租户归属（`RuntimeReadPolicy`），见下一条；一条连接都没登记的租户不出现（它们跟着宿主自己的库迁移）；登记过却解析不出这个名字的租户会让作业整体停下（`InvalidOperationException`），不跳过——跳过的库会停在旧结构上，下一次发版才炸。本地迁移作业同样要解密，必须与 API 共享密钥环。
- **运行时逐库处理**：`AddMultiTenancyCore()` 注册 `ITenantDatabaseEnumerator`；没有注册租户连接解析时清单只有宿主库，注册了本地或远端解析时再列出独立库。它给归档、清理、扫描这类后台作业列出物理库——宿主库在第一个，其后是独立库，结果里没有连接串。**停用租户的库算不算由调用方用 `activeOnly` 显式决定**，框架不替它选：保留期作业要连停用租户一起处理（合规义务不随停用消失），刷新进程内状态一类作业则不该去连可能已下线的库。无租户上下文里的 `IgnoreQueryFilters()` 只放开同一个库里的租户，独立库要逐个进去：先 `ICurrentTenant.Change(database.TenantId)`，再 `BeginAsync(requiresNew: true)`，然后经 `IDbContextProvider` 取上下文；直接注入的 `DbContext` 不跟随租户路由。每个库单独捕获异常，一个库失败不影响其余——`ITenantDatabaseRunner.ForEachDatabaseAsync` 封装了切租户与逐库隔离，回调里按需开工作单元。登记成与宿主完全相同的连接串时，该项会并进宿主库，不会让同一个物理库出现两次。但指纹判的是**连接配置相同**而非物理库相同：同一个库用不同凭据连、或键值对顺序不同，仍会被当成两个库，因此逐库逻辑仍须能重复执行。

```json
{
  "TenantRouting": { "CacheLifetime": "00:05:00" }
}
```

`CacheLifetime` 同时决定改租户路由前的排空等待：`max(Access Token 有效期, CacheLifetime)`。

固定在控制库的 DbContext 声明专属连接名，与 `ControlPlaneConnectionStringName` 一致：

```csharp
[ConnectionStringName("IdentityControl")]
public sealed class IdentityControlDbContext : DbContext;
```

## 接口参考

| 类型 | 用途 |
| --- | --- |
| `Leistd.MultiTenancy.Tenancy.IMultiTenant` | 通过 `Guid? TenantId` 声明数据归属；`null` 表示宿主 |
| `Leistd.MultiTenancy.Context.ICurrentTenant` | 读取或临时切换租户上下文 |
| `CurrentTenantKeyExtensions.ScopeKey(currentTenant, key)` | 把缓存键、锁键等租户外部标识限定到当前租户；`this ICurrentTenant` 扩展 |
| `Leistd.MultiTenancy.Stores.ITenantStore` | 按 Id 或归一化名称查找租户 |
| `Leistd.MultiTenancy.Stores.ITenantManager` | 创建、修改、启停、软删除和分页查询租户；`GetPagedAsync(keyword, PageRequest, ct)` 返回 `PagedResult<TenantConfiguration>` |
| `ITenantManagementService` | 租户管理用例：`GetPagedAsync`、`GetAsync`、`CreateAsync`（登记→开通→启用与补偿）、`UpdateAsync`、`SetActivationAsync`、`DeleteAsync`；EF 包注册。<b>没有按名字查</b>——匿名按名字查等于给任何人一个租户枚举接口 |
| `ITenantProvisioner` | 宿主实现：`ProvisionAsync(context, ct)` 在新租户里写初始数据，`PurgeAsync(context, ct)` 幂等清除；未注册时不开通 |
| `ITenantActivationGuard` | 宿主实现：手动启用前的前置条件，不满足时抛带码异常 |
| `ITenantDatabaseErrorDescriber` | 把开通时的数据库错误翻成带码的 400；默认不翻译，宿主按自己的数据库实现并在组件注册前登记 |
| `TenantChangedEvent` | 管理用例成功后发布：`TenantId`、`DisplayName`、`Change`（`Created`/`Updated`/`ActivationChanged`/`Deleted`） |
| `ITenantConnectionManagementService` | 连接管理用例：`GetListAsync`、`GetRuntimeAsync`、`GetMigrationListAsync`、`SetAsync`、`RemoveAsync`；EF 包注册 |
| `ITenantConnectionDirectory` | 列出租户已登记的连接名与版本；租户不存在返回 `null`，不分库返回空列表 |
| `MultiTenancyErrorCodes` | 组件错误码，默认中英译文随包分发 |
| `MultiTenancyExceptionMappings.Configure(options)` | AspNetCore 包：由宿主显式登记租户停用 403、不存在 404、版本及命名冲突 409；宿主随后可覆盖 |
| `MapTenantManagement<TCreateInput>(configure)` / `MapTenantConnections(configure)` | AspNetCore 包：租户管理与连接端点；策略名必填，端点名前缀 `TenantManagementEndpoints.NamePrefix`；只有 `by-host` 匿名 |
| `UseTenantSessionRecovery(configure?)` | AspNetCore 包：租户会话自恢复中间件；`SignOutScheme`、`TenantInvalidHeader`（默认 `X-Tenant-Invalid`） |
| `AddRemoteTenantConnectionStore(serviceName, configuration)` | ServiceClient 包：远端连接存储，返回 `IHttpClientBuilder`；与控制库的 EF 存储二选一 |
| `ITenantConnectionConfigurationManager` | `SetAsync(tenantId, name, connectionString, expectedVersion, ct)` 登记或更新一条；`RemoveAsync(tenantId, name, expectedVersion, ct)` 删除一条（该名字随即回落到服务自己的配置）。**会改变数据落点的写入要求租户已停用**，判据见上文表格 |
| `AddLocalTenantConnectionResolution<TControlDbContext>(configure)` | EF 包：注册本地连接解析与本地迁移目标；`LocalTenantConnectionOptions.ControlPlaneConnectionStringName` 必填且不能是 `Default` |
| `AddRemoteTenantConnectionResolution()` | Core 包：注册远端连接解析、单飞协调器、内存缓存与远端迁移目标；绑定 `TenantRouting` 配置节 |
| `ITenantConnectionConfigurationStore` | 按名字读连接：`FindAsync(tenantId, name, ct)` 返回 `TenantConnectionLookupResult`（租户不存在或已删除时为 `null`），"精确名 → 默认名"的回落由实现完成；`GetListAsync(name, ct)` 供迁移作业枚举。EF 包提供控制库实现，ServiceClient 包提供远端实现，**按名字问、按名字答，一次只出一条** |
| `TenantConnectionLookupResult` | `HasAnyConnection` 区分"不分库"与"缺这个名字"；`Connection` 是命中的那一条 |
| `TenantRouteCacheOptions` | `CacheLifetime`（必填，不超过 `MaximumCacheLifetime` 1 小时） |
| `ITenantMigrationTargetProvider` | `GetDedicatedTargetsAsync(name, ct)` 按连接名枚举独立物理库 `TenantMigrationTarget(TenantId, ConnectionString)`，每个库一条，`Fingerprint` 为连接串的 SHA-256 |
| `ITenantDatabaseDirectory` | 控制库侧的库目录：按连接名回 `TenantDatabaseListResult(Databases, FailedTenants)`，不含连接串。EF 实现随 `AddMultiTenancyEfCore` 注册（它读的就是控制库），ServiceClient 的远端存储同时实现它；自定义存储的宿主要自己注册，`AddTenantManagement` 少了它解析不出连接管理用例 |
| `ITenantDatabaseEnumerator` | `GetDatabasesAsync(name, activeOnly, ct)` 列出运行时要逐库处理的物理库 `TenantDatabase(TenantId, Fingerprint, TenantIds)`；`TenantId` 为 `null` 即宿主库，宿主项的 `TenantIds` 为空（那份清单等于共享库的租户数，且要跨 HTTP 边界；进宿主库用宿主配置，不需要某个租户）。宿主库与独立库用同一套指纹算法，与宿主同配置的登记会被合并掉，不会让同一个物理库出现两次 |
| `ITenantDatabaseRunner` | `ForEachDatabaseAsync(name, activeOnly, action, ct)` 在每个物理库的代表租户上下文里执行回调，逐库隔离失败，返回 `TenantDatabaseRunResult(Databases, FailedDatabases, UnresolvedTenants)`（最后一项是本轮解析不出连接、被跳过的租户）；不替回调开工作单元。**两份清单都要看**：解析不出连接的租户有自己的库、这一轮一条数据都没处理，只看 `FailedDatabases` 会把这一轮报成成功 |
| `Leistd.MultiTenancy.Tenancy.MultiTenancySides` | 表示权限属于 `Tenant`、`Host` 或 `Both` |

`ITenantStore` 只有 EF 一种实现，由 `AddMultiTenancyEfCore<TDbContext>()` 注册。资源服务不注册它：把 `ValidateResolvedTenant` 置为 `false` 后解析链只信已验证主体的租户声明，中间件不查注册表。

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

- 中间件顺序必须是 `UseAuthentication()` →（`UseTenantSessionRecovery()`）→ `UseMultiTenancy()` → `UseAuthorization()`。不挂会话自恢复时，被停用租户的用户连登录页与注销端点都访问不了；跨域部署要把恢复标记头加进 CORS 暴露头。
- **连接的机器端点只对机器主体开放，但三条的敏感度不同**：`GET /migration?name=` 下发**全部租户**的明文连接串，只给一次性迁移作业的 DDL 身份；`GET /runtime/{tenantId}?name=` 下发**被问到的那一条**明文；`GET /databases?name=` **不下发连接串**，只回指纹与租户归属，因此它与 `/runtime` 同属 `RuntimeReadPolicy`，常驻服务的逐库作业走它即可，不必申请迁移权限。不签发机器令牌的部署不要配置 `RuntimeReadPolicy` / `MigrationReadPolicy`。
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
