# 工作单元与事务

一个业务操作往往跨多个仓储、多次数据库写入，必须**要么全部成功、要么全部回滚**。如果让每个方法各自管理 `DbContext`、显式开启/提交事务、再手动 `SaveChanges`，业务代码会被基础设施细节淹没，且很难保证嵌套调用时复用同一个事务边界。

Leistd 的工作单元（Unit of Work）借鉴 Volo.ABP 的设计，把「一个请求 / 一个业务方法」内的所有数据库操作收敛到一个统一的事务边界：通过 `[UnitOfWork]` 特性声明边界，由拦截器自动 `Begin → SaveChanges → Commit`，异常时自动回滚；嵌套调用自动复用外层工作单元（不重复开事务）。同时它还提供与领域事件的集成——可让事件处理器精确地在「提交前 / 提交后 / 回滚后 / 完成后」等阶段运行。

## 何时使用

| 场景 | 做法 |
| --- | --- |
| 一个业务方法内跨多次写入需原子提交（如下单：扣库存 + 建订单 + 记流水） | 在方法或类上标注 `[UnitOfWork]`，拦截器自动管理事务 |
| 需要手动控制事务边界（非拦截场景） | 注入 `IUnitOfWorkManager`，`BeginAsync` 后 `CompleteAsync` |
| EF Core 持久化、且希望多个仓储共享同一 `DbContext` 与事务 | 注入 `IDbContextProvider<TDbContext>` 获取受工作单元管理的 `DbContext` |
| 让领域事件在事务提交成功后再执行（发通知、刷缓存） | 事件处理器标注 `[UnitOfWorkEventHandler(UnitOfWorkPhase.AfterCommit)]` |
| 只想编写依赖工作单元抽象的领域/应用服务 | 仅引用 `Leistd.UnitOfWork.Core` |

> 持久化实现当前仅提供 EF Core（`Leistd.UnitOfWork.EfCore`）。若不接入任何持久化实现，核心包仍可提供事务边界、嵌套复用与事件阶段调度能力。

## 安装

```bash
# 抽象与核心（工作单元管理、拦截器、事件阶段）
dotnet add package Leistd.UnitOfWork.Core

# EF Core 持久化集成（DbContext 与事务接入工作单元）
dotnet add package Leistd.UnitOfWork.EfCore
```

> 本仓库模板项目通过中央包管理（CPM）统一版本，添加时无需写版本号。

## 配置 Provider

在 `Program.cs` 注册核心服务，需要 EF Core 集成时再追加 `AddUnitOfWorkEfCore`：

```csharp
// 核心：注册工作单元管理器、拦截器，并可配置默认 Options
builder.Services.AddUnitOfWork(options =>
{
    options.IsTransactional = true;
    options.IsolationLevel = System.Data.IsolationLevel.ReadCommitted;
});

// EF Core 集成：注册 IDbContextProvider<TDbContext>
builder.Services.AddUnitOfWorkEfCore();
```

`AddUnitOfWork` 的注册绑定如下：

| 注册 | 绑定接口 / 类型 | 生命周期 |
| --- | --- | --- |
| `AmbientUnitOfWork` | `IAmbientUnitOfWork`（基于 `AsyncLocal`） | Singleton |
| `UnitOfWorkManager` | `IUnitOfWorkManager` | Singleton |
| `UnitOfWork` | `IUnitOfWork` | Transient |
| `UnitOfWorkInterceptor` | 拦截带 `[UnitOfWork]` 的服务方法/类 | Transient |
| `UnitOfWorkEventHandlerInterceptor` | 拦截带 `[UnitOfWorkEventHandler]` 的事件处理器 | Transient |
| `UnitOfWorkOptions` | 默认配置单例 | Singleton |

> `AddUnitOfWork` 通过依赖注入组件的 `OnServiceRegistered` 回调，为带 `[UnitOfWork]` / `[UnitOfWorkEventHandler]` 特性的类型自动挂接拦截器，因此特性能否生效依赖 AOP（动态代理）组件已就绪。

`AddUnitOfWorkEfCore` 注册 `IDbContextProvider<>` → `DbContextProvider<>`（Scoped）。

## 使用

### 声明式事务（推荐）

在应用服务上标注 `[UnitOfWork]`，方法执行期间的所有写入会被收敛到一个事务，正常返回后自动提交，抛异常自动回滚：

```csharp
using Leistd.UnitOfWork.Core.Attributes;

[UnitOfWork]
public class OrderAppService(
    IDbContextProvider<AppDbContext> dbContextProvider)
{
    public async Task PlaceOrderAsync(PlaceOrderInput input)
    {
        var db = await dbContextProvider.GetDbContextAsync();

        db.Stocks.Deduct(input.ProductId, input.Quantity);
        db.Orders.Add(new Order(input));
        // 无需手动 SaveChanges / Commit：方法正常返回时工作单元统一提交，
        // 抛异常则整体回滚。
    }
}
```

`[UnitOfWork]` 可标注在类（对所有方法生效）或单个方法上；通过特性属性覆盖默认配置：

```csharp
[UnitOfWork(IsolationLevel = IsolationLevel.Serializable)]
public async Task TransferAsync(...) { /* ... */ }

[UnitOfWork(IsDisabled = true)] // 不开启事务（只读查询）
public async Task<OrderDto> GetAsync(Guid id) { /* ... */ }
```

### 手动控制边界

非拦截场景下，注入 `IUnitOfWorkManager` 手动管理：

```csharp
public class BatchImporter(IUnitOfWorkManager uowManager)
{
    public async Task ImportAsync()
    {
        var uow = await uowManager.BeginAsync();
        try
        {
            // —— 业务写入 ——
            await uow.CompleteAsync(); // 提交
        }
        catch
        {
            await uow.RollbackAsync(); // 回滚
            throw;
        }
        finally
        {
            uow.Dispose();
        }
    }
}
```

### 事件阶段集成

让事件处理器在事务成功提交后才执行（避免「事务回滚了但通知已发出」）：

```csharp
using Leistd.UnitOfWork.Core.Events;

[UnitOfWorkEventHandler(UnitOfWorkPhase.AfterCommit)]
public class SendWelcomeEmailHandler : IEventHandler<UserCreatedEvent>
{
    public async Task HandleAsync(UserCreatedEvent @event)
        => await _emailService.SendWelcomeEmailAsync(@event.User.Email);
}
```

## 接口参考

`Leistd.UnitOfWork.Core` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `IUnitOfWorkManager.Current` | 当前生效的工作单元；无则返回 `null`（自动跳过已释放/已完成的） |
| `IUnitOfWorkManager.BeginAsync(options?, requiresNew=true)` | 开启工作单元；`requiresNew=false` 且已有当前工作单元时返回复用父级的子工作单元 |
| `IUnitOfWork.Id` | 工作单元唯一标识（`Guid`） |
| `IUnitOfWork.Options` | 当前工作单元的 `IUnitOfWorkOptions` 配置 |
| `IUnitOfWork.Outer` | 外层工作单元（嵌套场景），无则 `null` |
| `IUnitOfWork.IsDisposed` / `IsCompleted` | 是否已释放 / 已完成 |
| `IUnitOfWork.CompleteAsync(ct)` | 完成并提交；重复调用抛 `InvalidOperationException` |
| `IUnitOfWork.RollbackAsync(ct)` | 回滚；幂等（已回滚再调无副作用） |
| `IUnitOfWork.AddPendingEvents(events)` | 由基础设施层登记待发布的领域事件 |
| `IAmbientUnitOfWork.Get() / Set(uow)` | 读取/设置当前线程上下文中的工作单元（`AsyncLocal`） |
| `IDatabaseApiContainer.GetOrAddDatabaseApi(factory)` | 获取或惰性创建该工作单元的 `IDatabaseApi` |
| `ITransactionApiContainer.FindTransactionApi()` | 查找事务 API，无则 `null` |
| `ITransactionApi.CommitAsync()` | 提交事务 |
| `ISupportsSavingChanges.SaveChangesAsync(ct)` / `ISupportsRollback.RollbackAsync(ct)` | 数据库/事务 API 的可选实现，供工作单元在提交/回滚时调用 |
| `[UnitOfWork]` | 声明事务边界；属性 `Timeout` / `IsolationLevel` / `IsDisabled` |
| `[UnitOfWorkEventHandler(phase)]` | 声明事件处理器执行阶段（默认 `AfterCommit`） |
| `UnitOfWorkPhase` | 阶段枚举：`BeforeCommit` / `AfterCommit` / `AfterRollback` / `AfterCompletion` |
| `UnitOfWorkContext.CurrentPhase` | 当前所处的工作单元阶段（`AsyncLocal`，供处理器过滤） |

`Leistd.UnitOfWork.EfCore` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `IDbContextProvider<TDbContext>.GetDbContextAsync(ct)` | 获取受工作单元管理的 `DbContext`；不在工作单元内时直接返回 Scoped 实例 |

## 实现行为

### Leistd.UnitOfWork.Core

- **当前工作单元**基于 `AsyncLocal`（`AmbientUnitOfWork`）随异步流传递；`IUnitOfWorkManager.Current` 在读取时会顺着 `Outer` 链跳过已 `Disposed` 或已 `Completed` 的工作单元。
- **嵌套复用**：拦截器以 `requiresNew: false` 调 `BeginAsync`，若已有当前工作单元则创建 `ChildUnitOfWork`——子单元的 `CompleteAsync` 为**空操作**，提交/回滚由最外层真正的工作单元统一负责，从而保证嵌套调用共用同一事务边界。
- **提交流程**（`CompleteAsync`）：循环执行「`SaveChangesAsync` → 发布 `BeforeCommit` 阶段事件」直到无新增待发布事件，再 `CommitAsync` 提交事务，成功后发布 `AfterCommit` 阶段事件。重复调用 `CompleteAsync` 抛 `InvalidOperationException`。
- **事件阶段调度**：`BeforeCommit` 处理器抛异常会导致事务回滚；`AfterCommit` 在提交成功后发布；失败时发布 `AfterRollback`（其内部异常被吞掉）；`Dispose` 时发布 `AfterCompletion`（无论成功失败）。`UnitOfWorkEventHandlerInterceptor` 仅拦截 `HandleAsync`，依据 `UnitOfWorkContext.CurrentPhase` 与处理器特性的 `Phase` 匹配决定是否执行，不匹配则跳过；无 `CurrentPhase` 时按 `AfterCommit` 处理。
- 拦截器在被拦截方法**抛异常时调用 `uow.Dispose()`**（而非显式 `RollbackAsync`），由 Dispose 路径触发失败处理与事务释放。

### Leistd.UnitOfWork.EfCore

- `IDbContextProvider<TDbContext>` 注册为 **Scoped**。`GetDbContextAsync` 有两种模式：不在工作单元内时直接从 Scoped 容器返回 `DbContext`（适合简单 CRUD）；在工作单元内则从工作单元自身的 Scope 获取，并通过 `GetOrAddDatabaseApi` 保证整个工作单元内复用同一 `DbContext`。
- 当工作单元 `IsTransactional` 时，首个 `DbContext` 会 `BeginTransactionAsync` 开启数据库事务（按 `Options.IsolationLevel` 指定隔离级别）；后续 `DbContext` 若是关系型且共享连接，则通过 `UseTransaction` 复用同一事务，否则各自开事务并登记到 `AttendedDbContexts`，提交/回滚时统一处理。
- `Options.Timeout` 仅对关系型数据库生效，且仅在 `CommandTimeout` 未设置时按秒应用。
- `EfCoreDatabaseApi.Dispose` **不显式释放 `DbContext`**——`DbContext` 由工作单元创建的 Scope 在释放时统一回收，避免重复 Dispose。

## 配置项 / Options

`AddUnitOfWork` 接受 `Action<UnitOfWorkOptions>` 配置默认值；`[UnitOfWork]` 特性可在方法/类级别覆盖。

| 属性 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `IsTransactional` | `bool` | `false`（`UnitOfWorkManager.BeginAsync` 在未传 options 时置为 `true`） | 是否开启数据库事务 |
| `IsolationLevel` | `IsolationLevel?` | `null`（用数据库默认） | 事务隔离级别 |
| `Timeout` | `TimeSpan?` | `null` | 命令超时（仅关系型数据库） |

`[UnitOfWork]` 特性额外属性 `IsDisabled`（默认 `false`）：置 `true` 时生成的工作单元 `IsTransactional = false`，即不开启事务。

## 注意事项

- `[UnitOfWork]` 与 `[UnitOfWorkEventHandler]` 的生效依赖拦截器，而拦截器只在通过 DI 解析的服务上挂接（基于本框架 AOP 组件）。直接 `new` 出来的对象不会被拦截。
- 声明式用法**无需手动** `SaveChanges` / `CommitAsync`：拦截器在方法正常返回时统一提交。手动用法务必在异常路径 `RollbackAsync` 并 `Dispose`。
- 嵌套调用中只有最外层工作单元真正提交/回滚，内层 `ChildUnitOfWork.CompleteAsync` 不做任何事——不要依赖内层「提交」来落库。
- `BeforeCommit` 阶段的事件处理器异常会触发整体回滚；只有确实希望影响事务结果的逻辑才放在该阶段，发通知、刷缓存等副作用应放 `AfterCommit`。
- `RollbackAsync` 是幂等的；`CompleteAsync` 不可重复调用，否则抛 `InvalidOperationException`。
- 部分接口注释采用英文并对照 ABP 设计（如 `UnitOfWorkFailedEventArgs`、`ITransactionApi`），命名与 ABP 工作单元保持一致以便迁移参考。

## 相关

- [组件总览](./README.md)
- [依赖注入](./dependency-injection.md)
- [面向切面编程（AOP）](./aop.md)
- [事件总线](./event-bus.md)
