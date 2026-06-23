# 事件总线（`event-bus`）
> 进程内（本地）事件的发布/订阅抽象与实现：发布事件后由 DI 容器中注册的处理器同步消费。

## 包

| 包 | 角色 | 何时引用 |
| --- | --- | --- |
| `Leistd.EventBus.Core` | 核心抽象：事件接口/基类、事件总线接口、事件处理器接口 | 定义事件、编写事件处理器时引用 |
| `Leistd.EventBus.Local` | 进程内事件总线实现 + DI 注册扩展 | 应用启动组装、需要实际发布事件时引用 |

## 核心抽象

均位于 `Leistd.EventBus.Core`。

### `IEvent`（`Leistd.EventBus.Core.Event`）
事件接口，所有事件的根。

```csharp
Guid EventId { get; }       // 事件 ID，用于幂等性与追踪
DateTime OccurredOn { get; } // 事件发生时间
```

### `BaseEvent` / `ILocalEvent` / `LocalEvent`（`Leistd.EventBus.Core.Event`）
- `BaseEvent`：`IEvent` 的抽象基类，构造时自动生成 `EventId`（`Guid.NewGuid()`）并设 `OccurredOn = DateTime.UtcNow`。标注 `[Serializable]`。
- `ILocalEvent : IEvent`：本地（进程内）事件标记接口。
- `LocalEvent : BaseEvent, ILocalEvent`：本地事件抽象基类，自定义本地事件通常继承它。

### `IEventBus`（`Leistd.EventBus.Core.EventBus`）
事件总线接口。

```csharp
Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent; // 泛型发布，编译期已知事件类型
Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default); // 非泛型发布，按运行时实际类型分发
```

### `ILocalEventBus`（`Leistd.EventBus.Core.EventBus`）
```csharp
public interface ILocalEventBus : IEventBus { }
```
本地事件总线标记接口，无新增成员，用于按本地总线语义注入。

### `IEventHandler<in TEvent>`（`Leistd.EventBus.Core.EventHandler`）
事件处理器接口，`TEvent : IEvent`。

```csharp
Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default); // 处理一个事件
```

## 能力实现

### `Leistd.EventBus.Local`
DI 注册扩展（`Leistd.EventBus.Local.DependencyInjection`）：

```csharp
IServiceCollection AddLocalEventBus(this IServiceCollection services);
```

将 `LocalEventBus` 注册为 **Singleton**，并以同一实例同时满足 `IEventBus` 与 `ILocalEventBus`。

行为要点（源码确有语义）：
- **生命周期与 Scope**：总线本身为 Singleton；每次 `PublishAsync` 内部通过 `IServiceScopeFactory` 创建独立 Scope，再用 `GetServices<IEventHandler<TEvent>>()` 解析该事件的全部处理器，因此可安全消费 Scoped 处理器。适用于 Web、Console、BackgroundService。
- **同步顺序执行**：处理器按解析顺序 `foreach` 依次 `await`，非并行。
- **无处理器**：泛型 `PublishAsync` 解析不到处理器时直接返回；仅在 `Debug` 级别记录一条日志，避免生产刷屏。
- **异常传播**：泛型路径中任一处理器抛异常，会先 `LogError`（含事件类型、`EventId`、处理器名）再 **重新抛出**，以保证调用方事务一致性；后续处理器不再执行。
- **非泛型分发**：`PublishAsync(IEvent)` 通过内部 `EventHandlerWrapperImpl<>`（按事件运行时类型缓存于 `ConcurrentDictionary`）恢复泛型上下文后分发；该路径不含上述无处理器降级日志与 try/catch 包装。

## 最小可用示例

```csharp
using Leistd.EventBus.Core.Event;
using Leistd.EventBus.Core.EventBus;
using Leistd.EventBus.Core.EventHandler;
using Leistd.EventBus.Local;
using Microsoft.Extensions.DependencyInjection;

// 1. 定义事件
public class OrderCreatedEvent : LocalEvent
{
    public string OrderNo { get; init; } = "";
}

// 2. 定义处理器
public class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
{
    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken ct = default)
    {
        Console.WriteLine($"订单已创建: {@event.OrderNo} ({@event.EventId})");
        return Task.CompletedTask;
    }
}

// 3. 注册
var services = new ServiceCollection();
services.AddLogging();
services.AddLocalEventBus();
services.AddScoped<IEventHandler<OrderCreatedEvent>, OrderCreatedHandler>();
var sp = services.BuildServiceProvider();

// 4. 发布
var bus = sp.GetRequiredService<IEventBus>();
await bus.PublishAsync(new OrderCreatedEvent { OrderNo = "SO-1001" });
```

## 依赖

- `Leistd.Core`（`Leistd.EventBus.Core` 引用）。

## 备注

- 处理器需自行向 DI 注册（如 `AddScoped<IEventHandler<TEvent>, ...>()`），总线不做扫描自动注册。
- 仅提供进程内（Local）实现，无跨进程/分布式总线实现，`ILocalEventBus` 与 `IEventBus` 当前指向同一 `LocalEventBus` 实例。
- 发布为同步等待全部处理器完成；若需后台异步消费，应由处理器内部自行处理。
