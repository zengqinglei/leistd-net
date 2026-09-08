# 事件总线

事件总线以发布/订阅解耦应用内部逻辑：发布方只声明「发生了什么」，订阅方实现 `IEventHandler<TEvent>` 各自响应、互不感知。`Leistd.EventBus.Local` 在发布方进程内同步消费。

## 何时使用

| 场景 | 推荐 |
| --- | --- |
| 单进程内解耦业务副作用（领域事件、应用内通知），发布方与处理器同进程 | `Leistd.EventBus.Local` |
| 仅编写业务代码（发布事件 / 实现处理器），不关心实现 | 只引用 `Leistd.EventBus.Core` 中的接口 |
| 需要跨进程/跨服务投递（消息队列） | 当前分组未提供，需另选分布式总线 |

默认情况下，发布方会等待所有处理器完成。活动工作单元中的本地事件则推迟到指定提交阶段。

## 安装

```bash
dotnet add package Leistd.EventBus.Core

dotnet add package Leistd.EventBus.Local
```

## 注册

在 `Program.cs` 注册本地事件总线：

```csharp
builder.Services.AddLocalEventBus();
```

`AddLocalEventBus` 以 **Singleton** 注册 `LocalEventBus`，并将同一实例同时绑定到 `IEventBus` 与 `ILocalEventBus`，注入任一接口都可发布事件。

事件处理器需**自行注册**（总线不做程序集扫描）。处理器在每次发布时通过独立 Scope 解析，因此 Scoped 注册可正常工作：

```csharp
builder.Services.AddScoped<IEventHandler<OrderPlacedEvent>, OrderPlacedHandler>();
```

## 使用

发布方注入 `IEventBus`，发布一个事件对象：

```csharp
public class OrderNotifier(IEventBus eventBus)
{
    public async Task NotifyPlacedAsync(string orderNo, CancellationToken ct = default)
    {
        await eventBus.PublishAsync(new OrderPlacedEvent { OrderNo = orderNo }, ct);
    }
}

public class OrderPlacedEvent : LocalEvent
{
    public string OrderNo { get; init; } = "";
}
```

DDD 项目中由聚合记录并随保存发布事件的组合方式见 [DDD 四层基座](../ddd-struct/ddd-struct.md)。

订阅方实现 `IEventHandler<TEvent>` 并注册到 DI：

```csharp
public class OrderPlacedHandler : IEventHandler<OrderPlacedEvent>
{
    public Task HandleAsync(OrderPlacedEvent @event, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
```

同一事件可注册多个处理器，发布时会依次全部执行。

## 接口参考

`Leistd.EventBus.Core` 包的 API 位于 `Leistd.EventBus.Abstractions`、`Leistd.EventBus.Events` 与 `Leistd.EventBus.EventHandlers`：

| 成员 | 说明 |
| --- | --- |
| `IEventBus` | 事件总线统一接口，发布事件的定义方 |
| `IEventBus.PublishAsync<TEvent>(@event, ct)` | 泛型发布；按运行时类型解析处理器 |
| `IEventBus.PublishAsync(IEvent @event, ct)` | 非泛型发布，按事件运行时实际类型解析处理器 |
| `ILocalEventBus : IEventBus` | 本地事件总线标记接口，表达"进程内事件"的依赖意图，无新增成员 |
| `IEventHandler<in TEvent>` | 事件处理器接口（`TEvent : IEvent`），实现 `HandleAsync` 订阅事件 |
| `IEvent` | 事件接口，含 `EventId`（Guid，用于幂等/追踪）与 `OccurredOn`（事件发生时间） |
| `ILocalEvent : IEvent` | 本地事件标记接口 |
| `BaseEvent : IEvent` | 事件抽象基类，构造时生成 `EventId`、设 `OccurredOn = DateTime.UtcNow`，标注 `[Serializable]` |
| `LocalEvent : BaseEvent, ILocalEvent` | 本地事件抽象基类，业务事件通常继承它 |

## 实现行为

### Leistd.EventBus.Local（进程内本地总线）

- `LocalEventBus` 以 **Singleton** 全局共享一个实例；每次发布时通过 `IServiceScopeFactory` 创建**独立 Scope** 再 `GetServices<IEventHandler<TEvent>>()` 解析处理器，因而处理器可安全注册为 Scoped。适用于 Web、Console、BackgroundService。
- 处理器按解析顺序 `foreach` **串行 `await`**（非并行），且在发布方上下文中同步等待全部完成，不是后台异步投递。
- 所有处理器都会执行；单个失败原样抛出，多个失败包装为 `AggregateException`，取消异常不参与聚合。
- 未解析到任何处理器时**静默返回**，不报错。
- 泛型与非泛型重载都按事件运行时类型解析处理器。

## 与工作单元的关系

`AddLocalEventBus` 除 `ILocalEventBus` 外还注册 `ILocalEventDispatcher`（同一个 `LocalEventBus` 实例）。两者分工：

| 接口 | 谁用 | 行为 |
| --- | --- | --- |
| `ILocalEventBus` | 业务代码 | 发布前先问 `ILocalEventDeferrer`；有活动事务边界则**推迟入队**，否则立即分发 |
| `ILocalEventDispatcher` | 实现事务边界的组件 | **绕过推迟**，立即分发。边界排空自己的待发队列时走这条 |
| `ILocalEventDeferrer` | 由事务边界组件**实现** | 契约在本包，实现在工作单元组件；未注册实现时总线一律立即分发 |

`ILocalEventDispatcher` 绕过推迟，避免工作单元排空事件时重新入队；处理器内新发布的事件仍会进入下一轮排空。

> **替换默认本地总线时**：自定义实现必须同时提供语义一致的 `ILocalEventDispatcher`。只替换 `ILocalEventBus` 会形成两条分发管道——业务发布走自定义总线，而工作单元排空走默认 dispatcher。工作单元在有待发事件却取不到 `ILocalEventDispatcher` 时会直接抛出，不会静默丢弃事件。

## 注意事项

- 处理器**不会自动注册**，必须显式 `AddScoped`/`AddTransient`/`AddSingleton` 注册 `IEventHandler<TEvent>`，否则发布时找不到处理器（静默返回，不报错）。
- 本地总线为**同步语义**：处理器耗时直接计入发布方的调用时长；长耗时副作用应在处理器内部自行转为后台任务。**例外**是活动工作单元内的发布——那只是入队并立即返回，处理器在工作单元完成时执行，见[与工作单元的关系](#与工作单元的关系)。
- 仅进程内有效，无跨进程/持久化能力；`ILocalEventBus` 与 `IEventBus` 当前指向同一 `LocalEventBus` 实例，进程重启不保留未处理事件。
