# 工作单元与事务

一个业务操作往往跨多个仓储、多次数据库写入，必须**要么全部成功、要么全部回滚**。如果让每个方法各自管理 `DbContext`、显式开启/提交事务、再手动 `SaveChanges`，业务代码会被基础设施细节淹没，且很难保证嵌套调用时复用同一个事务边界。

Leistd 的工作单元（Unit of Work）把「一个请求 / 一个业务方法」内的所有数据库操作收敛到统一事务边界：通过 `[UnitOfWork]` 特性声明边界，由拦截器自动 `Begin → SaveChanges → Commit`，异常时自动回滚；嵌套调用自动复用外层工作单元（不重复开事务）。同时它还提供与领域事件的集成，可让事件处理器精确地在「提交前 / 提交后 / 回滚后 / 完成后」等阶段运行。

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
public class OrderPlacementService(
    IDbContextProvider<AppDbContext> dbContextProvider)
{
    public async Task PlaceOrderAsync(PlaceOrderInput input)
    {
        // 同一工作单元内多次获取，拿到的是同一个受管理的 DbContext
        var db = await dbContextProvider.GetDbContextAsync();

        db.Stocks.Deduct(input.ProductId, input.Quantity);
        db.Orders.Add(new Order(input));
        // 无需手动 SaveChanges / Commit：方法正常返回时工作单元统一提交，
        // 抛异常则整体回滚。扣库存与建订单的原子性由 [UnitOfWork] 保证。
    }
}
```

> `IDbContextProvider<TDbContext>` 是本组件的核心 API：它返回受当前工作单元管理的 `DbContext`，让多个数据操作共享同一上下文与事务。在采用 [DDD 四层基座](../ddd-struct/ddd-struct.md) 的项目里，应用服务通常经仓储读写、由仓储实现在内部使用 `IDbContextProvider`；本组件本身不依赖 ddd-struct，上面直用 `IDbContextProvider` + `DbContext` 是其最小自包含用法。

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
| `IDatabaseApiContainer.FindDatabaseApi(key)` / `AddDatabaseApi(key, api)` | 按稳定 key 管理工作单元中的多个数据库 API；同 key 不允许覆盖 |
| `ITransactionApiContainer.FindTransactionApi(key)` | 按物理目标 key 查找事务 API，无则 `null` |
| `ITransactionApiContainer.AddTransactionApi(key, api)` | 向容器登记一个事务 API（供基础设施层实现接入） |
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
| `DbContextCreationContext.Current` | 宿主 `AddDbContext` 同步 Options 回调读取的已异步解析连接上下文 |

## 实现行为

### Leistd.UnitOfWork.Core

- **当前工作单元**基于 `AsyncLocal`（`AmbientUnitOfWork`）随异步流传递；`IUnitOfWorkManager.Current` 在读取时会顺着 `Outer` 链跳过已 `Disposed` 或已 `Completed` 的工作单元。
- **嵌套复用**：拦截器以 `requiresNew: false` 调 `BeginAsync`，若已有当前工作单元则创建 `ChildUnitOfWork`——子单元的 `CompleteAsync` 为**空操作**，提交/回滚由最外层真正的工作单元统一负责，从而保证嵌套调用共用同一事务边界。
- **提交流程**（`CompleteAsync`）：循环执行「`SaveChangesAsync` → 发布 `BeforeCommit` 阶段事件」直到无新增待发布事件，再 `CommitAsync` 提交事务，成功后发布 `AfterCommit` 阶段事件。重复调用 `CompleteAsync` 抛 `InvalidOperationException`。
- **事件阶段调度**：`BeforeCommit` 处理器抛异常会导致事务回滚；`AfterCommit` 在提交成功后发布；失败时发布 `AfterRollback`（其内部异常被吞掉）；`Dispose` 时发布 `AfterCompletion`（无论成功失败）。`UnitOfWorkEventHandlerInterceptor` 仅拦截 `HandleAsync`，依据 `UnitOfWorkContext.CurrentPhase` 与处理器特性的 `Phase` 匹配决定是否执行，不匹配则跳过；无 `CurrentPhase` 时按 `AfterCommit` 处理。
- 拦截器在被拦截方法**抛异常时调用 `uow.Dispose()`**（而非显式 `RollbackAsync`），由 Dispose 路径触发失败处理与事务释放。

### Leistd.UnitOfWork.EfCore

- `IDbContextProvider<TDbContext>` 注册为 **Scoped**。在工作单元内按 DbContext 类型 key 复用实例；不在工作单元内仍每次通过当前 Scoped 容器创建/获取。
- 宿主注册了 `ITenantConnectionStringResolver` 时，Provider 先根据 DbContext 上的 `[TenantConnectionStringName]` 异步解析连接（未标记使用 `Default`），再用 `DbContextCreationContext.Current` 把结果传入同步 `AddDbContext` Options 回调。回调不得执行远程调用或 sync-over-async。未注册 Resolver 时保持普通单连接 DbContext 行为。
- 一个 UoW 在首次获取 DbContext 时绑定当前租户和物理数据库目标。UoW 存活期内切换租户或让后续 DbContext 解析到不同物理目标会立即失败，避免把一个原子边界静默拆成跨库操作。
- 当 UoW `IsTransactional` 时，同一物理关系型目标上的多个 DbContext 共用一个连接与 EF Core 事务，提交/回滚由最外层 UoW 统一处理。非关系型 Provider 可参与 UoW 的保存/生命周期，但不声称具备关系型共享事务语义。
- `Options.Timeout` 仅对关系型数据库生效，且仅在 EF Core 命令超时未设置时按秒应用。
- `EfCoreDatabaseApi.Dispose` **不显式释放 `DbContext`**——`DbContext` 由工作单元创建的 Scope 在释放时统一回收，避免重复 Dispose。

## 配置项 / Options

`AddUnitOfWork` 接受 `Action<UnitOfWorkOptions>` 配置默认值；`[UnitOfWork]` 特性可在方法/类级别覆盖。

| 属性 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `IsTransactional` | `bool` | `false`（`UnitOfWorkManager.BeginAsync` 在未传 options 时置为 `true`） | 是否开启数据库事务 |
| `IsolationLevel` | `IsolationLevel?` | `null`（用数据库默认） | 事务隔离级别 |
| `Timeout` | `TimeSpan?` | `null` | EF Core 命令超时（仅关系型数据库） |

`[UnitOfWork]` 特性额外属性 `IsDisabled`（默认 `false`）：置 `true` 时生成的工作单元 `IsTransactional = false`，即不开启事务。

## 注意事项

- `[UnitOfWork]` 与 `[UnitOfWorkEventHandler]` 的生效依赖拦截器，而拦截器只在通过 DI 解析的服务上挂接（基于本框架 AOP 组件）。直接 `new` 出来的对象不会被拦截。
- 声明式用法**无需手动** `SaveChanges` / `CommitAsync`：拦截器在方法正常返回时统一提交。手动用法务必在异常路径 `RollbackAsync` 并 `Dispose`。
- 嵌套调用中只有最外层工作单元真正提交/回滚，内层 `ChildUnitOfWork.CompleteAsync` 不做任何事——不要依赖内层「提交」来落库。
- `BeforeCommit` 阶段的事件处理器异常会触发整体回滚；只有确实希望影响事务结果的逻辑才放在该阶段，发通知、刷缓存等副作用应放 `AfterCommit`。
- `RollbackAsync` 是幂等的；`CompleteAsync` 不可重复调用，否则抛 `InvalidOperationException`。

## 相关

- [组件总览](./README.md)
- [依赖注入](./dependency-injection.md)
- [面向切面编程（AOP）](./aop.md)
- [事件总线](./event-bus.md)
