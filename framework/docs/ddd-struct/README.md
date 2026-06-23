# DDD 四层基础类型

构建中大型业务系统时，最难统一的不是某个框架，而是**分层约定**：实体放哪、审计字段谁来填、仓储接口长什么样、应用服务怎么映射 DTO、权限怎么声明。各团队各写一套，代码就难以复用与维护。

Leistd 的 DDD 分组提供一套 Volo.ABP 风格的领域驱动设计基础类型，按 **Domain / Application.Contracts / Application / Infrastructure** 四层划分职责：Domain 定义实体基类、仓储抽象、审计接口与数据过滤器；Application.Contracts 提供 DTO 基类与分页约定；Application 提供应用服务基类与权限模型；Infrastructure 基于 EF Core 落地仓储、自动审计、软删除过滤与本地事件发布。业务项目只需继承这些基类、注册 `AddDddInfrastructure()`，即可获得审计字段自动填充、软删除、仓储自动注册、领域事件随保存发布等开箱能力。

## 何时使用

| 场景 | 引用的包 |
| --- | --- |
| 定义领域实体、仓储接口、审计/软删除模型（领域层代码） | `Leistd.Ddd.Domain` |
| 定义对外 DTO、分页请求/结果、应用服务契约 | `Leistd.Ddd.Application.Contracts` |
| 编写应用服务、声明权限、对 Controller 加权限校验 | `Leistd.Ddd.Application` |
| 用 EF Core 落地仓储、启用自动审计与软删除、注册基础设施 | `Leistd.Ddd.Infrastructure` |

> 四层按依赖方向引用：Application 依赖 Application.Contracts 与 Domain，Infrastructure 依赖 Domain。业务项目通常每层各建一个工程，分别引用对应的 Leistd.Ddd.* 包。

## 安装

```bash
# 领域层：实体基类、仓储抽象、审计接口、数据过滤器
dotnet add package Leistd.Ddd.Domain

# 契约层：DTO 基类、分页 DTO、对象映射扩展
dotnet add package Leistd.Ddd.Application.Contracts

# 应用层：应用服务基类、权限模型与特性（依赖 Domain）
dotnet add package Leistd.Ddd.Application

# 基础设施层：EF Core 仓储、自动审计、软删除、本地事件（依赖 Domain）
dotnet add package Leistd.Ddd.Infrastructure
```

> 本仓库的模板项目通过中央包管理（CPM）统一版本，添加时无需写版本号。

## 配置 Provider

在 `Program.cs` 注册基础设施服务（仅 `Leistd.Ddd.Infrastructure` 提供 DI 扩展）：

```csharp
// 注册 DDD 基础设施：UnitOfWork + EF Core + 自动仓储注册 + 审计 + 数据过滤器
builder.Services.AddDddInfrastructure();

// 可选：配置 UnitOfWork 选项
builder.Services.AddDddInfrastructure(uow =>
{
    // 在此配置 UnitOfWorkOptions
});
```

`AddDddInfrastructure()` 一次性完成以下注册：

| 接口 | 实现 | 生命周期 |
| --- | --- | --- |
| `IQueryableAsyncExecuter` | `EfCoreQueryableAsyncExecuter` | Singleton |
| `IClock` | `UtcClockProvider`（来自 Timing 组件） | Singleton |
| `IAuditPropertySetter` | `AuditPropertySetter` | Scoped |
| `IDataFilter` | `DataFilter`（非泛型） | Singleton |
| `IDataFilter<>` | `DataFilter<>`（泛型） | Scoped |
| 各 `IRepository<TEntity>` / `IRepository<TEntity,TKey>` | `EfCoreRepository<...>` | Scoped |

同时内部调用 `AddUnitOfWork()` 与 `AddUnitOfWorkEfCore()` 接入工作单元。仓储注册通过 `OnServiceRegistered` 钩子在容器构建时自动扫描所有已注册的 `DbContext` 的 `DbSet<>` 属性完成——业务侧无需逐个手工注册仓储。

> `IPermissionChecker`、`IPermissionDefinitionProvider` 的具体实现由业务项目自行注册，框架只提供抽象与 `PermissionDefinitionManager`。

## 使用

### 定义实体（Domain）

继承审计实体基类即可获得创建/修改/删除审计字段（由拦截器自动填充，业务代码不手动赋值）：

```csharp
public class Order : FullAuditedEntity<Guid>   // 含创建+修改+软删除审计
{
    public string CustomerName { get; private set; } = default!;

    protected Order() { }

    public Order(Guid id, string customerName) : base(id)
    {
        CustomerName = customerName;
        AddLocalEvent(new OrderCreatedEvent(id));  // 领域事件随 SaveChanges 自动发布
    }
}
```

### 通过仓储读写（注入自动注册的 IRepository）

```csharp
public class OrderManager(IRepository<Order, Guid> orderRepository)
{
    public async Task<Order?> GetAsync(Guid id)
        => await orderRepository.GetByIdAsync(id);

    public async Task<Order> CreateAsync(Order order)
        => await orderRepository.InsertAsync(order);

    public async Task<IEnumerable<Order>> SearchAsync(string keyword)
        => await orderRepository.GetListAsync(o => o.CustomerName.Contains(keyword));
}
```

### 应用服务与分页 DTO（Application.Contracts + Application）

```csharp
public class OrderAppService(IRepository<Order, Guid> repo, IObjectMapper mapper)
    : BaseAppService, IAppService
{
    public async Task<PagedResultDto<OrderDto>> GetListAsync(PagedRequestDto input)
    {
        var total = await repo.CountAsync();
        var items = await repo.GetListAsync();
        var source = new PagedResultDto<Order>(total, items);
        return mapper.MapPagedResult<Order, OrderDto>(source);  // 分页整体映射
    }
}
```

### 声明并校验权限（Application）

```csharp
[Permission("Orders.View")]                          // 单权限
[Permission("Orders.View", "Orders.Edit")]           // 多权限，默认任一即可
[Permission("Orders.View", "Orders.Edit", RequireAll = true)]  // 要求全部
public class OrderController : ControllerBase { }
```

### 临时禁用软删除过滤器（Domain）

```csharp
public class OrderReportService(IDataFilter dataFilter, IRepository<Order, Guid> repo)
{
    public async Task<IEnumerable<Order>> GetAllIncludingDeletedAsync()
    {
        using (dataFilter.Disable<ISoftDelete>())   // 作用域内查询包含已软删除数据
        {
            return await repo.GetListAsync();
        }
    }
}
```

## 接口参考

### Leistd.Ddd.Domain

实体（`Leistd.Ddd.Domain.Entities`）：

| 成员 | 说明 |
| --- | --- |
| `IEntity` / `IEntity<TKey>` | 实体标记接口；`GetKeys()` 返回主键数组，泛型版含 `Id` |
| `Entity` / `Entity<TKey>` | 实体基类；`AddLocalEvent(ILocalEvent)` 登记领域事件，由保存时统一发布 |

审计接口与实体（`Leistd.Ddd.Domain.Entities.Auditing`）：

| 成员 | 说明 |
| --- | --- |
| `IHasCreationTime` / `ICreationAuditedObject` | 创建时间 / 创建者 ID |
| `IHasModificationTime` / `IModificationAuditedObject` | 最后修改时间 / 修改者 ID |
| `IHasDeletionTime` / `ISoftDelete` / `IDeletionAuditedObject` | 删除时间 / 软删除标志 / 删除者 ID |
| `IFullAuditedObject` | 聚合上述三组创建+修改+删除审计 |
| `CreationAuditedEntity[<TKey>]` | 创建审计实体基类 |
| `ModificationAuditedEntity[<TKey>]` | 创建+修改审计实体基类 |
| `DeletionAuditedEntity[<TKey>]` | 创建+修改+软删除审计实体基类 |
| `FullAuditedEntity[<TKey>]` | 完整审计实体基类（最常用） |

仓储（`Leistd.Ddd.Domain.Repositories`）：

| 成员 | 说明 |
| --- | --- |
| `IRepository<TEntity>` | 无主键仓储；增删改查、`GetQueryableAsync`、`CountAsync`、`AnyAsync` 等 |
| `IRepository<TEntity, TKey>` | 带主键仓储，额外提供 `GetByIdAsync`、按 Id 删除 |
| `GetOneAsync(predicate)` | 单条查询（`SingleOrDefault` 语义）；多于一条抛异常，无匹配返回 `null` |
| `GetFirstAsync(predicate, orderBy?)` | 取首条，找不到返回 `null` |
| `GetListAsync(predicate?)` | 列表查询，谓词为 `null` 表示全量 |
| `GetByIdAsync(id)` | 按主键查找，找不到返回 `null` |
| `BaseRepository<TEntity>` | 仓储抽象基类，供 Infrastructure 实现 |
| `IQueryableAsyncExecuter` | 将 `IQueryable` 的异步执行（`ToListAsync` 等）从领域层解耦到基础设施 |

其它（`Leistd.Ddd.Domain.Auditing` / `Leistd.Ddd.Domain.DataFilters`）：

| 成员 | 说明 |
| --- | --- |
| `IAuditPropertySetter` | 设置创建/修改/删除审计属性；参数用 `object` 以避免领域层依赖 EF Core |
| `IDataFilter` / `IDataFilter<TFilter>` | 数据过滤器开关；`Disable()` / `Enable()` 返回 `IDisposable`，离开作用域恢复 |

### Leistd.Ddd.Application.Contracts

`Leistd.Ddd.Application.Contracts.Dtos` / `.AppService` / `.Extensions`：

| 成员 | 说明 |
| --- | --- |
| `IAppService` | 应用服务标记接口 |
| `EntityDto<TKey>` / `EntityDto` | DTO 基类记录；`EntityDto` 默认 `Guid` 主键 |
| `PagedRequestDto` | 分页请求：`Offset`、`Limit`（默认 10）、`Sorting` |
| `PagedResultDto<T>` | 分页结果：`TotalCount` + 只读 `Items` |
| `ObjectMapperExtensions.MapPagedResult<TSource,TDest>` | 对 `PagedResultDto` 整体做条目映射；入参为 `null` 抛 `ArgumentNullException` |

### Leistd.Ddd.Application

`Leistd.Ddd.Application.AppService` / `.Permission`：

| 成员 | 说明 |
| --- | --- |
| `BaseAppService` | 应用服务基类 |
| `IApplicationService` | 应用服务标记接口（位于 `Leistd.Ddd.Application.Services` 命名空间） |
| `IPermissionChecker` | 权限检查；`IsGrantedAsync` 单/多权限重载，返回 `bool` 或 `MultiplePermissionGrantResult` |
| `MultiplePermissionGrantResult` | 多权限结果；`AllGranted` / `AnyGranted` |
| `IPermissionDefinitionProvider` | 业务侧实现以声明权限（`Define(context)`） |
| `IPermissionDefinitionContext` / `IPermissionGroupDefinition` / `IPermissionDefinition` | 权限定义上下文/组/项 |
| `IPermissionDefinitionManager` | 加载并查询所有权限定义（`GetOrNull` / `GetAll`） |
| `PermissionAttribute` | 权限校验特性，继承 `AuthorizeAttribute`；`RequireAll` 控制全部/任一 |

### Leistd.Ddd.Infrastructure

| 成员 | 说明 |
| --- | --- |
| `AddDddInfrastructure(configureUnitOfWork?)` | 注册基础设施的唯一 DI 入口 |
| `BaseDbContext` | DbContext 基类，`OnModelCreating` 自动套用全局软删除过滤器 |
| `EfCoreRepository<TDbContext,TEntity[,TKey]>` | 基于 EF Core 的仓储实现，由 DI 自动注册 |
| `ModelBuilderExtensions.ConfigureByConvention<TEntity>` | 按约定配置审计字段（审计者 ID 长度 64） |
| `ModelBuilderExtensions.ApplyGlobalFilters<TInterface>` | 为实现某接口的实体批量应用全局查询过滤器 |
| `RepositoryExtensions.GetQueryIncludingAsync` | 带 `Include` 导航属性的查询扩展 |

## 实现行为

### Leistd.Ddd.Infrastructure（EF Core 落地）

- **仓储自动注册**：`AddDddInfrastructure` 通过 `OnServiceRegistered` 钩子，扫描每个非抽象 `DbContext` 的所有 `DbSet<>`，为实体类型注册 `IRepository<TEntity>`（Scoped）；若实体实现 `IEntity<TKey>`，额外注册 `IRepository<TEntity,TKey>`。
- **智能保存**：`EfCoreRepository` 的写操作调用 `SaveChangesIfNeededAsync`——若当前处于 UnitOfWork（`Uow.Current != null`）内则**不立即保存**，交由工作单元统一提交；否则立即 `SaveChangesAsync`。`GetByIdAsync` 使用 EF `FindAsync`，可命中已追踪实体（`TKey` 约束为 `IEquatable<TKey>`）。
- **自动审计**：`AuditSaveChangesInterceptor` 在 `SavingChanges` 阶段按实体状态（Added/Modified/Deleted）调用 `IAuditPropertySetter`。删除操作若实体实现 `ISoftDelete`，会将状态由 `Deleted` 改为 `Modified` 并填充删除审计——即**物理删除自动转为逻辑删除**。`AuditPropertySetter` 通过 `EntityEntry` API 直接写属性（不反射），时间取自 `IClock`、用户取自 `ICurrentUser`；创建者/删除者 ID 仅在当前用户存在且原值为空时写入。
- **软删除过滤**：`BaseDbContext.OnModelCreating` 调用 `ApplyGlobalFilters<ISoftDelete>`，查询默认排除已删除数据；`IsSoftDeleteFilterEnabled` 读取 `IDataFilter.IsEnabled<ISoftDelete>()`（无 `IServiceProvider` 时默认启用），可在作用域内 `Disable()` 临时关闭。
- **本地事件发布**：`LocalEventSaveChangesInterceptor` 在 `SavedChanges` 之后收集实体的本地事件——若有 UnitOfWork 则加入其待发布队列（随事务提交发布），否则直接经 `ILocalEventBus` 发布。注意同步 `SaveChanges` 路径会记录 sync-over-async 警告，建议始终用 `SaveChangesAsync`。

### Leistd.Ddd.Domain（数据过滤器）

- `DataFilter<TFilter>` 用 `AsyncLocal` + 栈保存启用状态，支持嵌套 `Disable`/`Enable`，**默认启用**；返回的 `IDisposable` 在 `Dispose` 时弹栈恢复。非泛型 `DataFilter` 按类型缓存并委托到对应泛型实例。

## 配置项 / Options

本分组未提供独立的 Options 类。`AddDddInfrastructure` 接受一个 `Action<UnitOfWorkOptions>` 回调，用于配置所依赖的[工作单元](../components/unit-of-work.md)组件的选项；DDD 层自身（审计、软删除、仓储）当前无可配置项。

## 注意事项

- 软删除是**默认行为**：对实现 `ISoftDelete` 的实体调用 `DeleteAsync`，记录不会被物理删除，而是置 `IsDeleted = true`；查询默认看不到这些记录，需要时用 `IDataFilter.Disable<ISoftDelete>()` 临时关闭过滤器。软删除依赖两处协作——`AuditSaveChangesInterceptor` 转换删除状态、`BaseDbContext` 的全局过滤器在查询时排除已删数据。
- 审计字段（创建/修改/删除时间与操作者）由拦截器自动填充，业务代码**不要手动赋值**；这些属性在基类中是 `protected set`。脱离基础设施层（如纯领域单测）不会自动写入审计值。
- 仓储写操作在 UnitOfWork 内不会立即落库——依赖工作单元提交；脱离 UnitOfWork 调用时才即时 `SaveChanges`。
- 仓储注册依赖 `DbContext` 暴露 `DbSet<>` 属性；未声明为 `DbSet<>` 的实体不会被自动注册仓储。
- 审计者 ID 列经 `ConfigureByConvention` 约定为最大长度 **64**；请在实体配置中调用该扩展以生成正确的列约束。
- 应用服务标记接口有两个：契约层 `Leistd.Ddd.Application.Contracts.AppService.IAppService` 与领域层 `Leistd.Ddd.Application.Services.IApplicationService`，二者均为空标记接口。
- 本地事件请通过实体的 `AddLocalEvent` 登记，并使用 `SaveChangesAsync` 触发发布，避免同步路径的线程饥饿警告。

## 相关

- [组件总览](./README.md)
- [工作单元](../components/unit-of-work.md)
- [事件总线](../components/event-bus.md)
- [对象映射](../components/object-mapping.md)
- [安全与当前用户](../components/security.md)
- [依赖注入](../components/dependency-injection.md)
