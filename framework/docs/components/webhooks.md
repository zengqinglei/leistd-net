# 出站 Webhook

把业务事件推送到外部系统登记的地址：事件随业务事务一起写库，提交后立即签名发送，失败按退避表持久重试；接收方按 Standard Webhooks v1 验签、按事件标识去重。

## 何时使用

| 场景 | 用法 |
| --- | --- |
| 外部系统要在业务发生时收到通知（订单已支付、客户已开通） | 用 `IWebhookDefinitionProvider` 声明事件类型，在工作单元内调用 `IWebhookPublisher.PublishAsync` |
| 业务已有本地事件，想顺带对外推送 | 在 `[UnitOfWorkEventHandler(UnitOfWorkPhase.BeforeCommit)]` 处理器里发布 |
| 后台要管理订阅、查看投递日志、手动重发 | 路由组上调 `MapWebhooks(...)`；自定义路由或 DTO 时直接用 `IWebhookManagementService` |
| 仪表盘要按区间统计投递数与成功率 | `IWebhookDeliveryReader.CountByBucketsAsync`，桶边界按业务时区由调用方算好 |
| 投递记录要定期清理 | `AddWebhookRetention<TDbContext>()`，默认开启、保留 62 天 |
| 订阅被停用时要告知管理员 | 处理 `WebhookSubscriptionChangedEvent` 中 `Change` 为 `Disabled` 的事件，按 `DisabledReason` 决定是否通知 |
| 订阅的增删、启停与轮换要记入审计 | 处理 `WebhookSubscriptionChangedEvent`，提交后分发，回滚的写入不分发 |
| 改用托管投递服务或自定义发送方式 | 替换 `IWebhookSender`，重试、停用与记录仍由组件处理 |
| 接收方要求保证顺序或恰好一次 | 本组件不提供，见[注意事项](#注意事项) |

## 安装

```bash
# 契约、签名与默认发送器（业务代码引用；存储包已传递引用）
dotnet add package Leistd.Webhooks.Core

# EF Core 持久化、发布器、立即投递与兜底扫描、统计、保留期
dotnet add package Leistd.Webhooks.EntityFrameworkCore

# 后台管理 HTTP 端点
dotnet add package Leistd.Webhooks.AspNetCore
```

## 注册

```csharp
builder.Services.AddWebhooksEfCore<AppDbContext>();   // 未调用 AddWebhooks() 时以默认配置节代为调用
builder.Services.AddSingleton<IWebhookDefinitionProvider, OrderWebhookDefinitionProvider>();
```

在 `OnModelCreating` 中映射订阅、事件、投递与尝试四张表：

```csharp
using Microsoft.EntityFrameworkCore;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ConfigureWebhooks();
    }
}
```

webhook 表只映射在一个上下文里：以另一个 DbContext 再调用 `AddWebhooksEfCore` 时在注册期抛出 `InvalidOperationException`。

**前置**（组件不代为注册）：

```csharp
builder.Services.AddUnitOfWork();
builder.Services.AddUnitOfWorkEfCore();            // 发布与投递经 IDbContextProvider 取上下文
builder.Services.AddMultiTenancyCore();            // 兜底扫描与保留期逐库进入；不分库时只有宿主库
builder.Services.AddRedisDistributedLock(builder.Configuration.GetConnectionString("Redis")!);   // 集群周期任务需要分布式锁
builder.Services.AddInProcessBackgroundJobs();     // 调度器与立即投递所用的进程内队列
builder.Services.AddDataProtection();              // 签名密钥加密存储
builder.Services.AddLocalEventBus();               // 可选：发布 WebhookSubscriptionChangedEvent
```

多副本部署时，周期任务的完成水位与 Data Protection 密钥环都须在副本间共享，见[后台作业](./background-jobs.md)。

`AddWebhooks()` 已登记本组件错误码的非默认 HTTP 状态（具体映射见[接口参考](#接口参考)），宿主不需要另行组合。

改配置或用另一配置节时，在 `AddWebhooksEfCore` 之前调用 Core 的入口：

```csharp
builder.Services.AddWebhooks(options => options.AllowedNetworks.Add("10.0.0.0/8"));
```

映射管理端点；两个授权策略都必填，漏配在映射时抛出：

```csharp
app.MapGroup("/api/v1/webhooks").MapWebhooks(options =>
{
    options.ReadPolicy = "App.Webhooks";
    options.ManagePolicy = "App.Webhooks.Manage";
});
```

启用保留期清理（同样需要上面的后台作业调度器、分布式锁与 `AddMultiTenancyCore()`）：

```csharp
builder.Services.AddWebhookRetention<AppDbContext>();
```

## 使用

**声明事件类型**——类型名是接收方按类型分支的依据，一经发布不要修改：

```csharp
public class OrderWebhookDefinitionProvider : IWebhookDefinitionProvider
{
    public void Define(IWebhookDefinitionContext context)
    {
        context.Add("order.paid", "orders", "Webhooks:Events:OrderPaid");
        context.Add("order.cancelled", "orders", "Webhooks:Events:OrderCancelled");
    }
}
```

`group` 驱动界面分组，`description` 通常是本地化键，组件原样下发。`webhook.test`（`WebhookDefinition.TestEventType`）由组件内置，业务不得重复登记。

**在工作单元内发布**——事件与投递记录随业务事务提交，回滚时一起回滚：

```csharp
public class OrderPaymentService(IWebhookPublisher webhookPublisher)
{
    [UnitOfWork]
    public virtual async Task MarkPaidAsync(Guid orderId, decimal amount, CancellationToken cancellationToken)
    {
        // 更新订单……
        await webhookPublisher.PublishAsync("order.paid", new { orderId, amount }, cancellationToken);
    }
}
```

`data` 在发布时序列化为信封 `{type, timestamp, data}`（System.Text.Json Web 默认，camelCase）并冻结，之后修改对象不影响已发布的载荷。

**从本地事件处理器发布**——处理器须声明 `BeforeCommit`：默认的提交后阶段里环境工作单元已清空，`PublishAsync` 会抛出。

```csharp
public class OrderCancelledEvent(Guid orderId) : LocalEvent
{
    public Guid OrderId { get; } = orderId;
}

[UnitOfWorkEventHandler(UnitOfWorkPhase.BeforeCommit)]
public class OrderCancelledWebhookHandler(IWebhookPublisher webhookPublisher) : IEventHandler<OrderCancelledEvent>
{
    public Task HandleAsync(OrderCancelledEvent @event, CancellationToken cancellationToken = default)
        => webhookPublisher.PublishAsync("order.cancelled", new { @event.OrderId }, cancellationToken);
}
```

订阅的事件类型可以是已登记类型的列表，也可以单独一个 `"*"`（`WebhookDefinition.AllEventTypes`）：订阅全部事件，含以后新登记的类型；`"*"` 不与具体类型混用。

## 接口参考

| 类型 / 成员 | 说明 |
| --- | --- |
| `AddWebhooks(configure?, configSectionPath?)` | Core：绑定并在启动期校验 `WebhookOptions`，登记事件类型索引、默认发送器、错误码译文与默认 HTTP 状态；可重复调用，换用另一配置节时抛出 |
| `AddWebhooksEfCore<TDbContext>()` | EF：注册发布器、立即投递、统计读取，登记集群周期任务 `webhooks.dispatch`；宿主未调用 `AddWebhooks()` 时代为调用 |
| `AddWebhookRetention<TDbContext>(configure?, configSectionPath?)` | EF：启用保留期，登记集群周期任务 `webhooks.retention`；绑定 `Leistd:Webhooks:Retention` |
| `ModelBuilder.ConfigureWebhooks()` | 映射四张表，表名沿用 EF Core 默认约定 |
| `MapWebhooks(configure)` | AspNetCore：映射管理端点（下表），返回路由组；`WebhookEndpointOptions.ReadPolicy`、`ManagePolicy` 必填；端点名常量见 `WebhookEndpoints`（如 `CreateSubscriptionName`、`RotateSecretName`），宿主按名字给个别端点追加元数据，均以 `NamePrefix` 开头 |
| `IWebhookDefinitionProvider.Define(context)` | 经 `IWebhookDefinitionContext.Add(name, group, description?)` 登记事件类型；类型名全局唯一，重复登记在启动期抛出 |
| `IWebhookDefinitionManager` | 已登记类型的只读索引（`GetOrNull`、`GetAll`），含内置测试类型 |
| `IWebhookPublisher.PublishAsync(eventType, data, ct)` | 业务发布事件的唯一入口；类型未登记抛 `ArgumentException`，没有活动工作单元抛 `InvalidOperationException` |
| `IWebhookSender.SendAsync(request, ct)` | 发一次请求并如实报告 `WebhookSendResult`；网络失败、超时与被拦截的地址不抛异常，只有取消才抛出；实现不自行重试 |
| `IWebhookManagementService` | 管理用例：事件类型、订阅增删改与启停、密钥轮换、测试事件、投递日志与手动重发；只作用于当前租户，不做权限检查 |
| `IWebhookDeliveryReader` | 统计读取：`CountByBucketsAsync(edges, subscriptionId?, ct)` 按 UTC 半开桶返回 `WebhookDeliveryCounts`；`GetSnapshotAsync` 返回 `WebhookDeliverySnapshot`（重试中投递数、已停用订阅数）；`GetRetainedSince()` 见[实现行为](#实现行为) |
| `WebhookSubscriptionChangedEvent` | 订阅创建、轮换密钥、启用、停用、删除后经可选的 `ILocalEventBus` 发布，在工作单元内推迟到提交后分发，失败或回滚不分发；处于订阅所属租户的上下文。带 `SubscriptionId`、`SubscriptionName`（删除时为删除前的名称）、`Change`（`WebhookSubscriptionChangeKind`：`Created`、`SecretRotated`、`Enabled`、`Disabled`、`Deleted`）、`DisabledReason`（仅停用时有值，`WebhookDisabledReason`：`Manual`、`EndpointGone`、`ConsecutiveFailures`）。状态未变的再启用、再停用与更新不发布；410 与连续失败的自动停用同样发布 |
| `WebhookDeliveryStatus` / `WebhookAttemptTrigger` | 投递状态 `Pending`（重试中）、`Succeeded`、`Failed`；尝试来源 `Automatic`、`Manual` |
| 默认 HTTP 状态 | `WebhookErrorCodes.SubscriptionNotFound`、`DeliveryNotFound` → 404；`SubscriptionDisabled`、`DeliveryNotRetryable` → 409。地址、事件类型等输入不合法走验证错误（400）。由 `AddWebhooks()` 自动登记；宿主 `MapCode` 可覆盖 |

管理端点（前缀由宿主的路由组决定）：

| 方法与路由 | 策略 | 说明 |
| --- | --- | --- |
| `GET /event-types` | Read | 已登记的事件类型（含分组） |
| `GET /subscriptions`、`GET /subscriptions/{id}` | Read | 查询参数 `offset`、`limit`、`keyword`、`isEnabled`、`eventType`；每项带最近一次投递与近 7 天成功率 |
| `POST /subscriptions` | Manage | 返回订阅与一次性明文密钥 |
| `PUT /subscriptions/{id}`、`DELETE /subscriptions/{id}` | Manage | 删除返回 204，并一并删除它的投递与尝试 |
| `POST /subscriptions/{id}/enable`、`POST /subscriptions/{id}/disable` | Manage | 启用可带查询参数 `retryFailed=true` |
| `POST /subscriptions/{id}/rotate-secret` | Manage | 返回一次性明文新密钥 |
| `POST /subscriptions/{id}/test` | Manage | 向该订阅发一条 `webhook.test`，不论它是否订阅了该类型 |
| `GET /deliveries`、`GET /deliveries/{id}` | Read | 查询参数 `offset`、`limit`、`keyword`、`subscriptionId`、`status`（`pending`、`succeeded`、`failed`）、`eventType`；详情含载荷与全部尝试 |
| `POST /deliveries/{id}/retry` | Manage | 手动重发一次终态投递 |

明文密钥只在创建与轮换的响应里出现一次，之后只返回 `SecretPrefix`。

## 配置项（`Leistd:Webhooks`）

这些是协议参数与信任边界，归部署配置；默认值可直接上线。

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `RetrySchedule` | `00:00:05, 00:05:00, 00:30:00, 02:00:00, 05:00:00, 10:00:00, 10:00:00` | 首发失败后的重试间隔，第 i 次自动重试取第 i 项，带 ±10% 抖动；共约 27 小时。最多 20 项，每项大于 0 且不超过 7 天；配成空表时首发失败即失败 |
| `RequestTimeout` | `00:00:15` | 单次请求超时，1 秒–5 分钟 |
| `DispatchInterval` | `00:00:10` | 兜底扫描间隔，1 秒–1 小时；调度器启动时取一次 |
| `AutoDisableAfter` | `5.00:00:00` | 从首次失败起连续失败多久后自动停用订阅，须大于 0 |
| `AllowedNetworks` | 空 | 放行的私网 CIDR（如 `10.0.0.0/8`、`::1/128`） |
| `SecretRotationOverlap` | `1.00:00:00` | 轮换后旧密钥继续一并签名的时长，0–30 天；为 0 时轮换即失效 |
| `Retention:Enabled` | `true` | 是否清理到期记录；关闭时记录永久保留 |
| `Retention:Days` | `62` | 保留天数（1–3650），按投递创建时间计；覆盖 30 天视图及其上一区间（含时区边界） |
| `Retention:DailyRunHourUtc` | `19` | 每天执行的 UTC 小时（0–23），排期时取一次 |
| `Retention:BatchSize` | `500` | 单批删除的投递数（100–10000），每批连同其尝试一个事务 |

校验在启动期执行，消息以实际配置节的键开头。`Retention` 子节只在调用 `AddWebhookRetention` 时绑定；开关与天数每轮取当前值，关闭时任务照常排期、到点跳过。

## 实现行为

- **立即投递 + 兜底扫描**：工作单元提交后，本次的投递送进进程内队列立即发送；回滚时不入队。新投递留 1 分钟宽限期，之后由集群周期任务 `webhooks.dispatch` 按 `DispatchInterval` 扫描到期的待投递记录补发，覆盖队列满、进程退出等情况。扫描按物理库逐个进入，每条投递在所属租户的上下文里处理，停用租户的库照样扫描；有库或投递处理失败时本轮记为失败。
- **发送**：两个入口发请求前都重读订阅；订阅已停用或已删除时不发请求，直接终结这条投递。默认发送器 POST `application/json`，不跟随重定向（3xx 按失败处理），不走系统代理，订阅的自定义请求头随请求发出，同名的 `webhook-*` 头由组件覆盖。每次尝试记录实际发出的请求头快照（不含签名）、状态码、耗时与截断到 1024 字符的响应体。
- **结果按顺序判断，命中即停**：

  | 顺序 | 条件 | 处理 |
  | --- | --- | --- |
  | 1 | 2xx | 成功，清零订阅的首次失败时刻 |
  | 2 | 410 | 投递失败，订阅以 `EndpointGone` 停用 |
  | 3 | 其余失败（含 429、503、3xx、超时、连接失败），且本次是手动尝试 | 失败，不排自动重试 |
  | 4 | 其余失败，自动尝试已用完退避表 | 失败 |
  | 5 | 其余失败，自动尝试还有预算 | 按退避表排下一次；响应带 `Retry-After` 时取两者中较大的值 |

  每次失败后检查订阅：首次失败距今超过 `AutoDisableAfter` 时以 `ConsecutiveFailures` 停用。
- **停用的作用**：停用（手动、410、连续失败）时，同一事务里把该订阅所有待投递的记录改为失败并写明原因；已发出的请求不取消。停用期间发布不为它创建投递。重新启用清空停用原因、停用时刻与首次失败时刻；带 `retryFailed` 时，该订阅现存（保留期内）的全部失败投递各按手动重发处理一次。
- **手动重发只发一次**：只对终态（成功或失败）的投递开放，重试中的投递返回 409。重发与排期同事务保存、提交后立即入队；事件标识不变，成功即成功，失败即回到失败，不重开自动退避。订阅已停用时测试事件与重发都返回 409。
- **内网拦截**：默认发送器在建立连接时检查目标的实际 IP（含 DNS 解析出的每个地址），私网（10/8、172.16/12、192.168/16、fc00::/7 等）、回环、链路本地与未指定地址不在 `AllowedNetworks` 内就拒绝连接，作为一次失败的尝试记录；IPv4 映射的 IPv6 地址按 IPv4 判断。这是唯一的检查点，创建订阅时只校验地址是绝对 http(s) URL。
- **签名与轮换**：按 Standard Webhooks v1 对 `{webhook-id}.{webhook-timestamp}.{body}` 做 HMAC-SHA256，`webhook-signature` 为空格分隔的 `v1,<base64>`。密钥形如 `whsec_<base64>`，经宿主 Data Protection 加密存储。轮换后 `SecretRotationOverlap` 内新旧密钥各签一份（新密钥在前），接收方任一验过即可。
- **统计口径**：`CountByBucketsAsync` 按投递创建时刻（即发布时刻）分桶、按**当前**状态计数，一个事件发给 N 个订阅计 N；手动重发改变终态后统计随之改变。已删除的订阅连同其投递退出统计。成功率由调用方按成功 ÷（成功 + 失败）计算。
- **保留期下限**：`GetRetainedSince()` 在调用了 `AddWebhookRetention` 且开启时返回"当前时刻 − `Days`"（UTC），否则为 `null`；比较上一区间时，区间起点早于它就不提供可比数据，而不是按 0 计算。保留期按投递创建时间删除投递及其尝试，再删除已没有投递的事件，按物理库逐个执行。

## 注意事项

- 投递语义是**至少一次**：超时后接收方可能已处理、进程在写回结果前退出，都会再发一次。接收方按 `webhook-id` 去重；同一事件的每次尝试（含手动重发）`webhook-id` 都相同。
- **不保证顺序**：重试与并发发送会让后发生的事件先到。接收方需要顺序时按信封里的 `timestamp` 或业务数据里的版本判断。
- 停用期间发生的事件**不会补发**，重新启用后的 `retryFailed` 只重发已有的失败投递。
- 多副本部署须共享周期任务的完成水位与分布式锁，否则兜底扫描与保留期会在每个副本各跑一遍；Data Protection 密钥环也须共享，否则其他副本解不开签名密钥，投递发不出去。
- `AllowedNetworks` 是信任边界：放行内网地址等于允许订阅者让服务端访问该网段，只放行确需接收 webhook 的网段。替换 `IWebhookSender` 后内网拦截由替换方负责。
- 发布方负责租户上下文：只投给当前租户（宿主上下文下为宿主）的启用订阅。后台作业里发布前先切到目标租户。
- 事件类型名与 `data` 的形状是对外契约：改名等于换类型，删字段会让接收方出错。
- **接收方接入要点**：用订阅密钥按 Standard Webhooks v1 验签（官方各语言库可直接使用），拒绝 `webhook-timestamp` 偏离当前时刻过远的请求以防重放；先持久化或入队再尽快回 2xx，耗时处理放到后台，避免超时引发重发；按 `webhook-id` 去重；不再接收时回 410，订阅会被停用；需要限流时回 429 或 503 并带 `Retry-After`。

## 相关

- [后台作业](./background-jobs.md)
- [工作单元与事务](./unit-of-work.md)
