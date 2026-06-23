# 事件总线

事件总线用于在应用内部以**发布/订阅**方式解耦业务逻辑：发布方只负责"发生了什么"（发布事件），不关心谁来响应；订阅方各自实现处理器，互不感知。典型场景包括：聚合根状态变更后触发副作用（发送通知、写审计日志、刷新缓存）、应用服务完成主流程后派发后续动作、把横切关注点从核心业务流程中剥离。

Leistd 通过 `IEventBus` 抽象屏蔽底层实现，业务代码只依赖接口发布事件、只实现 `IEventHandler<TEvent>` 订阅事件。当前提供进程内（本地）实现 `Leistd.EventBus.Local`，可在不改调用代码的前提下平滑升级到未来的分布式实现。

## 何时使用

| 场景 | 推荐 |
| --- | --- |
| 单进程内解耦业务副作用（领域事件、应用内通知），发布方与处理器同进程 | `Leistd.EventBus.Local` |
| 仅编写业务代码（发布事件 / 实现处理器），不关心实现 | 只引用 `Leistd.EventBus.Core` 中的接口 |
| 需要跨进程/跨服务投递（消息队列） | 当前分组未提供，需另选分布式总线 |

> 本地事件总线在发布方的调用上下文中**同步等待**所有处理器执行完毕（详见[实现行为](#实现行为)），适合需要与发布方共享事务、即时反馈的场景，而非"发后即忘"的异步投递。

## 安装

```bash
# 抽象（业务代码引用；实现包已传递引用，通常无需单独添加）
dotnet add package Leistd.EventBus.Core

# 本地（进程内）实现
dotnet add package Leistd.EventBus.Local
```

> 本仓库的模板项目通过中央包管理（CPM）统一版本，添加时无需写版本号。

## 配置 Provider

在 `Program.cs` 注册本地事件总线：

```csharp
builder.Services.AddLocalEventBus();
```

`AddLocalEventBus` 以 **Singleton** 注册 `LocalEventBus`，并将同一实例同时绑定到 `IEventBus` 与 `ILocalEventBus`，注入任一接口都可发布事件。

事件处理器需**自行注册**（总线不做程序集扫描）。处理器在每次发布时通过独立 Scope 解析，因此 Scoped 注册可正常工作：

```csharp
builder.Services.AddScoped<IEventHandler<OrderCreatedEvent>, OrderCreatedHandler>();
```

## 使用

发布方注入 `IEventBus`，发布一个事件对象：

```csharp
public class OrderAppService(IEventBus eventBus)
{
    public async Task PlaceOrderAsync(Order order)
    {
        // —— 主业务流程 ——
        await eventBus.PublishAsync(new OrderCreatedEvent { OrderNo = order.No });
    }
}

// 事件：继承 LocalEvent 自动获得 EventId / OccurredOn
public class OrderCreatedEvent : LocalEvent
{
    public string OrderNo { get; init; } = "";
}
```

订阅方实现 `IEventHandler<TEvent>` 并注册到 DI：

```csharp
public class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
{
    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        // —— 响应事件：发通知 / 写日志 / 刷缓存 ——
        return Task.CompletedTask;
    }
}
```

同一事件可注册多个处理器，发布时会依次全部执行。

## 接口参考

`Leistd.EventBus.Core` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `IEventBus` | 事件总线统一接口，发布事件的定义方 |
| `IEventBus.PublishAsync<TEvent>(@event, ct)` | 泛型发布，编译期确定事件类型，按 `TEvent` 解析处理器 |
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
- **异常传播**：泛型 `PublishAsync<TEvent>` 中任一处理器抛异常时，先 `LogError`（含事件类型、`EventId`、处理器名）再**向上重新抛出**并中断后续处理器，以保证调用方事务一致性。
- 未解析到任何处理器时，泛型重载仅在 `Debug` 级别记录一条日志后直接返回，不报错（避免生产环境刷屏）。
- 非泛型 `PublishAsync(IEvent)` 按事件运行时类型解析处理器，内部用 `ConcurrentDictionary` 缓存 `EventHandlerWrapperImpl<>` 以恢复泛型上下文；该路径不含上述无处理器降级日志与逐处理器 try/catch 包装。

## 配置项 / Options

当前无配置项：`AddLocalEventBus` 无参数，本地实现也未暴露 Options 类。

## 注意事项

- 处理器**不会自动注册**，必须显式 `AddScoped`/`AddTransient`/`AddSingleton` 注册 `IEventHandler<TEvent>`，否则发布时找不到处理器（泛型重载静默返回）。
- 本地总线为**同步语义**：处理器耗时直接计入发布方的调用时长；长耗时副作用应在处理器内部自行转为后台任务。
- 处理器异常会沿泛型 `PublishAsync<TEvent>` 抛回发布方并中断其余处理器；若需"尽力执行、互不影响"，请在处理器内部自行捕获异常。
- 仅进程内有效，无跨进程/持久化能力；`ILocalEventBus` 与 `IEventBus` 当前指向同一 `LocalEventBus` 实例，进程重启不保留未处理事件。

## 相关

- [组件总览](./README.md)
- [依赖注入](./dependency-injection.md)
