# DDD 四层基座

DDD 基座为 Domain、Application.Contracts、Application 和 Infrastructure 提供实体、仓储、数据过滤、DTO 与 EF Core 集成。

## 何时使用

| 层 | 包 | 主要能力 |
| --- | --- | --- |
| Domain | `Leistd.Ddd.Domain` | 实体、审计基类、仓储契约、数据过滤器 |
| Application.Contracts | `Leistd.Ddd.Application.Contracts` | 应用服务契约、DTO、分页类型 |
| Application | `Leistd.Ddd.Application` | 应用服务基类（约定标记 + 预留扩展缝）、分页映射 |
| Infrastructure | `Leistd.Ddd.Infrastructure` | EF Core 仓储、DbContext 基类、过滤器和本地事件 |

业务项目通常每层建立一个工程并只引用对应包，层间依赖为 Application → Application.Contracts → Domain。

框架包本身的引用更窄：`Leistd.Ddd.Application` 只引用 `Application.Contracts` 与
`Leistd.ObjectMapping.Core`（分页映射需要 `IObjectMapper`，而 `Application.Contracts`
作为客户端也要引用的契约包，不应背上映射依赖）；`Leistd.Ddd.Infrastructure` 依赖 `Domain`。

### DbContext 显式接入

每个注册过的 DbContext 都要调用一次 `AddDddDbContext<TDbContext>()`，完整写法见[注册](#注册)。

**漏掉的上下文会逃出租户过滤器闸门**——那道闸门是"多租户实体被映射进没有租户过滤器的
DbContext"的唯一拦截点。装了 `DynamicProxyServiceRegistrationCallbackFactory` 时，漏写在构建容器时直接
失败；**不装则框架查不出漏登记**——校验器不执行，闸门也只看已登记的上下文，漏掉的那个
自始至终不可见。这就是它属于必要步骤而非可选优化的原因。
不需要仓储的上下文调用无参重载即可，那也算显式声明。

刻意不扫描容器自动发现：扫描得到的覆盖面取决于宿主怎么注册 DbContext（工厂委托注册
看不到实现类型；不装 provider factory 则注册回调根本不执行），于是一道安全闸门的
有效性会挂在无关的注册形态上。

**仓储的实体来源是 `DbSet<T>` 声明**，且 `T` 实现 `IEntity`。这是有意的选择信号——声明
`DbSet<T>` 等于宣布"这是我要直接查询的实体"。只经 `modelBuilder` 映射、不暴露 `DbSet<T>`
的实体不在其中（注册阶段拿不到 EF Core 模型，取 `Model` 要实例化 DbContext 而那需要已构建
的容器）。确实需要时用
`AddDefaultRepository<TEntity>()` 点名，或用 `AddRepository<TEntity, TImpl>()` 指定自定义实现。

同一实体被两个上下文各注册一次会**直接抛异常**：Microsoft DI 让后注册的静默胜出，调用方
无从知道读的是哪个库。

### BaseAppService 是预留的扩展缝

`BaseAppService` 当前不含共享行为，保留它是为了留住一条**版本推送通道**：「继承」写在生成后的
项目代码里，而类住在框架包里，只有生成代码已经继承，框架才能在后续版本下发应用服务层的公共
行为并靠升级包生效。删掉它，已生成的项目只能逐个回改。

它同时实现 `IAppService`，继承即满足标记，派生类不必重复声明。注意 `IAppService`
**不参与注册与织入**：本框架注册一律显式手写，拦截器织入判据是特性（如 `[UnitOfWork]`）。
标记只是把"这个类型是应用服务"写进类型系统，供阅读与后续分析器使用。

**可以加**不需要注入状态的成员：`protected` 帮助方法、模板方法钩子、约定常量。
**不要加** `IServiceProvider` 或延迟服务定位器（暴露时钟、映射器、当前用户之类）——那会把真实
依赖从构造签名里藏起来，是 .NET 依赖注入指南明确列出的反模式。应用服务需要什么就在自己的
构造函数里声明。横切关注点由 AOP 拦截器 + 特性承担，不走继承。

## 安装

```bash
dotnet add package Leistd.Ddd.Domain
dotnet add package Leistd.Ddd.Application.Contracts
dotnet add package Leistd.Ddd.Application
dotnet add package Leistd.Ddd.Infrastructure
```

## 注册

四步缺一不可：

```csharp
// 1. 拦截器织入与漏登记校验都由这个工厂驱动，不装则两者都不生效
builder.Host.UseServiceProviderFactory(new DynamicProxyServiceRegistrationCallbackFactory());

// 2. 工作单元、EF Core 支持与数据过滤器
builder.Services.AddDddInfrastructure(options =>
{
    options.IsTransactional = true;
});

// 3. 每个 DbContext 显式登记一次；不需要仓储的调无参重载
builder.Services.AddDddDbContext<AppDbContext>(o => o.AddDefaultRepositories());
builder.Services.AddDddDbContext<ControlPlaneDbContext>();

// 4. 修改/删除审计与领域事件需要显式挂载拦截器
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseNpgsql(connectionString);
    options.AddDddInterceptors(sp);
});
```

`AddDddInfrastructure()` 注册工作单元、EF Core 支持和数据过滤器；仓储由 `AddDddDbContext<TDbContext>()` 按上下文显式注册。`AddDddInterceptors(sp)` 为业务 DbContext 挂载：

- `AuditSaveChangesInterceptor`：修改/删除审计与软删除转换。
- `LocalEventSaveChangesInterceptor`：收集并发布实体本地事件。
- `ConcurrencyStampSaveChangesInterceptor`：配置并换发乐观并发标记。

拦截器不会自动挂到所有 DbContext。控制面上下文若只需审计，应单独挂载 `AuditSaveChangesInterceptor`。

## 使用

### 定义实体与本地事件

```csharp
public class Order : FullAuditedEntity<Guid>, IAggregateRoot<Guid>
{
    public string CustomerName { get; private set; } = string.Empty;

    private Order() { }

    public Order(Guid id, string customerName) : base(id)
    {
        CustomerName = customerName;
        AddLocalEvent(new OrderCreatedEvent(id));
    }
}
```

审计字段由基础设施填充，业务代码不手动赋值。创建审计在实体进入跟踪时落定，修改与删除审计在保存时落定；详见[审计组件](../components/auditing.md)。

### 通过仓储读写

```csharp
public class OrderManager(IRepository<Order, Guid> repository)
{
    public Task<Order?> GetAsync(Guid id, CancellationToken ct = default)
        => repository.GetByIdAsync(id, ct);

    public Task<Order> CreateAsync(Order order, CancellationToken ct = default)
        => repository.InsertAsync(order, ct);
}
```

仓储写入在工作单元内延迟到统一提交，在工作单元外立即调用 `SaveChangesAsync`。`GetByIdAsync` 使用过滤查询而非 `FindAsync`，不会绕过软删除或租户隔离。

### 分页映射

```csharp
var source = new PagedResultDto<Order>(total, orders);
return mapper.MapPagedResult<Order, OrderDto>(source);
```

`PagedRequestDto` 默认 `Offset = 0`、`Limit = 10`，上限为 1000。`Sorting` 的可用字段应由具体应用服务校验。

### 临时关闭数据过滤器

```csharp
using (dataFilter.Disable<ISoftDelete>())
{
    return await repository.GetListAsync();
}
```

`IDataFilter` 基于嵌套作用域恢复先前状态。多租户过滤可使用 `Disable<IMultiTenant>()` 独立关闭。

### 定义 DbContext

```csharp
public class AppDbContext(
    DbContextOptions<AppDbContext> options,
    IServiceProvider serviceProvider)
    : BaseDbContext(options, serviceProvider)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity => entity.ConfigureByConvention());
        modelBuilder.ConfigureAuthorization();
    }
}
```

`BaseDbContext.OnModelCreating` 已封闭；派生类只覆盖 `ConfigureModel`。基类在派生配置完成后为所有已进入模型的实体添加命名过滤器，防止未声明 `DbSet` 的组件实体逃逸软删除或租户隔离。

需要审计、租户上下文或运行时过滤开关时，DbContext 必须接收 `IServiceProvider` 并传给基类。否则它固定为宿主视角，创建审计与 `IDataFilter` 作用域均无法生效。

软删除与租户过滤器分别命名为 `SoftDelete` 和 `MultiTenant`，同时命中时以 AND 叠加。注册了 `ICurrentTenant` 后，启动检查会拒绝任何未带 `MultiTenant` 过滤器的 `IMultiTenant` 实体。

## 接口参考

| 包 | 关键类型 |
| --- | --- |
| `Leistd.Ddd.Domain` | `Entity<TKey>`、审计实体基类、`IRepository<TEntity, TKey>`、`IDataFilter` |
| `Leistd.Ddd.Application.Contracts` | `IAppService`、`EntityDto<TKey>`、`PagedRequestDto`、`PagedResultDto<T>` |
| `Leistd.Ddd.Application` | `BaseAppService`、`MapPagedResult<TSource, TDestination>` |
| `Leistd.Ddd.Infrastructure` | `AddDddInfrastructure`、`BaseDbContext`、`EfCoreRepository`、`AddDddInterceptors` |

`IRepository<TEntity>` 提供查询、计数、存在性与批量写入；带主键的接口另提供按 Id 读取和删除。`IQueryableAsyncExecuter` 使 Domain 可异步执行 `IQueryable`，而不直接依赖 EF Core。

审计实体层次为 `CreationAuditedEntity<TKey>` → `ModificationAuditedEntity<TKey>` → `DeletionAuditedEntity<TKey>`；`FullAuditedEntity<TKey>` 聚合创建、修改与删除审计契约。

## 实现行为

- `AddDddDbContext<TDbContext>(o => o.AddDefaultRepositories())` 扫描该上下文的 public `DbSet<>` 属性，为对应实体注册 Scoped 仓储；不传选项即只登记上下文、不注册仓储。未声明 `DbSet<>` 的实体用 `AddDefaultRepository<TEntity>()` 点名。
- `LocalEventSaveChangesInterceptor` 在保存前收集并清空实体事件，保存成功后加入当前工作单元；无工作单元时直接发布。保存失败会丢弃本次收集。
- 使用同步 `SaveChanges()` 时，本地事件发布需要 sync-over-async 并记录 Warning；应优先使用 `SaveChangesAsync()`。
- 全局过滤器只应用于 EF Core 模型中的根实体类型，派生实体不重复添加。

## 建模原语

### 聚合根

```csharp
public class Order : FullAuditedEntity<Guid>, IAggregateRoot<Guid>;
```

`IAggregateRoot<TKey>` 是接口而非基类，因此可与任意实体或审计基类组合。该标记不限制仓储泛型；需要强制“只为聚合根建仓储”的项目应使用架构测试。

### 值对象

```csharp
public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Amount;
        yield return Currency;
    }
}
```

`ValueObject` 按严格运行时类型与 `GetAtomicValues()` 结果实现相等、哈希和运算符。所有字段都参与相等且无构造不变量时，直接使用 `record`；需要排除派生字段或防止 `with` 绕过构造校验时使用本基类。

### 乐观并发标记

```csharp
public class Document : Entity<Guid>, IHasConcurrencyStamp
{
    public string ConcurrencyStamp { get; set; } = ConcurrencyStamps.New();
}
```

`ConfigureByConvention()` 将该属性配置为必填、最长 40 的并发令牌。`ConcurrencyStampSaveChangesInterceptor` 在新增时补种空值，在修改时换发；并发更新的落败方收到 `DbUpdateConcurrencyException`。

框架不提供手动 `Renew()`，避免遗漏调用时静默失去并发保护。断开连接更新时，应将客户端回传的标记设置为 EF Core `OriginalValue`。

## 注意事项

- 业务 DbContext 必须显式调用 `AddDddInterceptors(sp)`；漏挂会使修改/删除审计、软删除转换、本地事件或并发标记静默失效。
- 实现 `ISoftDelete` 的实体在删除时转为逻辑删除，查询默认不可见；临时读取使用 `IDataFilter.Disable<ISoftDelete>()`。
- `AddLocalEvent` 只由实体内部调用；`GetLocalEvents()` 与 `ClearLocalEvents()` 仅供基础设施使用。
- `PagedResultDto<T>` 将 `null` items 视为空集合；`MapPagedResult` 对空 mapper 或 source 抛 `ArgumentNullException`。
- Entity 默认使用引用相等；需要按主键值相等时由业务类型明确实现。

## 相关

- [审计](../components/auditing.md)
- [多租户](../components/multi-tenancy.md)
- [工作单元](../components/unit-of-work.md)
- [事件总线](../components/event-bus.md)
