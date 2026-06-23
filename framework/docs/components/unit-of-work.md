# 工作单元（`unit-of-work`）
> 借鉴 ABP 的 UnitOfWork 设计，通过 `[UnitOfWork]` 特性与 AOP 拦截器声明式管理数据库事务边界，并按提交阶段编排本地事件发布。

## 包

| 包 | 角色 | 何时引用 |
| --- | --- | --- |
| `Leistd.UnitOfWork.Core` | 核心抽象与运行时：工作单元、管理器、环境上下文、事务/数据库 API 抽象、AOP 拦截器、事件阶段编排 | 任何需要声明式事务/工作单元的项目 |
| `Leistd.UnitOfWork.EfCore` | EF Core 落地实现：`IDbContextProvider<T>`、EF Core 数据库/事务 API | 使用 EF Core 作为持久化层时 |

## 核心抽象

### `IUnitOfWork`（命名空间 `Leistd.UnitOfWork.Core.Uow`）
工作单元，继承 `IDatabaseApiContainer`、`ITransactionApiContainer`、`IDisposable`。

```csharp
Guid Id { get; }                 // 工作单元唯一标识
IUnitOfWorkOptions Options { get; }
IUnitOfWork? Outer { get; }      // 外层工作单元（嵌套场景），无则为 null
bool IsDisposed { get; }
bool IsCompleted { get; }
```
```csharp
Task CompleteAsync(CancellationToken cancellationToken = default);
```
完成工作单元：执行 `SaveChanges`、发布 `BeforeCommit` 事件、提交事务。重复调用会抛 `InvalidOperationException`；若已回滚则直接返回。

```csharp
Task RollbackAsync(CancellationToken cancellationToken = default);
```
回滚所有数据库与事务操作（幂等，重复调用直接返回）。

```csharp
void AddPendingEvents(IEnumerable<ILocalEvent> events);
```
由基础设施层调用，登记待发布的本地事件，事件按阶段（`BeforeCommit`/`AfterCommit`/`AfterRollback`/`AfterCompletion`）发布。

### `IUnitOfWorkManager`（`Leistd.UnitOfWork.Core.Uow`）
```csharp
IUnitOfWork? Current { get; }    // 当前有效工作单元（自动跳过已释放/已完成的，沿 Outer 上溯），无则 null
Task<IUnitOfWork> BeginAsync(UnitOfWorkOptions? options = null, bool requiresNew = true);
```
开启工作单元。`requiresNew = false` 且已存在当前工作单元时返回 `ChildUnitOfWork`（复用父级，自身 `CompleteAsync` 为空操作）；否则创建新工作单元（含独立 DI Scope）。`options` 为 null 且 `requiresNew` 时使用默认选项并强制 `IsTransactional = true`。

### `IAmbientUnitOfWork`（`Leistd.UnitOfWork.Core.Uow`）
```csharp
IUnitOfWork? Get();
void Set(IUnitOfWork? value);    // 设为 null 时恢复到外层工作单元
```
基于 `AsyncLocal` 存储当前工作单元，提供环境（ambient）访问。

### 数据库 / 事务 API 抽象（`Leistd.UnitOfWork.Core.Database`）
- `IDatabaseApi : IDisposable`：数据库 API 标记接口。
- `IDatabaseApiContainer`：`IDatabaseApi GetOrAddDatabaseApi(Func<IDatabaseApi> factory)`，按需创建并缓存单一数据库 API。
- `ITransactionApi : IDisposable`：`Task CommitAsync()`，提交事务。
- `ITransactionApiContainer`：`ITransactionApi? FindTransactionApi()`（无则 null）与 `void AddTransactionApi(ITransactionApi api)`。
- `ISupportsSavingChanges`：`Task SaveChangesAsync(...)`，数据库 API 选配，`CompleteAsync` 时被调用。
- `ISupportsRollback`：`Task RollbackAsync(...)`，回滚时被调用。

### 选项与特性（`Leistd.UnitOfWork.Core.Options` / `.Attributes`）
- `IUnitOfWorkOptions` / `UnitOfWorkOptions`：`IsTransactional`、`IsolationLevel`（`System.Data.IsolationLevel?`）、`Timeout`（`TimeSpan?`）；`UnitOfWorkOptions` 提供 `Clone()`。
- `UnitOfWorkAttribute`：标注在类/接口/方法上声明事务边界，属性 `Timeout`、`IsolationLevel`、`IsDisabled`（默认 false）；`CreateOptionsFromDefault(...)` 中 `IsTransactional = !IsDisabled`。

### 事件阶段（`Leistd.UnitOfWork.Core.Events`）
- `UnitOfWorkPhase` 枚举：`BeforeCommit`（抛异常会回滚事务）、`AfterCommit`（默认，不影响事务）、`AfterRollback`、`AfterCompletion`（Dispose 时触发）。
- `UnitOfWorkEventHandlerAttribute(phase = AfterCommit)`：标注事件处理器在哪个阶段执行。
- `UnitOfWorkContext.CurrentPhase`：`AsyncLocal` 存储的当前阶段，供处理器过滤（只读对外，框架内部写）。
- `UnitOfWorkEventArgs` / `UnitOfWorkFailedEventArgs`：事件参数（后者含 `Exception`、`IsRolledback`）。

## 能力实现

### `Leistd.UnitOfWork.Core`
DI 注册：`IServiceCollection.AddUnitOfWork(Action<UnitOfWorkOptions>? configureOptions = null)`。

行为要点：
- 注册 `IAmbientUnitOfWork`（单例，`AsyncLocal` 存储）、`IUnitOfWorkManager`（单例）、`IUnitOfWork`（瞬态 `UnitOfWork`）及 `UnitOfWorkInterceptor`、`UnitOfWorkEventHandlerInterceptor`（瞬态）；`UnitOfWorkOptions` 注册为单例。
- 通过 DI 组件的 `OnServiceRegistered` 回调：为带 `[UnitOfWork]` 特性（类或方法）的服务自动挂上 `UnitOfWorkInterceptor`；为带 `[UnitOfWorkEventHandler]` 特性的 `IEventHandler<>` 挂上 `UnitOfWorkEventHandlerInterceptor`。
- `UnitOfWorkInterceptor`：拦截带特性的方法，以 `requiresNew: false` 开启工作单元，正常返回后 `CompleteAsync`，异常时 `Dispose`（触发失败/回滚阶段）后重新抛出。无 `[UnitOfWork]` 特性的方法直接放行。
- `UnitOfWorkEventHandlerInterceptor`：仅拦截 `HandleAsync`；处理器无 `[UnitOfWorkEventHandler]` 特性则放行，否则仅当 `UnitOfWorkContext.CurrentPhase`（无阶段时默认 `AfterCommit`）与特性声明阶段一致时才执行，否则跳过。
- `CompleteAsync` 采用 ABP 风格循环：`SaveChanges` → 发布本阶段 `BeforeCommit` 事件 → 直到无新事件再 `Commit`，最后发布 `AfterCommit`；`Dispose` 时若未成功完成则发布 `AfterRollback`，并始终发布 `AfterCompletion`。
- 嵌套：`ChildUnitOfWork` 委托父工作单元，自身 `CompleteAsync` 为空操作、`Dispose` 为空，事务由最外层统一提交。

### `Leistd.UnitOfWork.EfCore`
DI 注册：`IServiceCollection.AddUnitOfWorkEfCore()`（注册泛型 `IDbContextProvider<>` → `DbContextProvider<>`，Scoped）。

行为要点（`DbContextProvider<TDbContext>.GetDbContextAsync`）：
- 模式 1（无当前工作单元）：直接从当前 Scoped 容器获取 `TDbContext`，适合简单 CRUD，无显式事务。
- 模式 2（在工作单元内）：从工作单元的独立 Scope 获取/缓存 `DbContext`；`Options.IsTransactional` 为真时开启数据库事务（按 `IsolationLevel` 选择重载），并将 `EfCoreTransactionApi` 登记到工作单元。
- 多 `DbContext` 复用同一事务：关系型数据库通过 `UseTransaction` 共享底层事务，非关系型则各自 `BeginTransaction`，附加上下文记入 `AttendedDbContexts`，在 Commit/Rollback 时统一处理（共享同一连接的关系型上下文随主事务一并提交，跳过单独提交）。
- `Timeout`：仅关系型数据库且未显式设置 `CommandTimeout` 时，按 `Options.Timeout.TotalSeconds` 设置命令超时。
- `EfCoreDatabaseApi<T>` 的 `Dispose` 为空操作：`DbContext` 随工作单元 Scope 释放自动 `Dispose`，避免重复释放。

## 最小可用示例

```csharp
// 1. 注册
var services = new ServiceCollection();
services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connStr));
services.AddUnitOfWork();        // 核心
services.AddUnitOfWorkEfCore();  // EF Core 支持
services.AddTransient<OrderService>();

// 2. 在业务方法上声明事务边界
public class OrderService(IDbContextProvider<AppDbContext> dbProvider)
{
    [UnitOfWork]
    public virtual async Task PlaceOrderAsync(Order order)
    {
        var db = await dbProvider.GetDbContextAsync();
        db.Orders.Add(order);
        // 无需手动 SaveChanges / 提交事务：
        // UnitOfWorkInterceptor 在方法返回后 CompleteAsync 提交，异常时回滚
    }
}

// 3. 调用（须经由 DI 代理实例，特性方法为 virtual）
var orderService = provider.GetRequiredService<OrderService>();
await orderService.PlaceOrderAsync(order);
```

需要手动控制时：

```csharp
await using var uow = await uowManager.BeginAsync();
var db = await dbProvider.GetDbContextAsync();
db.Orders.Add(order);
await uow.CompleteAsync();   // 提交
```

## 依赖
`Leistd.DynamicProxy`（AOP 拦截）、`Leistd.DependencyInjection`（`OnServiceRegistered` 回调）、`Leistd.EventBus.Core`（本地事件按阶段发布）。

## 备注
- 设计与术语对齐 ABP：`CompleteAsync` 的 `SaveChanges→BeforeCommit` 循环、`ChildUnitOfWork`、`DbContextProvider` 的双模式、多 `DbContext` 共享事务策略均参照 ABP 实现。
- 声明式事务依赖动态代理：带 `[UnitOfWork]` 的方法必须可被代理（`virtual` 方法或接口方法），且实例需从 DI 容器解析，直接 `new` 不会触发拦截。
- `UnitOfWorkInterceptor` 仅识别显式 `[UnitOfWork]` 特性（类级或方法级），不做约定式拦截。
- 拦截器内 `BeginAsync` 使用 `requiresNew: false`，因此嵌套调用会复用外层事务（子工作单元提交为空操作），事务在最外层统一提交。
- `DbContextProvider` 模式 2 通过把 `IUnitOfWork` 强制转换为具体 `Core.Uow.UnitOfWork` 获取其 `ServiceProvider`，若实际类型不符会抛 `InvalidOperationException`。
- `BeforeCommit` 阶段处理器抛异常会导致回滚；`AfterCommit`/`AfterRollback`/`AfterCompletion` 阶段的异常被吞掉，不影响事务结果。
