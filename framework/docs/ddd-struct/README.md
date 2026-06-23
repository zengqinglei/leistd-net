# DDD 四层基础类型（`ddd-struct`）
> 提供 DDD 分层架构（Domain / Application.Contracts / Application / Infrastructure）的基础类型：实体与审计基类、仓储抽象、应用服务与 DTO、权限抽象，以及基于 EF Core 的仓储/审计/软删除/本地事件落地实现。

## 包

| 包 | 角色 | 何时引用 |
| --- | --- | --- |
| `Leistd.Ddd.Domain` | 领域层基础类型：`Entity`、审计接口/基类、`IRepository`、数据过滤器、审计属性设置器接口 | 编写领域实体、仓储接口与领域模型时 |
| `Leistd.Ddd.Application.Contracts` | 应用契约层：`IAppService` 标记接口、DTO（`EntityDto`/`PagedRequestDto`/`PagedResultDto`）、对象映射扩展 | 定义应用服务契约与对外 DTO 时 |
| `Leistd.Ddd.Application` | 应用层基类与权限抽象：`BaseAppService`、`IPermissionChecker`、权限定义体系、`[Permission]` 特性 | 实现应用服务、定义/校验权限时 |
| `Leistd.Ddd.Infrastructure` | 基础设施层：EF Core 仓储实现、审计/本地事件拦截器、`BaseDbContext`、DI 注册入口 | 接入 EF Core 持久化并启用仓储/审计/软删除时 |

## 核心抽象

### 实体（`Leistd.Ddd.Domain.Entities`）

```csharp
public abstract class Entity : IEntity            // 非泛型实体基类，含本地事件收集能力
public abstract class Entity<TKey> : Entity, IEntity<TKey>  // 带主键实体基类
```
- `object?[] GetKeys()`：返回实体主键数组；`Entity<TKey>` 返回 `[Id]`。
- `protected void AddLocalEvent(ILocalEvent @event)`：领域内添加待发布本地事件（事件类型来自 `Leistd.EventBus.Core`）。
- `IReadOnlyCollection<ILocalEvent> GetLocalEvents()` / `void ClearLocalEvents()`：供 `BaseDbContext`/拦截器读取与清空本地事件，业务代码一般不直接调用。

### 审计接口与基类（`Leistd.Ddd.Domain.Entities.Auditing`）

接口分层：`IHasCreationTime`/`ICreationAuditedObject`、`IHasModificationTime`/`IModificationAuditedObject`、`IHasDeletionTime`/`ISoftDelete`/`IDeletionAuditedObject`、`IFullAuditedObject`（三者聚合）。

```csharp
public abstract class CreationAuditedEntity<TKey>      // CreatorId, CreationTime
public abstract class ModificationAuditedEntity<TKey>  // 继承创建审计 + LastModifierId, LastModificationTime
public abstract class DeletionAuditedEntity<TKey>      // 继承修改审计 + IsDeleted, DeleterId, DeletionTime（软删除）
public abstract class FullAuditedEntity<TKey>          // 完整审计（创建+修改+删除）
```
每个基类均有无主键重载。审计属性为 `protected set`，由基础设施层拦截器自动填充，业务代码不直接赋值。

### 仓储（`Leistd.Ddd.Domain.Repositories`）

```csharp
public interface IRepository<TEntity> where TEntity : class, IEntity
```
- `Task<IQueryable<TEntity>> GetQueryableAsync(...)`：返回可组合查询。
- `Task<TEntity?> GetOneAsync(predicate, ...)`：`SingleOrDefault` 语义，无匹配返回 null，多个匹配抛异常。
- `Task<TEntity?> GetFirstAsync(predicate, orderBy?, ...)`：按排序取第一个，无匹配返回 null。
- `Task<IEnumerable<TEntity>> GetListAsync(predicate?, ...)`：predicate 为 null 时返回全部。
- `Task<long> CountAsync(...)` / `Task<bool> AnyAsync(...)`：计数与存在性判断。
- `InsertAsync` / `InsertManyAsync` / `UpdateAsync` / `UpdateManyAsync` / `DeleteAsync` / `DeleteManyAsync`（含按谓词删除）：增改删。

```csharp
public interface IRepository<TEntity, TKey> : IRepository<TEntity> where TEntity : class, IEntity<TKey>
```
- `Task<TEntity?> GetByIdAsync(TKey id, ...)`：按主键查找，找不到返回 null。
- `Task DeleteAsync(TKey id, ...)` / `Task DeleteManyAsync(IEnumerable<TKey> ids, ...)`：按主键删除。

`BaseRepository<TEntity>` 为非泛型接口方法的抽象基类。`IQueryableAsyncExecuter` 提供对任意 `IQueryable` 的异步执行（`ToListAsync`/`CountAsync`/`LongCountAsync`/`FirstOrDefaultAsync`/`SingleOrDefaultAsync`/`AnyAsync`），解耦 LINQ 异步算子与具体 ORM。

### 数据过滤器（`Leistd.Ddd.Domain.DataFilters`）

```csharp
public interface IDataFilter            // 非泛型，动态指定过滤器类型
public interface IDataFilter<TFilter> where TFilter : class
```
- `IDisposable Disable<TFilter>()` / `IDisposable Enable<TFilter>()`：临时切换过滤器开关，释放返回的 `IDisposable` 时恢复上一状态（基于 `AsyncLocal` 栈，支持嵌套）。
- `bool IsEnabled<TFilter>()`：查询当前是否启用，默认启用。典型用于 `ISoftDelete` 软删除过滤器。

### 审计属性设置器（`Leistd.Ddd.Domain.Auditing`）

```csharp
public interface IAuditPropertySetter
```
- `SetCreationProperties` / `SetModificationProperties` / `SetDeletionProperties(object entityEntry)`：设置创建/修改/删除审计属性。参数刻意为 `object`（基础设施层传入 EF Core `EntityEntry`），以避免领域层依赖 EF Core。

### 应用契约（`Leistd.Ddd.Application.Contracts`）

```csharp
public interface IAppService                                  // 应用服务标记接口
public abstract record EntityDto<TKey> { public required TKey Id { get; init; } }
public abstract record EntityDto : EntityDto<Guid>            // 默认 Guid 主键 DTO
public record PagedRequestDto { int Offset; int Limit = 10; string? Sorting; }  // 分页查询请求
public record PagedResultDto<T>(long TotalCount, IReadOnlyList<T> Items)        // 分页结果
```
- `IObjectMapper.MapPagedResult<TSource, TDestination>(PagedResultDto<TSource>)`（`ObjectMapperExtensions`）：在保留 `TotalCount` 的前提下映射分页项；mapper 或入参为 null 时抛 `ArgumentNullException`。

> 注意：应用服务标记接口有两个 —— 契约层 `Leistd.Ddd.Application.Contracts.AppService.IAppService` 与领域层 `Leistd.Ddd.Application.Services.IApplicationService`，二者均为空标记接口。

### 应用层与权限（`Leistd.Ddd.Application`）

```csharp
public abstract class BaseAppService           // 应用服务基类（当前为空，供继承扩展）
public interface IPermissionChecker
```
- `Task<bool> IsGrantedAsync(string name, ...)` / `IsGrantedAsync(ClaimsPrincipal?, string, ...)`：检查当前用户或指定主体是否拥有单个权限。
- `Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] names, ...)`：批量检查；结果含 `AllGranted` / `AnyGranted`。

```csharp
public interface IPermissionDefinitionProvider          // 业务实现以声明权限
public interface IPermissionDefinitionContext           // GetOrAddGroup / AddPermission / GetPermissionOrNull
public interface IPermissionDefinition                  // Name/DisplayName/Parent/Children/IsEnabled/AddChild
public interface IPermissionDefinitionManager           // GetOrNull(name) / GetAll()
```
- `PermissionDefinitionManager`（`IPermissionDefinitionManager` 实现）：构造时遍历注入的所有 `IPermissionDefinitionProvider` 调用 `Define`，加载失败会记录日志并抛出。`GetOrNull` 找不到返回 null。
- `[Permission(params string[] permissions)]`（`PermissionAttribute`，继承 `AuthorizeAttribute` 并实现 `IAsyncAuthorizationFilter`）：在 ASP.NET Core MVC 上做权限校验。`RequireAll=false`（默认）满足任一即可，`true` 要求全部；命中 `[AllowAnonymous]` 跳过；未认证返回 401，无权限返回 403；空权限数组构造时抛 `BadRequestException`。

## 能力实现

### `Leistd.Ddd.Infrastructure`

唯一注册入口：
```csharp
public static IServiceCollection AddDddInfrastructure(
    this IServiceCollection services,
    Action<UnitOfWorkOptions>? configureUnitOfWork = null)
```
行为要点：
- 注册 `IQueryableAsyncExecuter`→`EfCoreQueryableAsyncExecuter`（单例）、`IClock`→`UtcClockProvider`（单例）、`IAuditPropertySetter`→`AuditPropertySetter`（Scoped）。
- 注册数据过滤器：`IDataFilter`（单例）与开放泛型 `IDataFilter<>`（Scoped）。
- 调用 `AddUnitOfWork(configureUnitOfWork)` 与 `AddUnitOfWorkEfCore()` 接入工作单元与 EF Core 支持。
- 通过 `OnServiceRegistered` 钩子，在容器构建时自动扫描每个非抽象 `DbContext` 的 `DbSet<>` 属性，为实现 `IEntity` 的实体注册 `IRepository<TEntity>`（及检测到主键时的 `IRepository<TEntity, TKey>`），实现均为 `EfCoreRepository<,>` / `EfCoreRepository<,,>`（Scoped）。无需手写仓储注册。

仓储实现 `EfCoreRepository<TDbContext, TEntity[, TKey]>`：
- 通过 `IDbContextProvider<TDbContext>` 取得 DbContext，经 `IUnitOfWorkManager` 协作。
- 智能保存：写操作后调用 `SaveChangesIfNeededAsync` —— 若处于活动 UnitOfWork（`Uow.Current != null`）则不立即 `SaveChanges`，交由工作单元统一提交；否则立即保存。
- `GetByIdAsync` 使用 EF `FindAsync`，可命中已追踪实体；`TKey` 约束为 `IEquatable<TKey>`。

`RepositoryExtensions.GetQueryIncludingAsync<TEntity, TKey>(repository, ct, params propertySelectors)`：在 `GetQueryableAsync` 基础上批量 `Include` 导航属性。

审计与软删除：
- `AuditSaveChangesInterceptor`（`SaveChangesInterceptor`）：在 `SavingChanges(Async)` 遍历 `ChangeTracker`，按 `Added`/`Modified`/`Deleted` 调用 `IAuditPropertySetter`。对实现 `ISoftDelete` 的删除实体，将状态由 `Deleted` 改为 `Modified` 并写入删除审计（软删除）；仅软删除标记的修改不重复写修改审计。
- `AuditPropertySetter`（`IAuditPropertySetter` 实现）：依赖 `IClock` 与 `Leistd.Security.Users.ICurrentUser`，用 EF `EntityEntry` API 直接写属性（不反射）。时间取 `clock.Normalize(clock.Now)`；`CreatorId`/`DeleterId` 仅在当前用户存在且原值为空时设置。注释标注“基于 ABP 框架审计逻辑，简化版（无多租户、无 API Key 审计）”。
- `BaseDbContext`（抽象，继承 `DbContext`）：`OnModelCreating` 通过 `ModelBuilderExtensions.ApplyGlobalFilters<ISoftDelete>` 注册全局软删除查询过滤器；过滤开关由 `IDataFilter.IsEnabled<ISoftDelete>()` 决定（无 `IServiceProvider` 时默认启用）。
- `ModelBuilderExtensions`：`ConfigureByConvention<TEntity>()` 按约定为审计 Id 字段设置 `HasMaxLength(64)`；`ApplyGlobalFilters<TInterface>(expression)` 用 `ReplacingExpressionVisitor` 为所有实现该接口的根实体应用查询过滤器（无反射）。

本地事件：
- `LocalEventSaveChangesInterceptor`（`SaveChangesInterceptor`）：在 `SavedChanges(Async)`（保存完成后）收集变更实体的本地事件并清空。优先加入当前 `IUnitOfWorkManager.Current` 的待发布队列（`AddPendingEvents`）；无工作单元时回退到 `ILocalEventBus.PublishAsync`。同步路径会记录 Sync-over-Async 警告，建议使用 `SaveChangesAsync`。

## 最小可用示例

```csharp
// 1) 定义实体（带软删除/完整审计）
public class Product : FullAuditedEntity<Guid>
{
    public string Name { get; private set; } = default!;
    public Product(Guid id, string name) : base(id) { Name = name; }
    private Product() { }
}

// 2) 定义 DbContext（继承 BaseDbContext，启用全局软删除过滤器）
public class AppDbContext : BaseDbContext
{
    public DbSet<Product> Products => Set<Product>();
    public AppDbContext(DbContextOptions options, IServiceProvider sp) : base(options, sp) { }
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b); // 应用 ISoftDelete 全局过滤器
        b.Entity<Product>(e => e.ConfigureByConvention());
    }
}

// 3) 注册（Program.cs）
services.AddDbContext<AppDbContext>(o => o.UseSqlServer(conn));
services.AddDddInfrastructure();   // 自动为 AppDbContext 中的实体注册 IRepository<Product, Guid>

// 4) 注入并使用仓储（无需手写实现）
public class ProductAppService(IRepository<Product, Guid> repo) : BaseAppService, IAppService
{
    public async Task<PagedResultDto<ProductDto>> GetListAsync(
        PagedRequestDto input, IObjectMapper mapper)
    {
        var total = await repo.CountAsync();
        var items = await repo.GetListAsync();
        var paged = new PagedResultDto<Product>(total, items);
        return mapper.MapPagedResult<Product, ProductDto>(paged);
    }
}
```

## 依赖

`Leistd.Core`、`Leistd.EventBus.Core`、`Leistd.ObjectMapping.Core`、`Leistd.Exception.Core`、`Leistd.DependencyInjection`、`Leistd.Security.Core`（`ICurrentUser`）、`Leistd.Timing`（`IClock`）、`Leistd.UnitOfWork.Core` / `Leistd.UnitOfWork.EfCore`；外部依赖 EF Core、ASP.NET Core、`System.Linq.Dynamic.Core`。

## 备注

- 审计字段为 `protected set`，必须依赖 `AuditSaveChangesInterceptor` 自动填充；脱离基础设施层（如纯领域单测）不会自动写入审计值。
- 软删除依赖两处协作：`AuditSaveChangesInterceptor` 把物理删除转为逻辑删除，`BaseDbContext` 的全局过滤器在查询时过滤已删除数据；要临时查询被删数据用 `IDataFilter.Disable<ISoftDelete>()`。
- 仓储写操作是否立即落库取决于是否处于 UnitOfWork：在 UoW 内不立即 `SaveChanges`，由 UoW 统一提交。
- 同步 `SaveChanges` 发布本地事件存在 Sync-over-Async 风险，应优先使用 `SaveChangesAsync`。
- `AuditPropertySetter` 注释标注“对应 ABP 框架审计逻辑的简化版本（无多租户、无 API Key 审计）”；`RepositoryExtensions` 注释标注“参考 Creekdream”。
- `PagedRequestDto` 默认 `Limit=10`、`Offset=0`。`EntityDto`（无泛型）默认主键类型为 `Guid`。
