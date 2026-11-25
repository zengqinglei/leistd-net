# 事件总线

事件总线以发布/订阅解耦应用内部逻辑：发布方只声明「发生了什么」，订阅方实现 `IEventHandler<TEvent>` 各自响应、互不感知。`Leistd.EventBus.Local` 在发布方进程内同步消费；分布式总线通过事务发件箱、持久收件箱及 RabbitMQ、Kafka 或自定义传输跨服务投递。

## 何时使用

| 场景 | 推荐 |
| --- | --- |
| 单进程内解耦业务副作用（领域事件、应用内通知），发布方与处理器同进程 | `Leistd.EventBus.Local` |
| 仅编写业务代码（发布事件 / 实现处理器），不关心实现 | 只引用 `Leistd.EventBus.Core` 中的接口 |
| 跨进程传递已经发生的事实，允许异步到达 | `Leistd.EventBus.Distributed` + EF 存储 + 一个传输 |
| 提交后必须由本服务 Worker 可靠完成（需要重试、死信与重放），事件不离开本服务；能在请求内直接调用、或按业务状态由周期任务补做的不必用事件（见[后台作业](background-jobs.md)） | 契约实现 `IInternalDistributedEvent`，见[本服务内事件](#本服务内事件) |
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

发件箱、收件箱、拒收与传输状态在同一个业务 DbContext 中映射，发布才能与业务写入共同提交；重复登记不同上下文会在注册时失败。

```csharp
using Microsoft.EntityFrameworkCore;

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ConfigureDistributedEventBus();
}
```

管理组必须给出宿主已登记的 Read 与 Manage 策略；漏存储或策略时映射即报错。列表和详情概要属于 Read，所有载荷与写操作属于 Manage。`EnablePayloadEndpoints` 默认 true；设为 false 时三个 `/payload` 路由均不映射（404），`/status` 的 `PayloadEnabled` 为 false，界面应隐藏载荷入口。直接调用管理服务时该字段为空。

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

同一工作单元内，同租户、同 EventId、事件名与冻结载荷相同的再次发布直接跳过，包括中途 SaveChanges 和参与父事务的嵌套单元；内容不同立即抛 InvalidOperationException，消息包含 EventId 与事件名。传播该异常会回滚业务事务。独立新工作单元的重复仍由数据库唯一约束拒绝，不在发布时查库去重。确定性 EventId 只代表同一事实，调用方应在产生新事实（新版本）时才发布。

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

跨事件名不保序。多个事件名共享版本序列且新状态完整覆盖旧状态时，消费方可按租户与聚合维护共同版本水位，忽略较旧版本；增量事实不能仅凭共同水位丢弃，所需顺序或补偿由业务保证。

同键并发事务的先后以提交后可见的自增 Id 为准，不保证业务版本顺序。RabbitMQ 首次交付用单活队列串行入库；Kafka 同键在一个主题的同分区。故障重投可能迟到，不承诺跨故障重排或跨主题顺序。

### 分工与配置

`Leistd:EventBus:Distributed` 的 `Outbox:DispatchEnabled`、`Inbox:ReceiveEnabled`、`Inbox:ProcessingEnabled` 默认均为 true。写入进程可显式全关并不注册传输；Worker 全开并注册传输。关闭的扫描任务在调度器取锁前跳过，选项不按可选依赖是否存在推断。三项任一开启却漏传输时，启动报出对应配置键。仅发布进程可通过 ValidateOnBuild 与 ValidateScopes 验证，发布和管理不要求连接 SDK。

```csharp
// Api：沿用宿主已有的工作单元、租户、后台任务队列与数据库接线。
services.AddDistributedEventBus(options =>
{
    options.Outbox.DispatchEnabled = false;
    options.Inbox.ReceiveEnabled = false;
    options.Inbox.ProcessingEnabled = false;
});
services.AddDistributedEventBusEfCore<AppDbContext>();
app.MapDistributedEvents();
```

Worker 共用数据库，注册所选传输并开启上述三项；仅发布 Api 不注册传输或该传输的健康检查。

默认发件箱每五秒兜底扫描；瞬态发送失败指数退避，从两秒起、最多五分钟并有抖动。首次失败满一天仍未恢复则进死信。收件箱每轮最多处理五次。新记录只在本进程提交后会立即发送或处理时留一分钟宽限，由立即路径先处理，立即队列满或进程退出时由宽限后的扫描接手，持久记录不丢失。没有立即路径的新记录立即可扫描：关闭 `Outbox:DispatchEnabled` 的写入进程发布的事件、关闭 `Inbox:ProcessingEnabled` 的进程接收的事件，以及按条件批量重新解码写入的收件箱，正常情况下由下一轮扫描（`DispatchInterval`）取走；集群锁、积压与重试都可能延长。

`TestEvent:Enabled` 默认 true，登记 `event-bus.test` 测试消费者；不允许本服务自发自收的传输可关闭，状态和界面随之隐藏测试入口。EF 保留期由 `AddDistributedEventRetention` 独立绑定 `Leistd:EventBus:Distributed:Retention`，默认 `Enabled=true`、`Days=14`，每天 UTC 02:00 清理最后变更已过期的终态；Pending 与拒收的 RedecodePending 永不删除。

| 传输 | 必需连接 | 默认部署选项 |
| --- | --- | --- |
| RabbitMQ | `ConnectionStrings:RabbitMQ`，amqp/amqps URI | `Leistd:EventBus:RabbitMQ`：持久 topic 交换机 `leistd.events`，队列取宿主应用名，预取 32，单活且入库串行。服务之间用不同队列名，副本共用队列。 |
| Kafka | `ConnectionStrings:Kafka`，bootstrap servers | `Leistd:EventBus:Kafka`：主题前缀空、组名取宿主应用名，`AutoCreateTopics=true` 创建缺失的单分区主题；关闭后由运维预建。服务之间用不同组名，副本共用组。 |

宿主通过 `AddHealthChecks().AddRabbitMqEventTransport(tags)` 或 `AddKafkaEventTransport(tags)` 显式登记健康检查，附加标签合并且固定带 `event-bus`；没有新鲜上报时快照报告 unknown。RabbitMQ 发布等待确认，按标准发布／订阅语义允许没有订阅队列。消费方须先启动以声明队列，此前发布的消息不会保留；需要离线接收时先完成队列部署。持久接收或拒收成功才 Ack；Kafka 发送要求 Persisted，禁用自动位点存储和提交，只在持久接收成功后提交当前分区的 offset+1。接收持久失败时保留当前消息重试，不越过它确认后续。

EF 后台服务只执行带 `event-bus` 标签的检查，并将每个传输进程的结果写入宿主库；没有注册传输或匹配检查时不写入，不修改宿主 `HealthCheckPublisherOptions.Predicate`。Api 即使不注册传输也能从同库读取 Worker 状态。上报失败只记警告，下轮重试，不影响收发。

`Leistd:EventBus:Distributed:TransportStatus` 的 `ReportInterval` 默认十五秒、`StaleAfter` 默认一分钟且须大于上报间隔；`Instance` 默认 `{ApplicationName}@{MachineName}:{ProcessId}`，显式覆盖须保证各并发实例唯一。配置无需另设部署变量。实际上报周期为间隔加检查与持久化耗时；StaleAfter 应大于间隔加最长检查耗时，并为实例间时钟偏差留余量，各实例应保持时钟同步。保留期任务会删除上报时间早于保留期的实例行。

快照只读宿主库新鲜报告，不在 GET 中执行健康检查；当前租户不改变来源。聚合取最差 `HealthStatus`：healthy 表示已连接，degraded 表示降级，unhealthy 表示断开且正在自动恢复，unknown 表示没有新鲜报告。`TransportStatusChangedAt` 为最差状态实例中最近的变化时刻，`TransportReportedAt` 为全部新鲜实例中最近的上报时刻；unknown 时二者为空。传输名优先当前进程，否则按最差状态、变化时刻降序、上报时刻降序、实例名升序选取。

### 本服务内事件

提交后交给本服务 Worker 异步处理、不应离开本服务的事件，例如替代按分钟轮询的后台处理，让契约实现 `IInternalDistributedEvent`。发件箱取到这类记录不调用传输，在同库直接为本服务登记的消费者写收件箱，入站坐标为 `transport=internal`；重试、死信、重投、收件箱去重与处理、管理端与普通分布式事件相同。契约通常放在本服务内，不进共享契约包，也可同时实现 `IOrderedDistributedEvent`。

```csharp
[EventName("order.fulfillment-requested")]
public sealed class OrderFulfillmentRequestedEvent : DistributedEvent, IInternalDistributedEvent
{
    public string OrderId { get; init; } = "";
}

public sealed class OrderFulfillmentHandler : IEventHandler<OrderFulfillmentRequestedEvent>
{
    public Task HandleAsync(OrderFulfillmentRequestedEvent @event, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
```

```csharp
builder.Services.AddDistributedEventHandler<OrderFulfillmentHandler>("orders.fulfillment");
```

发布照常使用 `IDistributedEventBus`。路由在发布时写入发件箱记录的消息头 `leistd-delivery: internal` 并随记录冻结，发件箱详情可见。默认的 `IDistributedEventSubscriptions.GetAll()` 只返回对外订阅，RabbitMQ、Kafka 与自定义传输不为本服务内事件建立绑定或主题；传输收到的同名消息不会进入内部消费者的收件箱。宿主替换该接口时，本服务内事件的消费者仍取自 `AddDistributedEventHandler` 的登记。收件箱处理、重放与事件目录同时包含对外订阅与内部消费者。开启发送仍须注册一个传输，只有本服务内事件的服务可使用回环。

本地投递按派发实例本机登记的内部消费者写收件箱，框架不做集群订阅发现。共库中所有开启 `Outbox:DispatchEnabled` 或 `Inbox:ProcessingEnabled` 的实例都必须登记相同、完整的内部消费者；`Inbox:ReceiveEnabled` 只控制传输入口，不影响本地投递。Api 只写、Worker 全开且使用同一镜像的分工天然满足这一前提。派发实例找不到任何内部消费者时进死信；只漏登部分消费者时，已登记的消费者照常收到、记录标为已发送，遗漏的消费者收不到，框架无法发现。

| LastErrorCode | 含义 | 处理 |
| --- | --- | --- |
| internal-no-consumer | 派发实例没有登记该事件的任何内部消费者 | 补齐登记后重投 |
| internal-invalid-payload | 载荷无法还原为消费者的事件类型 | 修正契约后重投 |
| internal-unknown-tenant | 记录可读写，但租户目录明确返回该租户不存在 | 确认租户后重投或保留死信 |

三者都直接进死信，不写拒收表。写收件箱或查询租户目录时抛出异常属于瞬态失败，按发送退避重试，LastErrorCode 为 transport-exception，警告日志注明失败发生在本地投递。

先把共库中能派发发件箱的全部实例升级到支持本服务内事件的版本，再开始发布：旧版本会把带标记的记录交给实际传输。回滚前先停止发布，排空或隔离带标记的待发记录，并暂停这类死信的重投，直到支持该标记的 Worker 恢复。已有事件名在对外与内部之间切换前，先排空该事件的待发记录，或改用新事件名：已写入的记录按发布时的路由投递，订阅视图则跟随当前契约。

本服务内事件替代的是"提交后尽快处理"的触发，不替代按业务状态对账：切换前已存在的待处理数据、耗尽重试进入 Failed 的收件箱记录不会自动补做。需要兜底时保留周期任务按业务状态补处理，或由管理端重放；处理器按业务状态保持幂等。

### 管理与排障

管理接口始终按当前租户定位数据库并过滤发件箱与收件箱。宿主只见 TenantId 为空的平台记录；租户只见本租户记录，包括共享库与独立库。接口不接受库或租户选择参数，也不提供库目录。拒收集中宿主库且仅宿主可访问。

Read 只读快照、目录、列表与详情概要，载荷经独立 Manage 端点读取。自增 Id 输出为十进制字符串，前端不得转为 number。列表按 Id 倒序，创建／接收时间上限固定为首次加载时刻（`before`）。可另传 `startTime`、`endTime` 按同一时间筛选（UTC，两端都包含），与 `before` 同时生效；发件箱比较创建时间，收件箱与拒收比较接收时间。查询参数接受带 `Z` 或显式偏移的 ISO 时刻，偏移换算成 UTC；不带时区返回验证错误。

| 动作 | 允许状态 | 效果 |
| --- | --- | --- |
| 发件箱重投 | DeadLettered | EventId 不变，清空失败窗口，累计尝试次数保留，RedeliverCount 增加。 |
| 收件箱重放 | Failed、Processed | 清零本轮尝试、错误与完成时间，ReplayCount 增加；Processed 会再次执行处理器及其外部副作用。 |
| 拒收重新解码 | Pending | 本地有同名传输时同步调用 DecodeAsync 与接收校验；否则转为 RedecodePending，由同名接收 Worker 处理。成功先写有去重保护的收件箱，再标 Redecoded；解码失败回 Pending 并更新脱敏原因。 |
| 拒收丢弃 | Pending、RedecodePending | 标 Discarded。 |

ids 与 filter 二选一，ids 最多 100；filter 必须带 UTC 时间上限 `before`，可带与列表相同的 `startTime`、`endTime`。JSON 里的时间须带 `Z`（带偏移或不带时区都返回验证错误），起始不得晚于结束。按条件恢复每批最多 500 条，逐批事务提交，改回 Pending 后交给扫描，不灌进立即队列。每个实际改动且提交的批次发布一条 `DistributedEventManagedEvent`，带目标 TargetTenantId、实际条数与原筛选或实际 ids。目标作用域退出后，在调用方租户与用户上下文中独立完成审计单元；调用方已有单元之后提交或回滚均不影响该审计。事件记录更新与独立审计不是原子事务：审计失败向上传播，已提交批次不撤回。部分批次失败不把未完成批次记为成功。

转交结果的 DeferredCount 是 AffectedCount 的子集，表示请求已登记而非已解码；该请求用 RedecodeRequested 动作审计。Worker 只领取宿主表中 Transport 与自身 Name 相同的 RedecodePending，沿用 Inbox:ReceiveEnabled 与 DispatchInterval，每批 100 行；单行执行失败记 4107 Warning，该行保留待办，继续后续行；整轮结束汇总失败并向调度器报错，下一轮重试。Pending 与 RedecodePending 都能丢弃；按筛选丢弃可用 Status 限定其中一种，省略时涵盖两种未解决状态。处理原始字节、头与入站坐标，不重新向 Broker 发布；进程中断后重入由 Inbox 唯一约束去重。Worker 完成不再以请求用户发管理审计，未解决拒收计数包含 RedecodePending。共库有多种传输接收宿主时，各自只领取同名行；集群任务按时段轮到谁由谁执行，因此可能有延迟。

DistributedEventManagementAction.RedecodeRequested 与 DistributedEventRejectionStatus.RedecodePending 是新增枚举成员，宿主的穷举 switch 必须覆盖；前者表示已转交请求，后者表示尚未解码的非终态。

积压先查传输健康、连接和最旧时长。永久失败直接死信；连接中断、超时、凭据过期等通道故障是瞬态，修复后自动重试。死信修复消息大小等不可恢复的单条消息错误后重投；消费者故障修复后重放；拒收修复映射、租户或载荷后重新解码。事件目录合并订阅与保留期历史，用最近发送／接收／处理时间判断某类事件是否静默，不把无活动本身判成故障。

活动源名为 "Leistd.EventBus"。发布写入 W3C traceparent、tracestate 和 leistd-correlation-id；发送恢复持久父上下文与关联标识，围绕 SendAsync 创建 Producer 类型的 event.send，立即发送与扫描重试共用此路径。消息头不改写，event.send 与 event.process 均以 event.publish 为父 span，三段同属一个 TraceId。处理时另恢复所属租户与系统身份。三类记录分别存储从有效 W3C traceparent 提取的小写 TraceId，以及符合 CorrelationIdConstants.IsWellFormed 的 leistd-correlation-id 头原始 CorrelationId（最长 64 字符；不合规时该列为空，原始头仍保留）；两者都可在列表关键字中精确检索。32 位十六进制关键字对 TraceId 转小写匹配，关联标识不归一化，匹配的大小写语义取决于数据库排序规则（例如 SQL Server 默认通常不区分大小写）。缺失或非法 traceparent 时 TraceId 为空；显式关联标识可与 TraceId 不同，无 Activity 的入口也可只携带关联标识。

稳定告警键如下，事件编号与名称一起使用：

| 编号 | 事件名 | 结构化上下文 |
| --- | --- | --- |
| 4101 | DistributedEventDeadLettered | EventId、EventName、ErrorCode |
| 4102 | DistributedEventOutboxBacklog | Database、Count、OldestAge |
| 4103 | DistributedEventConsumeFailed | EventId、EventName、Consumer |
| 4104 | DistributedEventRejected | Transport、Reason、EventName、EventId、Coordinates |
| 4105 | DistributedEventRejectPersistFailed | 同 4104，附完整持久化异常 |
| 4106 | DistributedEventConsumeRetry | EventId、EventName、Consumer、AttemptCount，附完整处理异常 |
| 4107 | DistributedEventRedecodeRetry | RecordId、Transport，附完整执行异常 |

拒收前无法解码时 EventName、EventId 可能为空。Coordinates 是入站坐标，按键排序为 `k=v;k=v`，最长 256 字符。发送传输未处理的异常只将异常类型全名写入 LastError，LastErrorCode 为 transport-exception；完整异常保留在日志。收件处理器失败同样只持久异常类型全名，未耗尽的每次失败由 4106 Warning 保留完整异常，耗尽时由 4103 Error 保留；LastErrorCode 仍为 handler-exception。

### 自定义传输

实现 `IDistributedEventTransport` 的发送与异步 DecodeAsync，并编写消费循环，在消息作用域内调用 `IDistributedEventReceiver`。传输只映射信封与线上格式，不负责业务处理器事务：

1. 发送结果 Error 必须先脱敏，不含凭据、连接串或载荷内容；将 SDK 异常转成稳定错误码及固定格式说明。组件不再次清洗该文本，只截断到 512 字符；未处理的发送异常及回环接收异常只持久化异常类型全名，完整异常写日志。成功须等待中枢确认；结果未知、连接或鉴权故障返回瞬态；只有该条消息原样重发永远失败才返回永久失败及稳定码。取消向上传播。
2. DecodeAsync 可从外部载荷提取业务 ID、租户与发生时间，也可异步访问 schema；不可恢复格式错误抛 `DistributedEventDecodeException`，临时依赖故障照常抛出，不能误写拒收。
3. 传输层已判坏的消息调用 RejectAsync。ReceiveAsync／RejectAsync 成功返回后才确认；失败时保留当前消息或重新投递，不提交该分区后续位点。遵守 Inbox:ReceiveEnabled，保留传输支持范围内的接收次序。
4. RawDistributedEvent 的短生命周期 SDK 缓冲区先复制；消息头承载链路，坐标只作诊断。登记带 event-bus 标签的健康检查。中枢专有的重同步、删除标记等能力由业务直接用 SDK，不扩充通用信封。
