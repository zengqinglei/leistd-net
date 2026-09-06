# 工作单元与事务

工作单元将一个业务方法中的多次数据库写入收敛到同一提交边界，并在提交前后分阶段发布本地事件。

## 何时使用

| 场景 | 做法 |
| --- | --- |
| 多次写入必须原子提交 | 在类或方法上标注 `[UnitOfWork]` |
| 非拦截场景需手动边界 | 使用 `IUnitOfWorkManager.BeginAsync()` |
| 多个 EF Core 操作需共享上下文与事务 | 使用 `IDbContextProvider<TDbContext>` |
| 本地事件需在提交前或提交后执行 | 使用 `UnitOfWorkPhase` |

以下情况不需要工作单元：

- 只读方法。
- 只有一次 `SaveChanges`；EF Core 已使用隐式事务。
- 管理器已在一次 `SaveChanges` 中完成批量写入。
- 写入由本组件无法管理的外部 Store 提交。

工作单元默认按需引入，不是所有写方法的必选装饰。

## 安装

```bash
dotnet add package Leistd.UnitOfWork.Core
dotnet add package Leistd.UnitOfWork.EntityFrameworkCore
```

## 注册

```csharp
builder.Services.AddUnitOfWork(options =>
{
    options.IsTransactional = true;
    options.IsolationLevel = IsolationLevel.ReadCommitted;
});

builder.Services.AddUnitOfWorkEfCore();
```

`AddUnitOfWork` 注册环境工作单元、管理器、默认实现、两个动态代理拦截器与本地事件延迟器。`AddUnitOfWorkEfCore` 将 `IDbContextProvider<>` 注册为 Scoped。

`[UnitOfWork]` 和 `[UnitOfWorkEventHandler]` 依赖 Leistd DI/AOP 管道。宿主必须通过框架的服务提供器工厂启用动态代理；否则特性不生效。

## 使用

### 声明式边界

```csharp
using Leistd.UnitOfWork.Attributes;

[UnitOfWork]
public class OrderPlacementService(
    IDbContextProvider<AppDbContext> dbContextProvider)
{
    public async Task PlaceOrderAsync(PlaceOrderInput input)
    {
        var db = await dbContextProvider.GetDbContextAsync();
        db.Stocks.Deduct(input.ProductId, input.Quantity);
        db.Orders.Add(new Order(input));
    }
}
```

方法正常返回时拦截器统一保存并提交；异常时回滚。声明式用法不手动调用 `SaveChanges` 或 `CommitAsync`。

```csharp
[UnitOfWork(IsolationLevel = IsolationLevel.Serializable)]
public Task TransferAsync(...) => ...;

[UnitOfWork(IsDisabled = true)]
public Task<OrderDto> GetAsync(Guid id) => ...;
```

`IsDisabled = true` 表示不创建工作单元；`[UnitOfWork(false)]` 创建非事务工作单元。

### 手动边界

```csharp
var uow = await unitOfWorkManager.BeginAsync();
try
{
    await ImportAsync();
    await uow.CompleteAsync();
}
catch
{
    await uow.RollbackAsync();
    throw;
}
finally
{
    uow.Dispose();
}
```

`requiresNew` 默认为 `false`：存在当前工作单元时返回复用父边界的子工作单元，只有最外层真正提交。传 `true` 创建独立作用域与提交边界，但不改变事务选项。

### 在事务内提前冲刷

工作单元内的写入在 `CompleteAsync` 前不保证已发送到数据库。创建后返回时，优先用仓储返回的实体构造输出，不再回查。

仅在需要数据库生成值、计算列、触发器结果或新并发标记时手动冲刷：

```csharp
var order = await orderRepository.InsertAsync(new Order(input));
await unitOfWorkManager.Current!.SaveChangesAsync();
await orderLineRepository.InsertManyAsync(CreateLines(order.Id));
```

`SaveChangesAsync()` 只冲刷挂起变更，不提交事务；后续回滚仍会撤销这些写入。

### 事件阶段

`CompleteAsync` 循环执行 `SaveChangesAsync` 和 `BeforeCommit` 事件，直到无新事件；然后提交事务并发布 `AfterCommit` 事件。

| 阶段 | 用途 | 失败语义 |
| --- | --- | --- |
| `BeforeCommit` | 必须影响事务结果的处理 | 事务型工作单元中异常触发回滚 |
| `AfterCommit` | 通知、缓存刷新等提交后副作用 | 事务已提交，后续写入使用独立上下文 |

未标注 `[UnitOfWorkEventHandler]` 的处理器默认属于 `AfterCommit`。`BeforeCommit` 处理器在工作单元外发布时不执行并记录 Warning。

```csharp
[UnitOfWorkEventHandler(UnitOfWorkPhase.BeforeCommit)]
public class ValidateOrderHandler : IEventHandler<OrderCreatedEvent>
{
    public Task HandleAsync(OrderCreatedEvent @event) => ValidateAsync(@event);
}
```

`AfterCommit` 阶段的 `IUnitOfWorkManager.Current` 为 `null`。该阶段若写库，会使用独立上下文并自行提交。需要可靠发送的跨系统副作用应使用 Outbox。

### EF Core 连接绑定

`IDbContextProvider<TDbContext>` 在工作单元内按 DbContext 类型复用实例。事务型工作单元中，同一物理关系数据库上的多个 DbContext 共用连接和事务。

宿主注册 `IConnectionStringResolver` 时，Provider 根据 `[ConnectionStringName]` 异步解析连接，并通过 `DbContextCreationContext.Current` 传入同步 `AddDbContext` 回调。该回调不得再执行远程调用或 sync-over-async。

首次获取 DbContext 时，工作单元绑定连接归属与物理目标。生命周期内任一值改变都立即失败，以防止一个原子边界跨库或跨租户。不在工作单元内时不建立这两道绑定。

本组件不提供跨物理事务原子性。多个事务按顺序提交时，后续失败可能已造成部分提交；此时抛出带已提交与失败 key 的 `InternalServerException`。

### 取消边界

事务型 `CompleteAsync(cancellationToken)` 分为两段：

```text
可取消：SaveChanges + BeforeCommit
不可取消：Commit + AfterCommit
```

进入 Commit 前会最后检查一次取消。Commit 已开始后不再响应取消，避免向调用方返回“无法确定是否已提交”的结果。

非事务工作单元没有该边界：每次保存可能已独立持久化，`BeforeCommit` 异常不承诺回滚已完成的写入。

## 接口参考

| 类型或成员 | 用途 |
| --- | --- |
| `IUnitOfWorkManager.Current` | 当前工作单元；无则为 `null` |
| `IUnitOfWorkManager.BeginAsync(options?, requiresNew)` | 创建或复用工作单元 |
| `IUnitOfWork.SaveChangesAsync` | 冲刷挂起变更，不提交事务 |
| `IUnitOfWork.CompleteAsync` | 完成并提交；不可重复调用 |
| `IUnitOfWork.RollbackAsync` | 回滚；幂等 |
| `[UnitOfWork]` | 声明工作单元边界并可覆盖选项 |
| `[UnitOfWorkEventHandler]` | 声明事件处理阶段 |
| `IDbContextProvider<TDbContext>` | 获取受当前工作单元管理的 DbContext |
| `IDatabaseApi` / `ITransactionApi` | 持久化提供方的数据库与事务扩展点 |
| `UnitOfWorkContext.CurrentPhase` | 当前事件阶段；提交流程外为 `null` |

## 注册形式约束

动态代理只能织入可见的实现类型。能从服务描述符中确定的无效形态会在容器构建时被拒绝：

| 注册形式 | 结果 |
| --- | --- |
| `AddScoped<IFoo, Foo>()` | 支持 |
| `AddScoped(typeof(IFoo<>), typeof(Foo<>))` | 拒绝；开放泛型无法织入 |
| `AddScoped<IFoo>(sp => new Foo(...))` | 不支持且无法自动检测 |
| 封闭的 `IEventHandler<T>` 实现、实例或同实例别名 | 支持 |
| 开放泛型 `IEventHandler<>` | 拒绝；改为逐事件封闭注册 |

`[UnitOfWork]` 只能标在类或实现方法上，不能标在接口上。需要事务边界的服务应使用实现类型注册，不使用隐藏实现类型的工厂委托。

## 配置项

`AddUnitOfWork(IConfiguration)` 绑定 `Leistd:UnitOfWork`；委托重载配置同一组选项。

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `IsTransactional` | `true` | 是否开启数据库事务 |
| `IsolationLevel` | `null` | 事务隔离级别；默认使用数据库设置 |
| `Timeout` | `null` | EF Core 关系数据库命令超时 |

## 注意事项

- 直接 `new` 的对象不会被拦截；工厂委托隐藏实现类型时也无法织入特性。
- 工作单元内不回查刚写入的行。优先使用现有实体；只有需要数据库回填值时手动冲刷。
- 嵌套调用只由最外层提交；不要依赖子工作单元的 `CompleteAsync()` 立即落库。
- `BeforeCommit` 仅承载必须影响事务的逻辑。发通知、刷缓存与远程调用放在 `AfterCommit` 或 Outbox。
- 非事务工作单元不承诺整体回滚，也不提供跨多个物理事务的原子性。
- `RollbackAsync` 是幂等的；`CompleteAsync` 不可重复调用。

## 相关

- [依赖注入](./dependency-injection.md)
- [面向切面编程](./aop.md)
- [事件总线](./event-bus.md)
- [连接解析](./data.md)
