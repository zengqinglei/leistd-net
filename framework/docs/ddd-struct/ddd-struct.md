# DDD 四层基座

构建中大型业务系统时，最难统一的不是某个框架，而是**分层约定**：实体基类放哪、仓储接口长什么样、应用服务怎么组织、分页结果怎么返回、领域事件何时发布、软删除怎么落地。各团队各写一套，代码就难以复用与维护。

`ddd-struct` 提供一套按 **Domain / Application.Contracts / Application / Infrastructure** 四层划分的 DDD 基础类型：Domain 定义实体基类、仓储抽象与数据过滤器；Application.Contracts 提供应用服务标记接口、DTO 基类与分页约定；Application 提供应用服务基类与分页映射扩展；Infrastructure 基于 EF Core 落地仓储、提供审计/本地事件拦截器、软删除与租户隔离全局过滤。业务项目继承这些基类、调用 `AddDddInfrastructure()` 获得仓储自动注册与全局过滤；**新增实体的环境值（`CreatorId` / `CreationTime` / `TenantId`）由 `BaseDbContext` 在实体进入跟踪时落定，修改与删除审计及领域事件发布依赖两个 SaveChanges 拦截器，需在配置 DbContext 时显式挂载**（见下方「挂载拦截器」）——这一步不可省略，否则修改/删除审计不填充、领域事件不发布、软删除不由删转改。

## 何时使用

| 场景 | 引用的包 |
| --- | --- |
| 定义领域实体、仓储接口、数据过滤器（领域层代码） | `Leistd.Ddd.Domain` |
| 定义应用服务契约、DTO、分页请求/结果 | `Leistd.Ddd.Application.Contracts` |
| 编写应用服务、做分页结果的整体映射 | `Leistd.Ddd.Application` |
| 用 EF Core 落地仓储、启用软删除全局过滤与本地事件发布、注册基础设施 | `Leistd.Ddd.Infrastructure` |

> 四层按依赖方向引用：Application 依赖 Application.Contracts 与 Domain，Infrastructure 依赖 Domain（并引用 `Leistd.Auditing.EntityFrameworkCore`、`Leistd.UnitOfWork.EfCore` 接入审计与工作单元）。业务项目通常每层各建一个工程，分别引用对应的 `Leistd.Ddd.*` 包。

## 安装

| 包 | 层 | 提供什么 |
| --- | --- | --- |
| `Leistd.Ddd.Domain` | Domain | `Entity`/`Entity<TKey>` 实体基类、审计实体基类、`IRepository<TEntity[,TKey]>` 仓储接口、`IDataFilter[<TFilter>]` 数据过滤器 |
| `Leistd.Ddd.Application.Contracts` | Application.Contracts | `IAppService` 标记接口、`EntityDto[<TKey>]`、`PagedRequestDto`、`PagedResultDto<T>` |
| `Leistd.Ddd.Application` | Application | `BaseAppService` 应用服务基类、`IApplicationService` 标记接口、`ObjectMapperExtensions.MapPagedResult` 分页映射扩展 |
| `Leistd.Ddd.Infrastructure` | Infrastructure | `AddDddInfrastructure()` DI 入口、`EfCoreRepository<...>`、`BaseDbContext`、`LocalEventSaveChangesInterceptor`、`ModelBuilderExtensions` |

```bash
dotnet add package Leistd.Ddd.Domain
dotnet add package Leistd.Ddd.Application.Contracts
dotnet add package Leistd.Ddd.Application
dotnet add package Leistd.Ddd.Infrastructure
```

> 本仓库的模板项目通过中央包管理（CPM）统一版本，添加时无需写版本号。

> **本文档覆盖四层整体基座**（4 个 `Leistd.Ddd.*` 包共用这一份，讲清四层如何协作）。若你只安装了其中的子集，按上表对应你装的包阅读相应内容即可——特别地，**「注册基础设施」「挂载拦截器」「软删除全局过滤」「本地事件发布」等运行时装配内容属 `Leistd.Ddd.Infrastructure` 专属**（并需 `Leistd.Auditing.EntityFrameworkCore`），只装 Domain/Application(.Contracts) 的项目用不到这些节。各节标题已标注所属包。

## 使用

### 定义实体（Domain）

继承审计实体基类获得创建/修改/软删除审计字段（框架自动填充，业务代码不手动赋值：创建审计由 `BaseDbContext` 在实体进入跟踪时写入，修改/删除审计由 `AuditSaveChangesInterceptor` 在保存时写入——后者前提是已按「挂载拦截器」小节挂到 DbContext）；通过 `AddLocalEvent` 登记领域事件：

```csharp
public class Order : FullAuditedEntity<Guid>   // 创建+修改+软删除审计
{
    public string CustomerName { get; private set; } = default!;

    private Order() { }

    public Order(Guid id, string customerName) : base(id)
    {
        CustomerName = customerName;
        AddLocalEvent(new OrderCreatedEvent(id));  // 随 SaveChanges 自动发布
    }
}
```

### 通过仓储读写（IRepository 接口属 Domain；自动注册与 EF 实现属 Leistd.Ddd.Infrastructure）

```csharp
public class OrderManager(IRepository<Order, Guid> orderRepository)
{
    public async Task<Order?> GetAsync(Guid id, CancellationToken ct = default)
        => await orderRepository.GetByIdAsync(id, ct);

    public async Task<Order> CreateAsync(Order order, CancellationToken ct = default)
        => await orderRepository.InsertAsync(order, ct);

    public async Task<IEnumerable<Order>> SearchAsync(string keyword, CancellationToken ct = default)
        => await orderRepository.GetListAsync(o => o.CustomerName.Contains(keyword), ct);
}
```

### 应用服务与分页结果（Application.Contracts + Application）

```csharp
public class OrderAppService(IRepository<Order, Guid> repo, IObjectMapper mapper)
    : BaseAppService, IAppService
{
    public async Task<PagedResultDto<OrderDto>> GetListAsync(PagedRequestDto input, CancellationToken ct = default)
    {
        var total = await repo.CountAsync(cancellationToken: ct);
        var items = await repo.GetListAsync(cancellationToken: ct);
        var source = new PagedResultDto<Order>(total, items);
        return mapper.MapPagedResult<Order, OrderDto>(source);  // 分页整体映射
    }
}
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

### 注册基础设施（Program.cs · Leistd.Ddd.Infrastructure）

```csharp
// 注册 DDD 基础设施：UnitOfWork + EF Core 支持 + 自动仓储注册 + DataFilter
builder.Services.AddDddInfrastructure();

// 可选：配置 UnitOfWork 选项
builder.Services.AddDddInfrastructure(uow =>
{
    // 在此配置 UnitOfWorkOptions
});
```

### 挂载拦截器（必需 · Leistd.Ddd.Infrastructure + Leistd.Auditing.EntityFrameworkCore）

`AddDddInfrastructure()` 只把 `AuditSaveChangesInterceptor` 与 `LocalEventSaveChangesInterceptor` 注册为**可解析的服务**，**不会**自动挂到任何 DbContext 上。必须在配置 DbContext 时从容器解析并显式 `AddInterceptors`：

```csharp
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseNpgsql(connectionString);   // 或其它 provider

    // ⚠️ 必须显式挂载这两个拦截器，否则：
    //   - 修改/删除审计字段（LastModifierId、DeleterId…）不会填充
    //   - 实体 AddLocalEvent 登记的领域事件不会随保存发布
    //   - 软删除不会由物理删除自动转为逻辑删除
    // 注：创建审计（CreationTime/CreatorId）不在拦截器里，由 BaseDbContext
    //     在实体进入跟踪时落定——但要求把 IServiceProvider 传给它的构造函数
    options.AddInterceptors(
        sp.GetRequiredService<AuditSaveChangesInterceptor>(),
        sp.GetRequiredService<LocalEventSaveChangesInterceptor>());
});
```

> 若领域事件"未触发订阅"、修改/删除审计全为空，几乎都是漏了这一步。拦截器由消费方挂载是 EF Core 的推荐做法（框架不代持/代挂业务的 DbContext）。若只有**创建**审计为空，则是 DbContext 没拿到 `IServiceProvider`。

### DbContext 基类与全局过滤器运行时开关（Leistd.Ddd.Infrastructure）

派生 DbContext 继承 `BaseDbContext`，**改写 `ConfigureModel` 而不是 `OnModelCreating`**（后者已封闭），即获得两个 **EF 10 命名全局查询过滤器**：软删除（`SoftDeleteFilterName`，作用于 `ISoftDelete` 实体）与租户隔离（`MultiTenantFilterName`，作用于 `IMultiTenant` 实体，见[多租户](../components/multi-tenancy.md)）。同一实体同时命中两个接口时两个过滤器 **AND 叠加**、可用 `IDataFilter` 独立开关；过滤器表达式捕获 DbContext 实例属性，EF 将其参数化并在每次查询时重估——`ICurrentTenant.Change()` 与 `IDataFilter` 开关即时生效，无需重建模型。实体不实现对应接口时过滤器不作用于它，零成本。

若需在运行时用 `IDataFilter.Disable<ISoftDelete>()` / `Disable<IMultiTenant>()` **临时关闭过滤**（见下文），DbContext 必须选用**接收 `IServiceProvider` 的构造函数重载**并把它传给 `base`：

```csharp
public class AppDbContext(DbContextOptions<AppDbContext> options, IServiceProvider serviceProvider)
    : BaseDbContext(options, serviceProvider)   // 传入 sp，Disable<ISoftDelete>() 才对本 DbContext 生效
{
    public DbSet<Order> Orders => Set<Order>();

    // 改写 ConfigureModel 而非 OnModelCreating，也不调用 base：
    // 基类保证全局过滤器在本方法之后套用
    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(b =>
        {
            b.ConfigureByConvention();           // 按约定配置审计者 ID 列长度（HasMaxLength(64)）
            // …其余 Fluent 配置
        });

        modelBuilder.ConfigureAuthorization();   // 组件的实体配置也放这里，同样被过滤器覆盖
    }
}
```

> **为什么封闭 `OnModelCreating`**：全局过滤器只能作用于当时已在模型中的实体类型。若在派生类配置之前套用，那些经 `ApplyConfiguration` 才进入模型、又**没有 `DbSet` 声明**的实体（各组件的版本表就是这种形态）会完全逃过软删除与租户隔离——那是静默的越权缺口。把顺序交给基类，覆盖完整性就不再依赖派生类的书写习惯。

> 若使用**不带 `IServiceProvider`** 的构造函数，`IsSoftDeleteFilterEnabled` 恒为 `true`、`CurrentTenantId` 恒为 `null`（宿主视角），`Disable<ISoftDelete>()` / `Disable<IMultiTenant>()` 与 `ICurrentTenant.Change()` 对该 DbContext 均无效，**创建审计也不会填充**。

#### 新增实体的环境值：在"进入跟踪"时落定

`BaseDbContext` 订阅 `ChangeTracker.Tracked` 与 `ChangeTracker.StateChanged`，对 `Added` 状态的实体落两样值：

| 值 | 条件 |
| --- | --- |
| `TenantId`（`IMultiTenant`） | 当前有租户上下文，且实体的 `TenantId` 仍为 null |
| `CreationTime` / `CreatorId` | 经 `IAuditPropertySetter`，值仍为空时写入 |

**为什么不在保存时。** 仓储在工作单元内不立即保存，新增与保存之间可以跨越 `ICurrentTenant.Change` 或 `ICurrentPrincipalAccessor.Change` 的边界。在保存时刻取环境值，会把该租户的数据**静默落成宿主行**（该租户自己看不见、宿主管理员看得见），创建者落成外层主体——两者都不报错。进入跟踪的时刻才是"这条数据属于谁、由谁创建"的语义时刻；落盘时机是基础设施的调度结果，把身份绑在它上面等于让这些值随事务边界漂移。

**为什么两个事件都订阅。** `Tracked` 只在实体首次进入跟踪时触发，看不到"查询出来（`Unchanged`）之后才被改成 `Added`"的 upsert 类写法；那种迁移只有 `StateChanged` 能捕获。

**为什么不会覆盖查询出来的实体。** 三层护栏：`FromQuery` 的实体直接跳过、状态必须是 `Added`（查询物化的结果是 `Unchanged`，永远不是 `Added`）、值已有则不动（种子、导入、迁移显式赋过的值保留）。

> 与 Volo.ABP 的差异：ABP 的创建审计同样落在进入跟踪时（`AbpDbContext.ChangeTracker_Tracked` → `ApplyAbpConceptsForAddedEntity`），本框架与之一致；`TenantId` 则 ABP 落在 `Entity` 基类构造函数里（更早一步，靠反射写私有 setter）。本框架选择保持领域实体基类零环境依赖，代价是"作用域内 `new`、作用域外 `Add`"这种跨作用域持有实体的写法拿不到租户值——那本身是应当避免的写法。

## 接口参考

### Leistd.Ddd.Domain

实体（`Leistd.Ddd.Domain.Entities`）：

| 成员 | 说明 |
| --- | --- |
| `IEntity` | 实体标记接口；`GetKeys()` 返回主键数组 |
| `IEntity<TKey>` | 带主键的实体接口，继承 `IEntity`，含 `Id` |
| `Entity` | 实体抽象基类（`[Serializable]`），实现 `IEntity`；`AddLocalEvent(ILocalEvent)`（`protected`）登记本地事件；`GetLocalEvents()` 返回只读事件集合；`ClearLocalEvents()` 清空事件——后两者标注仅供 `BaseDbContext`/拦截器使用。**未重写 `Equals`/`GetHashCode`：实体走引用相等，若需按主键值相等由业务自行实现** |
| `Entity<TKey>` | 带主键的实体基类，继承 `Entity` 并实现 `IEntity<TKey>`；`Id` 为 `virtual`、`protected set`；`GetKeys()` 返回 `[Id]` |

审计实体（`Leistd.Ddd.Domain.Entities.Auditing`，审计接口 `ICreationAuditedObject` 等来自 `Leistd.Auditing` 组件）：

| 成员 | 说明 |
| --- | --- |
| `CreationAuditedEntity[<TKey>]` | 实现 `ICreationAuditedObject`：`CreatorId`（`string?`）、`CreationTime` |
| `ModificationAuditedEntity[<TKey>]` | 继承 `CreationAuditedEntity[<TKey>]`，实现 `IModificationAuditedObject`：`LastModifierId`（`string?`）、`LastModificationTime`（`DateTime?`） |
| `DeletionAuditedEntity[<TKey>]` | 继承 `ModificationAuditedEntity[<TKey>]`，实现 `IDeletionAuditedObject`：`IsDeleted`、`DeleterId`（`string?`）、`DeletionTime`（`DateTime?`） |
| `FullAuditedEntity[<TKey>]` | 继承 `DeletionAuditedEntity[<TKey>]`，实现 `IFullAuditedObject`（聚合创建+修改+删除审计），无新增成员，最常用 |

> 上述属性均为 `virtual`、`protected set`——业务代码不能直接赋值，由 Infrastructure 层的审计拦截器写入。

仓储（`Leistd.Ddd.Domain.Repositories`）：

| 成员 | 说明 |
| --- | --- |
| `IRepository` | 空标记接口，所有仓储的公共基接口 |
| `IRepository<TEntity>` | 无主键仓储：`GetQueryableAsync`、`GetOneAsync`（谓词命中多条抛异常）、`GetFirstAsync`（可选 `orderBy`）、`GetListAsync`（谓词为 `null` 表示全量）、`CountAsync`/`AnyAsync`（谓词可选）、`InsertAsync`/`InsertManyAsync`、`UpdateAsync`/`UpdateManyAsync`、`DeleteAsync`/`DeleteManyAsync`（含按谓词批量删除重载） |
| `IRepository<TEntity, TKey>` | 继承 `IRepository<TEntity>`，额外提供 `GetByIdAsync(id)`、按 `TKey` 的 `DeleteAsync`/`DeleteManyAsync` |
| `BaseRepository<TEntity>` | 实现 `IRepository<TEntity>` 的抽象基类，方法均为 `abstract`，供 Infrastructure 实现 |
| `IQueryableAsyncExecuter` | 把 `IQueryable` 的异步执行（`ToListAsync`/`CountAsync`/`LongCountAsync`/`FirstOrDefaultAsync`/`SingleOrDefaultAsync`/`AnyAsync`）从领域层接口中解耦出来，避免领域层直接依赖 EF Core |

数据过滤器（`Leistd.Ddd.Domain.DataFilters`）：

| 成员 | 说明 |
| --- | --- |
| `IDataFilter<TFilter>` | 单一过滤器开关：`Disable()`/`Enable()` 返回 `IDisposable`（释放时恢复状态）；`IsEnabled` 只读 |
| `IDataFilter` | 非泛型版本，按 `TFilter` 类型动态操作，方法签名与泛型版一致但带类型参数 |

### Leistd.Ddd.Application.Contracts

| 成员 | 说明 |
| --- | --- |
| `IAppService`（`Leistd.Ddd.Application.Contracts.AppService`） | 应用服务空标记接口 |
| `EntityDto<TKey>`（`Leistd.Ddd.Application.Contracts.Dtos`） | DTO 抽象 record 基类，`required TKey Id { get; init; }` |
| `EntityDto`（同上） | 继承 `EntityDto<Guid>`，即默认 `Guid` 主键的 DTO 基类 |
| `PagedRequestDto`（同上） | 分页请求 record：`Offset`（默认 0）、`Limit`（默认 10，常量 `DefaultLimit`）、`Sorting`（`string?`） |
| `PagedResultDto<T>`（同上） | 分页结果 record：`TotalCount`（`long`）+ 只读 `Items`（`IReadOnlyList<T>`）；提供接受 `IEnumerable<T>` 的构造函数，**`items` 为 `null` 时不抛异常，而是回退为 `ReadOnlyCollection<T>.Empty`** |

### Leistd.Ddd.Application

| 成员 | 说明 |
| --- | --- |
| `BaseAppService`（`Leistd.Ddd.Application.AppService`） | 应用服务抽象基类，当前无成员 |
| `IApplicationService`（`Leistd.Ddd.Application.Services`） | 应用服务空标记接口（与 Contracts 层的 `IAppService` 是两个不同的空标记接口） |
| `ObjectMapperExtensions.MapPagedResult<TSource,TDestination>`（`Leistd.Ddd.Application.Extensions`） | 对 `PagedResultDto<TSource>` 做整体映射：内部调用 `IObjectMapper` 的 `MapList` 映射 `Items`，`TotalCount` 原样带过；`mapper` 或 `pagedSource` 为 `null` 时抛 `ArgumentNullException` |

### Leistd.Ddd.Infrastructure

| 成员 | 说明 |
| --- | --- |
| `AddDddInfrastructure(configureUnitOfWork?)`（`Leistd.Ddd.Infrastructure.DependencyInjection`） | 注册基础设施的唯一 DI 入口 |
| `BaseDbContext`（`Leistd.Ddd.Infrastructure.Persistence`） | DbContext 抽象基类，`OnModelCreating` 自动为 `ISoftDelete` 套用全局查询过滤器 |
| `EfCoreRepository<TDbContext, TEntity>`（`Leistd.Ddd.Infrastructure.Persistence.Repositories`） | 继承 `BaseRepository<TEntity>` 的 EF Core 仓储实现，写操作后调用 `SaveChangesIfNeededAsync` |
| `EfCoreRepository<TDbContext, TEntity, TKey>` | 继承上者并实现 `IRepository<TEntity, TKey>`；额外提供 `GetByIdAsync`（过滤查询，见实现行为）、按 `TKey` 的 `DeleteAsync`/`DeleteManyAsync`；`TKey` 约束为 `IEquatable<TKey>` |
| `EfCoreQueryableAsyncExecuter`（同命名空间） | `IQueryableAsyncExecuter` 的 EF Core 实现，直接转调 `EntityFrameworkQueryableExtensions` 对应方法 |
| `LocalEventSaveChangesInterceptor`（`Leistd.Ddd.Infrastructure.EventBus`） | 继承 `SaveChangesInterceptor`，见[实现行为](#实现行为) |
| `ModelBuilderExtensions.ConfigureByConvention<TEntity>`（`Leistd.Ddd.Infrastructure.Persistence.Extensions`） | 按约定为实现 `ICreationAuditedObject`/`IModificationAuditedObject`/`IDeletionAuditedObject` 的实体配置对应审计者 ID 列的最大长度 |
| `ModelBuilderExtensions.ApplyGlobalFilters<TInterface>` | 为所有实现 `TInterface` 的**根实体类型**（`BaseType == null`，不含继承实体）批量应用**命名**全局查询过滤器（首参为过滤器名；不同名称在同一实体上 AND 叠加，同名后写覆盖先写） |
| `RepositoryExtensions.GetQueryIncludingAsync<TEntity,TKey>`（`Leistd.Ddd.Infrastructure.Persistence.Repositories`） | 在 `IRepository<TEntity,TKey>.GetQueryableAsync` 基础上按传入的属性选择器依次 `Include` 导航属性 |

## 实现行为

### Leistd.Ddd.Infrastructure（EF Core 落地）

- **仓储自动注册**：`AddDddInfrastructure` 通过 `OnServiceRegistered` 钩子，在容器构建时对每个非抽象、非 `DbContext` 本身的已注册 `DbContext` 类型，反射扫描其所有 `DbSet<>` 属性，对属性对应的实体类型注册 `IRepository<TEntity>`（Scoped，实现 `EfCoreRepository<TDbContext,TEntity>`）；若实体实现 `IEntity<TKey>`，额外注册 `IRepository<TEntity,TKey>`（实现 `EfCoreRepository<TDbContext,TEntity,TKey>`）。业务侧无需逐个手工注册仓储。
- **写操作的智能保存**：`EfCoreRepository<TDbContext,TEntity>` 的所有写方法（`InsertAsync`/`InsertManyAsync`/`UpdateAsync`/`UpdateManyAsync`/`DeleteAsync`/`DeleteManyAsync`）在完成 EF 操作后统一调用 `protected SaveChangesIfNeededAsync`：**若 `Uow.Current != null`（当前处于 UnitOfWork 内）则直接返回、不立即保存**，交由工作单元统一提交；**否则立即 `await dbContext.SaveChangesAsync()`**。`GetByIdAsync` 走 `FirstOrDefaultAsync` 过滤查询而非 `FindAsync`——后者绕过全局查询过滤器，会让按 Id 的读取越过软删除与租户隔离边界；因此跨租户或已软删的 Id 一律返回 `null`。
- **本地事件收集与发布**（`LocalEventSaveChangesInterceptor`）：
  - **收集**发生在 `SavingChanges`/`SavingChangesAsync`（保存前），此时实体状态仍为 `Added`/`Modified`/`Deleted`——从 `ChangeTracker.Entries<Entity>()` 中筛出这三种状态的实体、调用其 `GetLocalEvents()` 收集事件后立即 `ClearLocalEvents()`，按 `DbContext` 实例暂存到 `ConditionalWeakTable`（避免持有 `DbContext` 引用导致泄漏，天然隔离并发的不同 `DbContext`）。若在保存后（`SavedChanges`）才收集，EF Core 此时已把实体状态置为 `Unchanged`，会被状态过滤器漏掉、导致事件丢失——这正是该拦截器把收集和发布拆成两个阶段的原因。
  - **发布**发生在 `SavedChanges`/`SavedChangesAsync`（保存成功后）：取出该 `DbContext` 暂存的事件列表；若存在 `IUnitOfWorkManager.Current`（当前处于 UnitOfWork），调用 `currentUow.AddPendingEvents(localEvents)` 加入工作单元的待发布队列（随事务提交发布）；否则（无 UnitOfWork）直接经 `ILocalEventBus.PublishAsync` 逐个发布——异步路径 `await` 逐个发布，同步路径（`SaveChanges` 而非 `SaveChangesAsync`）用 `GetAwaiter().GetResult()` 阻塞发布并记录一条 `LogWarning`（同步发布本地事件属于 sync-over-async 风险路径）。
  - **保存失败**（`SaveChangesFailed`/`SaveChangesFailedAsync`）会从暂存表中丢弃本次收集的事件，避免残留到下一次保存周期。
- **软删除与租户全局过滤**：`BaseDbContext.OnModelCreating` 以命名过滤器分别调用 `ApplyGlobalFilters<ISoftDelete>`（名 `SoftDelete`，表达式 `!IsSoftDeleteFilterEnabled || !e.IsDeleted`）与 `ApplyGlobalFilters<IMultiTenant>`（名 `MultiTenant`，表达式 `!IsMultiTenantFilterEnabled || e.TenantId == CurrentTenantId`）。过滤器被禁用时表达式恒为 `true`；租户过滤器启用且当前为宿主上下文（`CurrentTenantId == null`）时仅显示宿主行。开关读取 `IDataFilter`，`CurrentTenantId` 读取 `ICurrentTenant`（未注册或未传 `IServiceProvider` 时为宿主视角）。
- **修改/删除审计填充**（由 `Leistd.Auditing.EntityFrameworkCore` 的 `AuditSaveChangesInterceptor` 提供，Infrastructure 层通过 `AddAuditingEfCore()` 一并注册）：在 `SavingChanges` 阶段按 `ChangeTracker.Entries()` 的状态调用 `IAuditPropertySetter`——`Modified` 且实体是 `ISoftDelete { IsDeleted: true }` 时跳过（避免与删除审计重复设置）否则设置修改属性；`Deleted` 且实体实现 `ISoftDelete` 时，把状态由 `Deleted` 改为 `Modified` 并设置删除属性——即**物理删除自动转换为逻辑删除**。
- **创建审计不在拦截器里**：`Added` 状态由 `BaseDbContext` 在实体进入跟踪时处理，见下方「DbContext 基类」小节。

### Leistd.Ddd.Domain（数据过滤器）

- `DataFilter<TFilter>` 用 `AsyncLocal<FilterState>` 内含 `Stack<bool>` 保存启用状态，支持嵌套 `Disable`/`Enable`：栈为空时 `IsEnabled` 默认返回 `true`（默认启用）；`Disable()`/`Enable()` 入栈对应值并返回一个 `DisposeAction`，`Dispose()` 时出栈恢复上一层状态。非泛型 `DataFilter` 用 `ConcurrentDictionary<Type, object>` 按 `TFilter` 类型缓存并委托到对应的 `IDataFilter<TFilter>` 实例（通过 `IServiceProvider.GetRequiredService` 解析）。

## 注意事项

- **软删除是默认行为**：对实现 `ISoftDelete` 的实体调用 `DeleteAsync`，`AuditSaveChangesInterceptor` 会把物理删除转换为逻辑删除（`IsDeleted = true`），查询默认经全局过滤器看不到这些记录；需要时用 `IDataFilter.Disable<ISoftDelete>()` 在作用域内临时关闭过滤器。
- **拦截器必须显式挂载**：`AuditSaveChangesInterceptor` 与 `LocalEventSaveChangesInterceptor` 不会被 `AddDddInfrastructure()` 自动挂到 DbContext，必须在配置 DbContext 时 `AddInterceptors`（见「挂载拦截器」）；漏挂会导致审计不填充、领域事件不发布、软删除不转换，且**不报错、静默失效**。
- 审计字段（创建/修改/删除时间与操作者）均为 `virtual`、`protected set`，由框架自动填充（创建在进入跟踪时、修改/删除在保存时），业务代码不要手动赋值；脱离 Infrastructure 层（如纯领域单测直接 `new` 实体）不会写入审计值。
- 仓储写操作在 UnitOfWork 内**不会立即落库**——`Uow.Current != null` 时 `SaveChangesIfNeededAsync` 直接返回，依赖工作单元统一提交；脱离 UnitOfWork 调用仓储写方法时才会立即 `SaveChangesAsync`。
- 本地事件请通过实体的 `AddLocalEvent`（`protected`，只能在实体内部调用）登记；`GetLocalEvents()`/`ClearLocalEvents()` 仅供拦截器使用，业务代码不应直接调用。同步 `SaveChanges()` 发布本地事件会记录 sync-over-async 警告，建议始终使用 `SaveChangesAsync()`。
- 仓储自动注册依赖 `DbContext` 暴露 `public` 的 `DbSet<>` 属性；未声明为 `DbSet<>` 的实体不会被自动注册仓储。
- 全局查询过滤器（`ApplyGlobalFilters`）只应用于**根实体类型**（EF Core 模型中 `BaseType == null`），继承体系中的派生实体类型不会被单独处理。
- 应用服务标记接口有两个、彼此独立：Application.Contracts 层的 `Leistd.Ddd.Application.Contracts.AppService.IAppService` 与 Application 层的 `Leistd.Ddd.Application.Services.IApplicationService`，均为空标记接口，未见二者存在继承关系。
- `PagedResultDto<T>` 的 `IEnumerable<T>` 构造函数对 `null` 是**容错**的（回退为空集合），而 `ObjectMapperExtensions.MapPagedResult` / `MapList` 对 `null` 参数是**抛异常**的（`ArgumentNullException`）——两处 null 处理策略不同，使用时注意区分。

## 相关

- [组件总览](../components/README.md)
- [ddd-struct 索引](./README.md)
