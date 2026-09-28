# DDD 四层基座

DDD 基座为 Domain、Application.Contracts、Application 和 Infrastructure 提供实体、仓储、数据过滤、DTO 与 EF Core 集成。

## 何时使用

| 层 | 包 | 主要能力 |
| --- | --- | --- |
| Domain | `Leistd.Ddd.Domain` | 实体、审计基类、仓储契约、数据过滤器 |
| Application.Contracts | `Leistd.Ddd.Application.Contracts` | 应用服务契约、DTO、分页类型 |
| Application | `Leistd.Ddd.Application` | 应用服务标记基类、分页映射 |
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

### 应用服务基类

`BaseAppService`（`Leistd.Ddd.Application.AppServices`）当前只实现
`IAppService`（`Leistd.Ddd.Application.Contracts.AppServices`）标记，不参与注册或织入；服务仍需显式注册，
横切行为由特性和拦截器提供。派生服务的依赖应保留在构造函数中，不通过 `IServiceProvider` 隐藏。

## 安装

```bash
dotnet add package Leistd.Ddd.Domain
dotnet add package Leistd.Ddd.Application.Contracts
dotnet add package Leistd.Ddd.Application
dotnet add package Leistd.Ddd.Infrastructure
```

## 注册

注册基础设施、每个 DbContext 和所需拦截器：

```csharp
// 1. 拦截器织入与漏登记校验都由这个工厂驱动，不装则两者都不生效
builder.Host.UseServiceProviderFactory(new DynamicProxyServiceRegistrationCallbackFactory());

// 2. 工作单元、EF Core 支持与数据过滤器
builder.Services.AddDddInfrastructure(options =>
{
    options.IsTransactional = true;
});

// 3. DbContext 只配置连接
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

// 4. 每个 DbContext 显式登记一次；不需要仓储的调无参重载
builder.Services.AddDddDbContext<AppDbContext>(o => o.AddDefaultRepositories());
builder.Services.AddDddDbContext<ControlPlaneDbContext>();
```

`AddDddInfrastructure()` 注册工作单元、本地事件总线、EF Core 支持和数据过滤器，工作单元选项绑定 `Leistd:UnitOfWork` 配置节，传入的委托在绑定之后应用；仓储由 `AddDddDbContext<TDbContext>()` 按上下文显式注册。登记派生自 `BaseDbContext` 的上下文时，它经官方 `ConfigureDbContext<TDbContext>` 挂载三个保存拦截器（与 `AddDbContext` 的先后无关，重复登记不会挂第二层）：

- `AuditSaveChangesInterceptor`：修改/删除审计与软删除转换。
- `LocalEventSaveChangesInterceptor`：收集并发布实体本地事件。
- `ConcurrencyStampSaveChangesInterceptor`：配置并换发乐观并发标记。

不继承 `BaseDbContext` 的上下文不挂载。控制面上下文若只需审计，在 `AddDbContext` 中单独添加 `AuditSaveChangesInterceptor`。拦截器按作用域解析，因此不支持 DbContext 池与默认（单例）生命周期的 `AddDbContextFactory`：其选项为单例，解析拦截器与 `BaseDbContext` 所用的都是根容器。

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

延迟提交有一个后果值得单列：**唯一索引等约束冲突在冲刷时才抛出，不在 `InsertAsync` 抛出**。
所以工作单元内的 `try { InsertAsync } catch` 是永不触发的死代码，要就地处理并发首次写入
必须先 `IUnitOfWork.SaveChangesAsync`（见[工作单元](../components/unit-of-work.md#在事务内提前冲刷)）。

### 分页映射

```csharp
var source = new PagedResult<Order>(total, orders);
return mapper.MapPagedResult<Order, OrderDto>(source);
```

`PageRequest` 默认 `Offset = 0`、`Limit = 10`，上限为 1000。`Sorting` 的可用字段应由具体应用服务校验。

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
        modelBuilder.ConfigurePermissionAuthorization();
    }
}
```

`BaseDbContext.OnModelCreating` 与 `ConfigureConventions` 已封闭；派生类覆盖 `ConfigureModel` 配置实体、覆盖 `ConfigureModelConventions` 追加模型约定（如枚举统一存为字符串）。基类在派生配置完成后为所有已进入模型的实体添加命名过滤器，防止未声明 `DbSet` 的组件实体逃逸软删除或租户隔离。

基类注册 EF Core 约定 `DddEntityConvention`：审计人字段（`CreatorId`、`LastModifierId`、`DeleterId`）最长 64，`ConcurrencyStamp` 见[乐观并发标记](#乐观并发标记)。约定以约定来源写入，实体上的显式 Fluent 配置优先。不继承基类的上下文可在 `ConfigureConventions` 中注册同一约定：

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    => configurationBuilder.Conventions.Add(_ => new DddEntityConvention());
```

需要审计、租户上下文或运行时过滤开关时，DbContext 必须接收 `IServiceProvider` 并传给基类。否则它固定为宿主视角，创建审计与 `IDataFilter` 作用域均无法生效。

软删除与租户过滤器分别命名为 `SoftDelete` 和 `MultiTenant`，同时命中时以 AND 叠加。注册了 `ICurrentTenant` 后，启动检查会拒绝任何未带 `MultiTenant` 过滤器的 `IMultiTenant` 实体。

## 接口参考

| 包 | 关键类型 |
| --- | --- |
| `Leistd.Ddd.Domain` | `Entity<TKey>`、审计实体基类、`IRepository<TEntity, TKey>`、`IDataFilter` |
| `Leistd.Ddd.Application.Contracts` | `AppServices.IAppService`、`EntityDto<TKey>`、`PageRequest`、`PagedResult<T>` |
| `Leistd.Ddd.Application` | `AppServices.BaseAppService`、`MapPagedResult<TSource, TDestination>` |
| `Leistd.Ddd.Infrastructure` | `AddDddInfrastructure`、`AddDddDbContext`、`BaseDbContext`、`DddEntityConvention`、`EfCoreRepository` |

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

`IAggregateRoot<TKey>` 可与实体或审计基类组合；该标记本身不限制仓储泛型。

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

`DddEntityConvention` 将该属性配置为必填、最长 40 的并发令牌。`ConcurrencyStampSaveChangesInterceptor` 在新增时补种空值，在修改时换发；并发更新的落败方收到 `DbUpdateConcurrencyException`。

断开连接更新时，应将客户端回传的标记设置为 EF Core `OriginalValue`。

## 迁移快照检查

模型约定和组件实体配置随框架版本演进。升级会改变模型的框架版本（升级说明会注明）后，必须确认迁移快照与当前模型一致，否则真实库上执行迁移时会因模型存在未迁移的变更而失败，而 InMemory 测试察觉不到。

在测试中经设计时工厂检查，不连库、不装 `dotnet-ef`：

```csharp
[Fact]
public void The_migration_snapshot_matches_the_model()
{
    using var dbContext = new AppDbContextFactory().CreateDbContext([]);
    Assert.False(dbContext.Database.HasPendingModelChanges());
}
```

或在命令行执行 `dotnet ef migrations has-pending-model-changes --context AppDbContext`。有差异时用 `dotnet ef migrations add` 生成迁移。

## 注意事项

- 业务 DbContext 必须继承 `BaseDbContext` 并经 `AddDddDbContext<TDbContext>()` 登记；否则修改/删除审计、软删除转换、本地事件与并发标记都不生效。
- 实现 `ISoftDelete` 的实体在删除时转为逻辑删除，查询默认不可见；临时读取使用 `IDataFilter.Disable<ISoftDelete>()`。
- `AddLocalEvent` 只由实体内部调用；`GetLocalEvents()` 与 `ClearLocalEvents()` 仅供基础设施使用。
- `PagedResult<T>` 将 `null` items 视为空集合；`MapPagedResult` 对空 mapper 或 source 抛 `ArgumentNullException`。
- Entity 默认使用引用相等；需要按主键值相等时由业务类型明确实现。

## 相关

- [审计](../components/auditing.md)
- [多租户](../components/multi-tenancy.md)
- [工作单元](../components/unit-of-work.md)
- [事件总线](../components/event-bus.md)
