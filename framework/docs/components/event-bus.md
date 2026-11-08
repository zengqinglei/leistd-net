# 事件总线

事件总线以发布/订阅解耦应用内部逻辑：发布方只声明「发生了什么」，订阅方实现 `IEventHandler<TEvent>` 各自响应、互不感知。`Leistd.EventBus.Local` 在发布方进程内同步消费；分布式总线通过事务发件箱、持久收件箱及 RabbitMQ、Kafka 或自定义传输跨服务投递。

## 何时使用

| 场景 | 推荐 |
| --- | --- |
| 单进程内解耦业务副作用（领域事件、应用内通知），发布方与处理器同进程 | `Leistd.EventBus.Local` |
| 仅编写业务代码（发布事件 / 实现处理器），不关心实现 | 只引用 `Leistd.EventBus.Core` 中的接口 |
| 跨进程传递已经发生的事实，允许异步到达 | `Leistd.EventBus.Distributed` + EF 存储 + 一个传输 |
| 需要立即返回结果的跨服务查询或命令 | 服务客户端 |

默认情况下，发布方会等待所有处理器完成。活动工作单元中的本地事件则推迟到指定提交阶段。

## 安装

```bash
dotnet add package Leistd.EventBus.Core

dotnet add package Leistd.EventBus.Local

# 分布式部署再选择存储、端点与一个传输
dotnet add package Leistd.EventBus.EntityFrameworkCore
dotnet add package Leistd.EventBus.AspNetCore
dotnet add package Leistd.EventBus.RabbitMQ
```

## 注册

在 `Program.cs` 注册本地事件总线：

```csharp
builder.Services.AddLocalEventBus();
```

`AddLocalEventBus` 以 Singleton 注册 `LocalEventBus` 并绑定到 `ILocalEventBus`，发布方注入它。`IEventBus` 只是各类总线的共同基接口，不注册为服务。可重复调用，不会重复注册。

事件处理器需自行注册（总线不做程序集扫描）。处理器在每次发布时通过独立 Scope 解析，因此 Scoped 注册可正常工作：

```csharp
builder.Services.AddScoped<IEventHandler<OrderPlacedEvent>, OrderPlacedHandler>();
```

### 分布式总线

宿主先配置工作单元与 EF 适配、当前租户上下文、后台队列和调度器；多副本还需共享分布式锁。管理动作通过本地事件留痕，因此也需本地总线及工作单元的事件阶段织入。DDD 基座已组合其中的工作单元与本地总线，勿重复建立平行事务。

```csharp
builder.Services.AddDistributedEventBus();
builder.Services.AddDistributedEventBusEfCore<AppDbContext>();
builder.Services.AddDistributedEventRetention<AppDbContext>();
builder.Services.AddRabbitMqEventTransport();
builder.Services.AddHealthChecks().AddRabbitMqEventTransport(tags: ["ready"]);
builder.Services.AddDistributedEventHandler<OrderPaidHandler>("orders.projection");
```

三表在同一个业务 DbContext 中映射，发布才能与业务写入共同提交；重复登记不同上下文会在注册时失败。

```csharp
using Microsoft.EntityFrameworkCore;

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ConfigureDistributedEventBus();
}
```

管理组必须给出宿主已登记的 Read 与 Manage 策略；漏存储或策略时映射即报错。列表和详情概要属于 Read，所有载荷与写操作属于 Manage。

```csharp
app.MapGroup("/api/v1/distributed-events").MapDistributedEvents(options =>
{
    options.ReadPolicy = "events.read";
    options.ManagePolicy = "events.manage";
});
```

将 RabbitMQ 换为 Kafka 时引用 `Leistd.EventBus.Kafka`，在服务注册与健康检查注册两处用 `AddKafkaEventTransport` 替代 `AddRabbitMqEventTransport`；每个服务只能选择一个传输。回环也遵守 `Inbox.ReceiveEnabled`，禁用时不会调用接收入口，发送仍按无订阅者处理。测试或单进程开发可改用 `AddLoopbackEventTransport`，并显式用 `AddHealthChecks().AddLoopbackEventTransport(tags)` 登记其始终 Healthy 的检查。

## 使用

发布方注入 `ILocalEventBus`，发布一个事件对象：

```csharp
public class OrderNotifier(ILocalEventBus eventBus)
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

### 分布式发布与消费

共享契约包只引用 Core，使用稳定事件名。`EventId` 与 `OccurredOn` 在序列化后必须可还原；继承 `DistributedEvent` 已提供 init 元数据。排序键的范围是同租户、同事件名，消费者一侧还按消费者名区分。

```csharp
[EventName("order.paid")]
public sealed class OrderPaidEvent : DistributedEvent, IOrderedDistributedEvent
{
    public string OrderId { get; init; } = "";
    public string OrderingKey => "order:" + OrderId;
}

public sealed class OrderPaidHandler : IEventHandler<OrderPaidEvent>
{
    public Task HandleAsync(OrderPaidEvent @event, CancellationToken cancellationToken = default)
    {
        // 业务投影经 IDbContextProvider 取当前上下文，与收件箱标记在同一事务提交。
        return Task.CompletedTask;
    }
}
```

发布要求活动工作单元；载荷采用 System.Text.Json Web 默认并在调用时冻结。正常业务可用工作单元特性，也可显式开启：

```csharp
public sealed class OrderPaymentPublisher(IDistributedEventBus events, IUnitOfWorkManager units)
{
    public async Task PublishAsync(string orderId, CancellationToken ct)
    {
        using var unit = units.Begin();
        await events.PublishAsync(new OrderPaidEvent { OrderId = orderId }, ct);
        await unit.CompleteAsync(ct);
    }
}
```

本地领域事件转成分布式事件时，转发处理器声明 `UnitOfWorkEventHandler(UnitOfWorkPhase.BeforeCommit)`，确保写发件箱仍在原业务事务内。分布式消费者经 `AddDistributedEventHandler` 显式登记，不扫描程序集，也不登记给本地总线；消费者名参与去重，更名处理器时应保留原消费者名。

## 接口参考

`Leistd.EventBus.Core` 包的 API 位于 `Leistd.EventBus.Abstractions`、`Leistd.EventBus.Events` 与 `Leistd.EventBus.EventHandlers`：

| 成员 | 说明 |
| --- | --- |
| `IEventBus` | 各类总线的共同基接口（发布方法的定义方）；不注册为服务，发布方注入具体的 `ILocalEventBus` 或 `IDistributedEventBus` |
| `IEventBus.PublishAsync<TEvent>(@event, ct)` | 泛型发布；按运行时类型解析处理器 |
| `IEventBus.PublishAsync(IEvent @event, ct)` | 非泛型发布，按事件运行时实际类型解析处理器 |
| `ILocalEventBus : IEventBus` | 进程内事件总线：提交后分发、不序列化；发布方注入它 |
| `IEventHandler<in TEvent>` | 事件处理器接口（`TEvent : IEvent`），实现 `HandleAsync` 订阅事件 |
| `IEvent` | 事件接口，含 `EventId`（Guid，用于幂等/追踪）与 `OccurredOn`（事件发生时间） |
| `ILocalEvent : IEvent` | 本地事件标记接口 |
| `BaseEvent : IEvent` | 事件抽象基类，构造时生成有序 Guid 与 UTC 发生时间；元数据可通过 init 还原 |
| `LocalEvent : BaseEvent, ILocalEvent` | 本地事件抽象基类，业务事件通常继承它；无参构造取系统当前时刻，`LocalEvent(occurredOn)` 用发布方给的时刻——发布方已从可替换的时间源（如 `IClock`）取了"现在"时用后者 |

## 实现行为

### Leistd.EventBus.Local（进程内本地总线）

- 每次发布在独立 Scope 中解析 `IEventHandler<TEvent>`，处理器可注册为 Scoped。
- 处理器按解析顺序串行 `await`（非并行），发布方等待全部完成，不是后台异步投递。
- 所有处理器都会执行；单个失败原样抛出，多个失败包装为 `AggregateException`，取消异常不参与聚合。
- 未解析到任何处理器时直接返回，不报错。
- 泛型与非泛型重载都按事件运行时类型解析处理器。

## 与工作单元的关系

`AddLocalEventBus` 除 `ILocalEventBus` 外还注册 `ILocalEventDispatcher`（同一个 `LocalEventBus` 实例）。两者分工：

| 接口 | 谁用 | 行为 |
| --- | --- | --- |
| `ILocalEventBus` | 业务代码 | 发布前先问 `ILocalEventDeferrer`；有活动事务边界则**推迟入队**，否则立即分发 |
| `ILocalEventDispatcher` | 实现事务边界的组件 | **绕过推迟**，立即分发。边界排空自己的待发队列时走这条 |
| `ILocalEventDeferrer` | 由事务边界组件**实现** | 契约在本包，实现在工作单元组件；未注册实现时总线一律立即分发 |

`ILocalEventDispatcher` 绕过推迟，避免工作单元排空事件时重新入队；处理器内新发布的事件仍会进入下一轮排空。

> 替换默认本地总线时，在 `AddLocalEventBus()` 之前注册自定义 `ILocalEventBus`，并同时提供语义一致的 `ILocalEventDispatcher`，否则工作单元排空仍走默认实现。工作单元在有待发事件却取不到 `ILocalEventDispatcher` 时抛出。

## 注意事项

以下三条限于本地总线：

- 处理器不会自动注册，必须显式注册 `IEventHandler<TEvent>`，否则发布时找不到处理器且不报错。
- 处理器耗时计入发布方的调用时长，长耗时副作用应转为后台任务。活动工作单元内的发布只入队并立即返回，处理器在工作单元完成时执行，见[与工作单元的关系](#与工作单元的关系)。
- 仅进程内有效，无跨进程/持久化能力；进程重启不保留未处理事件。


### 分布式一致性与顺序

| 承诺 | 边界 |
| --- | --- |
| 发布与业务写入共同提交或回滚 | 发件箱和业务实体必须使用同一工作单元与 DbContext；无活动边界时拒绝发布。 |
| 至少一次送达 | 提交后立即队列降低延迟，持久扫描兜底；结果未知也重发，收件箱按租户、EventId、消费者去重。 |
| 本服务数据库写入只生效一次 | 处理、AttemptCount 并发令牌递增和 Processed 标记同事务；冲突整笔回滚，不能再写失败覆盖胜方。 |
| 处理器外部副作用至少一次 | 调用外部系统仍须由处理器按 EventId 幂等。 |
| 单键有序 | 同租户、同事件名、同键的前序 Pending 阻塞后续；消费者再按消费者名分开。死信和 Failed 不阻塞后续，业务应容忍版本跳号。 |
| 去重窗口等于保留期 | 默认十四天；删除终态后的迟到重投须由业务约束兜底。 |

同键并发事务的先后以提交后可见的自增 Id 为准，不保证业务版本顺序。RabbitMQ 首次交付用单活队列串行入库；Kafka 同键在一个主题的同分区。故障重投可能迟到，不承诺跨故障重排或跨主题顺序。

### 分工与配置

`Leistd:EventBus:Distributed` 的 `Outbox:DispatchEnabled`、`Inbox:ReceiveEnabled`、`Inbox:ProcessingEnabled` 默认均为 true。写入进程可显式全关并不注册传输；Worker 全开并注册传输。关闭的扫描任务在调度器取锁前跳过，选项不按可选依赖是否存在推断。发送或接收开启却漏传输时启动报出配置键。

默认发件箱每五秒兜底扫描；瞬态发送失败指数退避，从两秒起、最多五分钟并有抖动。首次失败满一天仍未恢复则进死信。收件箱每轮最多处理五次。立即队列满或进程退出不会丢失持久记录，新记录留一分钟宽限后由扫描接手。

`TestEvent:Enabled` 默认 true，登记 `event-bus.test` 测试消费者；不允许本服务自发自收的传输可关闭，状态和界面随之隐藏测试入口。EF 保留期由 `AddDistributedEventRetention` 独立绑定 `Leistd:EventBus:Distributed:Retention`，默认 `Enabled=true`、`Days=14`，每天 UTC 02:00 清理最后变更已过期的终态；Pending 永不删除。

| 传输 | 必需连接 | 默认部署选项 |
| --- | --- | --- |
| RabbitMQ | `ConnectionStrings:RabbitMQ`，amqp/amqps URI | `Leistd:EventBus:RabbitMQ`：持久 topic 交换机 `leistd.events`，队列取宿主应用名，预取 32，单活且入库串行。服务之间用不同队列名，副本共用队列。 |
| Kafka | `ConnectionStrings:Kafka`，bootstrap servers | `Leistd:EventBus:Kafka`：主题前缀空、组名取宿主应用名，`AutoCreateTopics=true` 创建缺失的单分区主题；关闭后由运维预建。服务之间用不同组名，副本共用组。 |

宿主通过 `AddHealthChecks().AddRabbitMqEventTransport(tags)` 或 `AddKafkaEventTransport(tags)` 显式登记健康检查，附加标签合并且固定带 `event-bus`；没有相关健康检查时快照报告 Unknown。RabbitMQ 发布等待确认，按标准发布／订阅语义允许没有订阅队列。消费方须先启动以声明队列，此前发布的消息不会保留；需要离线接收时先完成队列部署。持久接收或拒收成功才 Ack；Kafka 发送要求 Persisted，禁用自动位点存储和提交，只在持久接收成功后提交当前分区的 offset+1。接收持久失败时保留当前消息重试，不越过它确认后续。

### 管理与排障

宿主按物理库查看发件箱与收件箱，默认宿主库；库目录只返回代表租户与租户清单，不暴露连接串。租户始终只见自己，传 database 参数返回 HostOnly。拒收集中宿主库且仅宿主可访问；切换物理库不改变拒收范围。宿主选择其他库时不能借测试事件冒充该租户。

Read 只读快照、目录、列表与详情概要，载荷经独立 Manage 端点读取。自增 Id 输出为十进制字符串，前端不得转为 number。列表按 Id 倒序，创建／接收时间上限固定为首次加载时刻。

| 动作 | 允许状态 | 效果 |
| --- | --- | --- |
| 发件箱重投 | DeadLettered | EventId 不变，清空失败窗口，累计尝试次数保留，RedeliverCount 增加。 |
| 收件箱重放 | Failed、Processed | 清零本轮尝试、错误与完成时间，ReplayCount 增加；Processed 会再次执行处理器及其外部副作用。 |
| 拒收重新解码 | Pending | 原始字节重新经过当前 DecodeAsync 和接收校验；先写有去重保护的收件箱，再标 Redecoded，不追求跨库原子。失败更新原拒收。 |
| 拒收丢弃 | Pending | 标 Discarded。 |

ids 与 filter 二选一，ids 最多 100；filter 必须带 UTC 时间上限。按条件恢复每批最多 500 条，逐批事务提交，改回 Pending 后交给扫描，不灌进立即队列。每个成功批次发布一条 `DistributedEventManagedEvent`，带实际条数与原筛选或实际 ids；部分批次失败不会把未完成批次记为成功。

积压先查传输健康、连接和最旧时长。永久失败直接死信；连接中断、超时、凭据过期等通道故障是瞬态，修复后自动重试。死信修复消息大小等不可恢复的单条消息错误后重投；消费者故障修复后重放；拒收修复映射、租户或载荷后重新解码。事件目录合并订阅与保留期历史，用最近发送／接收／处理时间判断某类事件是否静默，不把无活动本身判成故障。

活动源为 `Leistd.EventBus`。发布写入 W3C traceparent、tracestate 和 leistd-correlation-id，处理时恢复链路、关联标识、所属租户与系统身份。稳定日志事件包括 DistributedEventDeadLettered、DistributedEventOutboxBacklog、DistributedEventConsumeFailed、DistributedEventRejected、DistributedEventRejectPersistFailed。

### 自定义传输

实现 `IDistributedEventTransport` 的发送与异步 DecodeAsync，并编写消费循环，在消息作用域内调用 `IDistributedEventReceiver`。传输只映射信封与线上格式，不负责业务处理器事务：

1. 成功须等待中枢确认；结果未知、连接或鉴权故障返回瞬态；只有该条消息原样重发永远失败才返回永久失败及稳定码。取消向上传播。
2. DecodeAsync 可从外部载荷提取业务 ID、租户与发生时间，也可异步访问 schema；不可恢复格式错误抛 `DistributedEventDecodeException`，临时依赖故障照常抛出，不能误写拒收。
3. 传输层已判坏的消息调用 RejectAsync。ReceiveAsync／RejectAsync 成功返回后才确认；失败时保留当前消息或重新投递，不提交该分区后续位点。遵守 Inbox:ReceiveEnabled，保留传输支持范围内的接收次序。
4. RawDistributedEvent 的短生命周期 SDK 缓冲区先复制；消息头承载链路，坐标只作诊断。登记带 event-bus 标签的健康检查。中枢专有的重同步、删除标记等能力由业务直接用 SDK，不扩充通用信封。
