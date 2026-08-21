# 多租户

业务服务经常需要让多个租户共用一套部署：每个租户拥有独立的用户、角色、权限授予和业务数据，彼此完全不可见；同时宿主（平台方）管理租户的生命周期。要正确做到这一点，至少要回答四个问题：当前请求属于哪个租户（解析）、这个租户是否存在且可用（校验）、查询和写入如何被限制在该租户分区内（隔离）、租户身份如何跨服务传递（传播）。

本组件家族把这四件事收敛为一套原语：`ICurrentTenant` 环境上下文（AsyncLocal，跨 `await`、跨后台任务、跨事件处理器流动）、可扩展的解析链与校验中间件、配合 DDD 基座全局查询过滤器的 `IMultiTenant` 标记接口与写入落值拦截器，以及租户注册表的存储与管理原语。

## 何时使用

| 场景 | 使用 |
| --- | --- |
| 服务需要按租户隔离数据（SaaS 后台） | `Core` + `AspNetCore` + `EntityFrameworkCore` 全套 |
| 资源服务仅消费 token 中的租户 claim，不持有租户注册表 | `Core` + `AspNetCore`，Store 用 `AddInMemoryTenantStore` 配置租户清单 |
| 后台任务/事件处理器需要在某租户上下文内执行 | `Core` 的 `ICurrentTenant.Change()` |
| 服务间调用需要传递租户 | 由 `Leistd.ServiceClient` 承担（出站 `X-Tenant-Id`、被调方受信恢复），本组件只负责解析恢复后的 claim |
| 非多租户项目 | 不引用、不注册——实体不实现 `IMultiTenant` 时过滤器不作用于任何实体，零成本 |

> 分层约定：`Core` 平台无关（Domain/Application 可引用）；`AspNetCore` 由 Web 宿主引用；`EntityFrameworkCore` 由基础设施层引用。

## 安装

```bash
dotnet add package Leistd.MultiTenancy.Core                # 领域/应用层：IMultiTenant、ICurrentTenant
dotnet add package Leistd.MultiTenancy.AspNetCore          # Web 宿主：中间件与解析链
dotnet add package Leistd.MultiTenancy.EntityFrameworkCore # 基础设施层：租户注册表与落值拦截器
```

## 配置

```csharp
// Program.cs（Web 宿主，含租户注册表的完整形态）
builder.Services.AddMultiTenancy(builder.Configuration);          // 上下文 + 解析链，绑定 Leistd:MultiTenancy
builder.Services.AddMultiTenancyEfCore<MyDbContext>();            // TenantRecord 存储 + 管理器 + 落值拦截器

builder.Services.AddDbContext<MyDbContext>((sp, options) =>
{
    options.UseNpgsql(connectionString);
    options.AddInterceptors(
        sp.GetRequiredService<MultiTenantSaveChangesInterceptor>()); // 显式挂载，与审计拦截器同型
});

var app = builder.Build();
app.UseAuthentication();
// 若使用 Leistd.ServiceClient 的被调方恢复，UseServiceUserContext() 在此
app.UseMultiTenancy();        // 认证之后（Claim 解析需要主体）、授权之前（权限检查须在租户上下文内）
app.UseAuthorization();
```

```csharp
// DbContext：租户注册表映射
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);   // BaseDbContext 在此套用软删除与租户全局过滤器
    modelBuilder.ConfigureMultiTenancy(); // TenantRecord 表
}
```

`AddMultiTenancy` 注册内容：

| 接口 | 实现 | 生命周期 |
| --- | --- | --- |
| `ICurrentTenantAccessor` | `AsyncLocalCurrentTenantAccessor.Instance`（静态单例） | Singleton |
| `ICurrentTenant` | `CurrentTenant` | Transient |
| `ITenantNormalizer` | `UpperInvariantTenantNormalizer` | Transient |
| `ITenantResolver` | `TenantResolver` | Scoped |

`AddMultiTenancyEfCore<TDbContext>` 追加：`ITenantStore` → `EfCoreTenantStore<TDbContext>`、`ITenantManager` → `EfCoreTenantManager<TDbContext>`（均 Transient，`TryAdd` 可替换）、`MultiTenantSaveChangesInterceptor`（Transient）、`IClock`（TryAdd UTC 默认）。除 `TDbContext` 外无其它基础设施依赖。

## 使用

### 实体按租户隔离

```csharp
// 业务实体实现 IMultiTenant，TenantId 由拦截器在保存时填充
public class Order : FullAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }   // null = 宿主数据
    public string Title { get; private set; } = string.Empty;
}
```

之后所有经 DDD 基座 `BaseDbContext` 的查询自动附加 `TenantId == 当前租户` 谓词（与软删除过滤器 AND 叠加）；新增实体在 `SaveChanges` 时自动落当前租户 Id（显式赋过值的不覆盖）。

### 切换租户上下文（后台任务、跨租户系统操作）

```csharp
public class TenantReportJob(ICurrentTenant currentTenant, MyDbContext db)
{
    public async Task RunForTenantAsync(Guid tenantId)
    {
        using (currentTenant.Change(tenantId))    // 释放时恢复父上下文，支持嵌套
        {
            var orders = await db.Orders.ToListAsync();   // 仅该租户的数据
        }
    }
}
```

两种"跨越隔离"的显式姿势，语义不同：

- `currentTenant.Change(null)`：**宿主视角**，仅显示 `TenantId == null` 的宿主行。
- `IDataFilter.Disable<IMultiTenant>()`（`Leistd.Ddd.Domain`）：**全量视角**，显示所有租户 + 宿主的行，用于全局统计、跨租户巡检等系统操作。

两者都应作为可审计的系统能力使用；普通业务代码不应触碰。

### 管理租户

```csharp
public class TenantAppService(ITenantManager tenantManager, ITenantStore tenantStore)
{
    // isActive 没有默认值：这是安全相关的选择，必须显式表态
    public Task<TenantConfiguration> CreateAsync(string name)
        => tenantManager.CreateAsync(name, displayName: null, isActive: true);
    // UpdateAsync / SetActiveAsync / DeleteAsync（软删除）同理；
    // 管理器负责名称归一化与未删除行内的唯一性校验
    // （冲突抛 DuplicateTenantNameException → 409）
}
```

契约与出参都在 Core（`TenantConfiguration`），应用层依赖 `ITenantManager` 即可完成租户管理，不必引用任何持久化实现包。

**创建后还要继续初始化租户数据（角色、管理员等）时，必须传 `isActive: false`**：

```csharp
var tenant = await tenantManager.CreateAsync(name, displayName, isActive: false);
using (currentTenant.Change(tenant.Id, tenant.Name))
{
    await SeedAsync(tenant);          // 期间租户对外不可用
}
await tenantManager.SetActiveAsync(tenant.Id, true);
```

租户一旦启用，中间件就会接受它——而此刻它可能还没有管理员和权限授予，任何匿名端点（注册、找回密码）带上它的 `X-Tenant-Id` 就能进入这个半成品租户。初始化失败时的补偿也可能失败，那样留下的会是一个永久可用、无人管得住的租户。停用态创建把这个窗口整体关掉。

### 解析与校验语义

默认解析链（`AddMultiTenancy` 装配，链为空时才写入默认值，可自定义增删排序）：

1. **CurrentPrincipal**：已认证主体的 `tenant_id` claim 定案并终止链——含"无 claim = 宿主用户"。请求头与查询串**无法改写已登录用户的租户**，这是防跨租户水平越权的关键顺序。
2. **Domain**（`{0}.example.com`）：仅在配置了 `MultiTenancyOptions.DomainFormat` 时生效，未配置直接跳过。排在头与查询串之前——子域名部署下域名是权威，匿名请求不能再用请求头把自己挪到别的租户。判定分三种：

   | 主机名 | 结果 |
   | --- | --- |
   | 受管域内且有合法租户段（`acme.example.com`） | 解析为该租户，终止链 |
   | 受管域内但没有租户段（基础域 `example.com`、多级子域 `a.b.example.com`、前缀形态不匹配） | **定案为宿主，终止链**——不定案的话链会继续走到请求头，匿名请求在基础域上带个 `X-Tenant-Id` 就能挑任意租户 |
   | 受管域之外（`svc.internal.cluster.local`） | 不定案，交回后续贡献者——服务间调用打的是集群内部主机名、靠 `X-Tenant-Id` 传租户，一刀切会把它打断 |

3. **Header**（`X-Tenant-Id`）：服务于匿名请求（登录前选租户、受信服务调用恢复前的原始头）。
4. **QueryString**（`?tenant=`）：服务于邮件验证、找回密码等匿名链接。

**子域名是便利性与安全性同时最优的形态**，也是 SaaS 的主流做法（Slack、Atlassian、Zendesk 等）：租户写在 URL 里，登录页不需要租户输入框，用户什么都不用记；同时系统不需要任何"列出全部租户"的接口——访客得先知道域名才能到达那个页面。

```csharp
builder.Services.AddMultiTenancy(options => options.DomainFormat = "{0}.example.com");
```

匹配规则：主机名大小写不敏感；端口不参与匹配（格式只写主机名，开发与生产共用一份配置）；末尾根点会被规范化后再匹配（DNS 里 `acme.example.com.` 与 `acme.example.com` 是同一个名字，不规范化就会退回请求头解析，等于给了一条绕过）；恰好等于基础域（`example.com`）视为宿主入口；租户段本身不能再含点号（`a.b.example.com` 不会被当成名为 `a.b` 的租户）。

**格式契约**（写错在启动期抛 `OptionsValidationException`）：

| 约束 | 说明 |
| --- | --- |
| 恰好一个 `{0}` | 多个占位符无法确定哪段是租户名 |
| 纯 ASCII | 国际化域名请填 punycode（`xn--` 前缀）形态，与浏览器发送的 Host 一致；不做启动期 IDN 规范化 |
| 至少两段 | 子域名解析需要有基础域 |
| 每段 1–63 字符 | 首尾为字母或数字，内部只允许字母、数字与连字符 |
| 总长 ≤ 253 | RFC 1035 |
| 不带末尾根点 | 配置侧只保留一种 canonical 形态；请求侧的等价写法由解析器规范化 |
| 占位符所在段之后要有固定基础域 | `example.{0}` 非法——受管域正是按段边界取占位符之后那部分，没有基础域就无从判定 |

占位符不必占满整段，前后都可以有固定文本：`tenant-{0}.example.com`、`{0}-tenant.example.com` 都合法。**受管域按 DNS 段边界计算**——两者的受管域都是 `example.com`，而不是"占位符之后的原始字符串"。

> **部署注意**：只把真正用于租户的通配子域指向本应用。把整个顶级域通配过来时，`www.example.com` 会被解析成名为 `www` 的租户并因查不到而 404。
>
> **代理信任是前提**：解析读的是 `Request.Host`，而 `UseForwardedHeaders()` 会用 `X-Forwarded-Host` 覆盖它。若宿主把 `KnownProxies`/`KnownIPNetworks` 清空（=接受任何客户端的转发头），任意访客都能用一个请求头改写"权威来源"，这条边界即告失效。宿主必须只信任明确列出的代理网段。

`DomainFormat` 写错（漏占位符、多个占位符、带 scheme/路径/端口/空白）会在**启动期**抛 `OptionsValidationException`，而不是运行期静默退回请求头解析——安全配置的错误要大声失败，不能悄悄降级。

**不要提供匿名的"租户列表"接口**去做可搜索下拉框——那等于公开全部客户名单。需要"忘记租户名"的便利时，把候选列表**发到用户邮箱**，而不是渲染在登录页上。ABP 同样只暴露按名/按 Id 的单点查询（AbpTenantAppService 上没有任何返回租户集合的方法），其切换 UI 也是纯文本框。

值可以是租户 Guid 或名称（名称大小写不敏感）。校验失败语义：

| 情况 | 行为 |
| --- | --- |
| 未解析出租户 | 宿主上下文，正常放行 |
| 租户不存在 | 抛 `TenantNotFoundException`（继承 `NotFoundException` → 404） |
| 租户已停用 | 抛 `TenantNotActiveException`（继承 `ForbiddenException` → 403） |
| 租户已删除 | 存储按 `!IsDeleted` 过滤，读不到即视为不存在 → 404（与"租户不存在"同一形状，不泄漏"这个租户曾经存在"） |

### 写入侧：租户值何时落定

新增实体的 `TenantId` 在**实体进入跟踪时**落定（`BaseDbContext` 挂 `ChangeTracker.Tracked`），不是在保存时。

这个时机是必须的：仓储在工作单元内**不立即保存**（由 UoW 统一提交），因此"新增"与"保存"之间可能跨越 `ICurrentTenant.Change` 的边界。若在保存时刻取当前租户，下面这段会把数据静默落成宿主行——

```csharp
using (currentTenant.Change(tenantId))
{
    await repository.InsertAsync(entity);   // UoW 内不保存
}                                          // 作用域退出
await unitOfWork.CompleteAsync();          // 此刻已无租户上下文
```

后果是双向的：**该租户看不见自己创建的数据**（过滤器要求 `TenantId` 等于当前租户），而**宿主管理员看得见**，且没有任何报错。进入跟踪的时刻才是"这条数据属于谁"的语义时刻。

显式赋过值的不覆盖；宿主上下文保持 `null` 即宿主数据；查询 materialize 出来的实体不碰。

## 接口参考

### `Leistd.MultiTenancy.IMultiTenant`

| 成员 | 说明 |
| --- | --- |
| `Guid? TenantId { get; }` | 所属租户，`null` 表示宿主数据 |

### `Leistd.MultiTenancy.ICurrentTenant`

| 成员 | 说明 |
| --- | --- |
| `bool IsAvailable` | 是否存在租户上下文 |
| `Guid? Id` / `string? Name` | 当前租户 Id / 名称（Name 仅供展示与日志） |
| `IDisposable Change(Guid? id, string? name = null)` | 切换上下文，释放时恢复父级；传 `null` 即宿主视角 |

### `Leistd.MultiTenancy.ITenantStore`

| 成员 | 说明 |
| --- | --- |
| `FindAsync(Guid id, ct)` | 按 Id 查找，不存在返回 null |
| `FindByNameAsync(string normalizedName, ct)` | 按归一化名称查找 |

### `Leistd.MultiTenancy.ITenantManager`

出参统一为 `TenantConfiguration`（与 `ITenantStore` 共用），EF 实现见 `AddMultiTenancyEfCore<TDbContext>()`。

| 成员 | 说明 |
| --- | --- |
| `CreateAsync(name, displayName, isActive, ct)` | 创建（归一化 + 唯一校验）。`isActive` **无默认值**：创建后还要初始化租户数据时必须传 `false` |
| `UpdateAsync(id, name, displayName, ct)` | 改名（归一化 + 唯一校验） |
| `SetActiveAsync(id, isActive, ct)` | 启停 |
| `DeleteAsync(id, ct)` | 软删除（不依赖审计拦截器，绝不物理删除） |
| `FindAsync(id, ct)` | 管理读路径按 Id 查找，不存在/已删除返回 null |
| `GetPagedAsync(keyword, offset, limit, ct)` | 分页查询（名称/显示名关键字，按创建时间倒序），返回 `TenantPage` |

### `Leistd.MultiTenancy.MultiTenancySides`

| 成员 | 值 | 说明 |
| --- | --- | --- |
| `Tenant` | 1 | 租户侧专属 |
| `Host` | 2 | 宿主侧专属 |
| `Both` | 3 | 两侧通用（默认） |

供 `Leistd.Authorization` 的权限定义声明侧别：宿主侧权限（如"租户管理"）在租户上下文内对任何主体（含超管）不可用，定义树与授予界面也应按侧别过滤。

## 配置项（`Leistd:MultiTenancy`）

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `HeaderName` | `X-Tenant-Id` | 租户请求头名（与 `Leistd.ServiceClient` 出站头默认值一致） |
| `QueryStringParameterName` | `tenant` | 租户查询串参数名 |
| `TenantClaimType` | `tenant_id` | 主体租户 claim 类型（`CustomClaimTypes.TenantId`） |

## 实现行为

### Leistd.MultiTenancy.Core

- `AsyncLocalCurrentTenantAccessor` 是**静态单例**：状态挂在 ExecutionContext 上，跨 `await`、跨 `Task.Run`、跨事件处理器创建的新 DI Scope 天然流动；拦截器等无 DI 场景也能取到同一实例。
- `Change()` 返回的句柄二次释放幂等（只恢复一次）。
- `TenantResolver` 以 Scoped 注册：贡献者从当前请求作用域解析所需服务。

### Leistd.MultiTenancy.AspNetCore

- 中间件把 `Change()` 包裹整个下游管道，并压入日志 Scope `leistd.tenantId`（结构化日志按租户检索）。
- 异常直接上抛，由 `Leistd.Exception` 全局处理器映射 HTTP 状态码；组件不自带错误页。

### Leistd.MultiTenancy.EntityFrameworkCore

- `EfCoreTenantStore` **不缓存**，每次解析都读库。它的返回值里带 `IsActive`，中间件据此放行或 403——这是访问控制状态，而 cache-aside 的失效是尽力而为的：缓存不可用时失效失败，已停用/已删除的租户仍被陈旧条目放行；删除之后连重试失效都做不到（租户已软删，管理器按 Id 找不到它，直接抛 `TenantNotFoundException`）。缩短 TTL 只能缩窄窗口、关不掉它，因此**启停与删除的生效由构造保证**：提交那一刻即对所有节点生效。
- 成本可忽略：租户解析每请求一次索引查找，用的是当前请求已有的 `DbContext`，不新开连接；同一个仓库里用户判活（`ActiveUserRequirement`）本来就是每请求读库，租户表比用户表更小更热。附带收益是租户解析不再依赖分布式缓存可用——此前缓存读取失败会直接抛出，缓存一挂则所有带租户的请求全部 500。
- 极高 RPS 且能接受撤销延迟的服务，自行包装 `ITenantStore` 装饰器加缓存。**那是一个需要显式承担陈旧风险的决定**，不由框架替所有人默认做。
- 落值拦截器只处理 `Added` 且 `TenantId == null` 的实体；宿主上下文保存的实体保持 `null` 即宿主数据。
- `TenantRecord` 不实现 `IMultiTenant`（它本身是宿主侧数据）。名称唯一性由**未删除行上的部分唯一索引**保证（`IsDeleted = false` 过滤，PostgreSQL 与 SQLite 通用），删除后名称可复用；管理器的先查后校验只负责给出友好错误，并发落败方由数据库拒绝后同样得到 `DuplicateTenantNameException`（映射 409）。低频与权限门禁都不能替代数据库不变量。

### 与 DDD 基座的配合（`Leistd.Ddd.Infrastructure`）

- `BaseDbContext` 用 **EF 10 命名查询过滤器**同时套用软删除（`SoftDelete`）与租户（`MultiTenant`）两个过滤器，二者独立开关、AND 叠加；过滤器表达式捕获 DbContext 实例属性，EF 参数化后每次查询重估——`Change()` 与 `IDataFilter` 开关即时生效。
- 仓储 `GetByIdAsync` 走过滤查询而非 `FindAsync`（后者绕过全局过滤器）；跨租户按 Id 取数返回 `null`，宿主应统一表现为 404。

## 注意事项

- **中间件顺序**：`UseAuthentication()` →（`UseServiceUserContext()`）→ `UseMultiTenancy()` → `UseAuthorization()`。放在认证前 Claim 贡献者拿不到主体；放在授权后权限检查会落在错误的租户分区。
- **认证端职责**：签发 cookie/token 时必须写入 `tenant_id` claim（`CustomClaimTypes.TenantId`），否则已登录用户每次请求都会退回宿主上下文。
- **派生 DbContext 改写 `ConfigureModel`**（`BaseDbContext` 已封闭 `OnModelCreating`）：组件实体配置必须在过滤器套用之前进入模型，否则没有 `DbSet` 声明的实体（如授权版本表）会逃过租户隔离。详见 [DDD 基座](../ddd-struct/ddd-struct.md)。
- **绕过过滤器的红线**：`IgnoreQueryFilters()` 与 raw SQL（如 EF 的 FromSqlRaw）都会越过租户隔离，代码评审应按跨租户操作对待；需要合法跨租户时用 `Disable<IMultiTenant>()` 显式表达。
- **租户实体的唯一约束要写成宿主行与租户行成对的部分索引**：`(TenantId, X)` 直接建唯一索引时，PostgreSQL 与 SQLite 都视 NULL 互不相等，宿主行（`TenantId IS NULL`）会失去唯一性兜底。正确形态是一条 `IS NULL` 过滤的 `(X)` 唯一索引加一条 `IS NOT NULL` 过滤的 `(TenantId, X)` 唯一索引——框架的授予、版本与租户注册表都按此配置。
- **外部系统提供的标识必须按租户分区**：第三方身份（如 OAuth 的 `provider + providerUserId`）只在租户内唯一。不分区会让同一外部身份在跨租户登录时命中别的租户的绑定，并阻止它在多个租户各自绑定。
- **缓存租户数据的 key 必须含租户 Id**（形如 "app:{tenantId}:orders:{id}"）——分布式缓存不会自动分区。
- **租户级互斥操作的锁 key 必须含租户 Id**（形如 "app:tenant-init:{tenantId}"），否则所有租户互相排队。
- **超级管理员不旁路租户隔离**：`IsSuperAdmin` 只旁路功能权限；跨租户数据访问必须走上面的显式姿势。
- 多服务体系中租户注册表应归属**身份服务**（token 携带 `tenant_id`），资源服务无需本地租户表；确需本地校验时用 `AddInMemoryTenantStore` 配置清单，注意它不会随身份服务自动同步。

## 相关

- [组件总览](./README.md)
- [DDD 基座](../ddd-struct/ddd-struct.md)：全局查询过滤器与仓储行为
- [授权](./authorization.md)：权限定义的多租户侧别
- [服务调用](./service-client.md)：跨服务租户传递与受信恢复
