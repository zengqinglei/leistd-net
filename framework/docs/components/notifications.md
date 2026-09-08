# 通知

站内通知：业务代码经 `INotificationPublisher` 发布，框架定案这条记录的身份、写入收件人历史（`INotificationStore`），再逐个外发渠道送出（`INotificationChannel`）。

## 何时使用

| 场景 | 推荐 |
| --- | --- |
| 需要给用户展示"通知历史"（铃铛列表、未读数） | 引入 `Leistd.Notifications.EntityFrameworkCore`，注册 `INotificationStore` |
| 需要通知到达即时弹出/刷新，无需等待用户刷新页面 | 引入 `Leistd.Notifications.AspNetCore.SignalR`，它注册一个 `INotificationChannel` |
| 同时需要历史记录 + 实时推送（最常见） | 两个实现包都引入，业务只注入 `INotificationPublisher` |
| 仅编写业务代码（发布通知），不关心底层持久化/传输 | 只引用 `Leistd.Notifications.Core` 中的接口 |

## 安装

```bash
# 抽象 + 默认发布器（业务代码引用；实现包已传递引用，通常无需单独添加）
dotnet add package Leistd.Notifications.Core

# 持久化（存历史通知、未读数）
dotnet add package Leistd.Notifications.EntityFrameworkCore

# 实时推送（基于 SignalR）
dotnet add package Leistd.Notifications.AspNetCore.SignalR
```

## 注册

在 `Program.cs` / DI 配置中按需注册：

```csharp
services.AddNotificationsEfCore<MyProjectDbContext>();

builder.Services.AddNotificationsSignalR();
```

在 `OnModelCreating` 中应用通知实体的 EF Core 配置：

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ConfigureNotifications();
}
```

映射通知 Hub 端点（需登录）：

```csharp
app.MapNotificationHub();
```

`AddNotificationsSignalR()` 会同时注册 Core 发布器与 SignalR 传输，但不注册持久化，也不映射业务实时 Hub。`MapNotificationHub()` 默认映射到 `/hubs/notifications` 并要求登录。

> `INotificationChannel` 以 `IEnumerable<T>` 注入，可同时注册多个传输通道，发布时逐一调用。
> `INotificationStore` 是**必需且唯一**的依赖：通知的定义就是有历史、可补看、计入未读数，未注册时解析 `INotificationPublisher` 直接失败；多个持久化去处只会带来「写了一半」的不一致。只要瞬态推送、不要历史的场景属于[实时通信](./realtime.md)，不属于本组件。

> **前置**：宿主须已注册 `AddUnitOfWork()` 与 `AddUnitOfWorkEfCore()`。本家族的 EF 存储与管理器经
> `IDbContextProvider<TDbContext>` 取上下文——只有它会设置 `DbContextCreationContext.Current`，
> 宿主的 `AddDbContext` 回调据此拿到本工作单元已解析的连接。直接注入 `TDbContext` 会让独立库租户的数据
> 落到宿主配置的默认连接上、并脱离工作单元事务，两者都是静默的。与 `AddMultiTenancyEfCore` 同一约定：
> 组件不替其它组件注册基础设施。

## 使用

**发布通知**——注入 `INotificationPublisher`，发布给指定用户：

```csharp
public class OrderNotifier(INotificationPublisher notificationPublisher)
{
    public async Task ApproveOrderAsync(string userId, string orderNo)
    {
        await notificationPublisher.PublishToUserAsync(userId, new NotificationInputDto
        {
            Title = "订单已通过审批",
            Content = $"订单 {orderNo} 已通过审批",
            Type = NotificationTypes.Workflow,
            Link = $"/orders/{orderNo}",
            RelatedEntityId = orderNo,
            RelatedEntityType = "Order",
        });
    }
}
```

**查询通知历史**——注入 `INotificationStore`（无需依赖 `INotificationPublisher`）：

```csharp
public class MessageCenterController(INotificationStore notificationStore, ICurrentUser currentUser) : BaseController
{
    [HttpGet]
    public async Task<IReadOnlyList<NotificationOutputDto>> GetListAsync(int maxCount = 50, CancellationToken ct = default)
        => await notificationStore.GetByUserAsync(currentUser.Id!.ToString()!, maxCount, ct);
}
```

## 接口参考

`Leistd.Notifications` 命名空间（`Leistd.Notifications.Core` 包）：

| 成员 | 说明 |
| --- | --- |
| `INotificationPublisher` | 通知发布器，业务层唯一入口，不感知底层传输 |
| `INotificationPublisher.PublishToUserAsync(userId, notification, ct)` | 推送给指定用户；`userId` 为 `string`，`notification` 为 `NotificationInputDto` |
| `INotificationChannel` | 通知外发渠道，只负责把已定案的通知送出去，由具体介质实现（如 SignalR）。**不是业务入口**——业务代码用 `INotificationPublisher` |
| `INotificationChannel.DeliverAsync(userId, notification, ct)` | 投递给指定用户；`notification` 已由发布器补齐 `Id`/`CreationTime` |
| `INotificationStore` | 通知持久化接口；**必需且只能有一个**实现 |
| `INotificationStore.SaveAsync(notification, userId, ct)` | 保存通知 |
| `INotificationStore.GetByUserAsync(userId, maxCount = 50, ct)` | 按创建时间**倒序**获取用户通知列表，默认最多 50 条 |
| `INotificationStore.MarkAsReadAsync(notificationId, userId, ct)` | 标记单条通知为已读 |
| `INotificationStore.MarkAllAsReadAsync(userId, ct)` | 标记用户所有通知为已读 |
| `INotificationStore.GetUnreadCountAsync(userId, ct)` | 获取用户未读通知数量 |
| `NotificationInputDto` | 发布输入：`Title`（必填）、`Content?`、`Type`（默认 `NotificationTypes.System`）、`Link?`、`Icon?`、`RelatedEntityId?`、`RelatedEntityType?`、`Metadata?`。**不含身份**——`Id`/`CreationTime`/`IsRead` 由发布器按收件人定案 |
| `NotificationOutputDto` | 读取与实时传输输出：在 `NotificationInputDto` 的字段上加 `Id`、`IsRead`、`CreationTime`。`Id` 恒为**该用户的那条记录**，标记已读用的就是它 |
| `NotificationTypes` | 通知类型字符串常量：`System`="System"、`DataChange`="DataChange"、`Workflow`="Workflow"；业务可自定义任意字符串，不限于这三个 |

## 实现行为

### Leistd.Notifications.Core（`NotificationPublisher` 默认发布器）

- `PublishToUserAsync` 先写入 Store，再依次调用所有渠道（`INotificationChannel`）。Store 是必需依赖；先落库再推送——推送失败只是这一次没送到，历史还在，反过来则是历史丢了。
- 创建时刻只来自 `IClock.Now`：发布输入里没有这个字段，调用方传不进 `Local`/`Unspecified` 的时间，因此「通知列表的排序基准与其它时间线对不上」不再可能发生——不是靠归一化去救，而是取消了这个入口。
- **`INotificationChannel` 不是 realtime 组件的替代品，两者寻址模型不同。** realtime 是<b>资源订阅</b>寻址（客户端先 `Subscribe(resourceKey)`，每次订阅无条件过 `IRealTimeSubscriptionAuthorizer`），它自己的文档也写明「Hub 只做资源订阅，不建任何用户分组：按用户寻址用 `Clients.User(userId)`」；通知是<b>用户</b>寻址，收件人服务端已知，没有授权决定可做。通知的 SignalR 渠道因此挂自己的空 Hub、只借用 [SignalR 基座](./aspnetcore-signalr.md)解析 `UserIdentifier`——复用 realtime 的 Hub 能省一条 WebSocket，但会让只装通知的宿主被迫为用不到的资源订阅注册授权器（`MapRealTimeHub` 缺它就起不来），两个组件也不再能独立安装。
- **业务代码只注入 `INotificationPublisher`。** 直接注入 `INotificationChannel` 调 `DeliverAsync` 会跳过历史写入，表现是"实时到达、刷新后铃铛空白、未读数不涨"，而且不报错。两者签名同构（都是 `(userId, notification, ct)`），编译期分不出来——发布器是「发一次通知」这个用例，`INotificationChannel` 是「某一种介质怎么送」这个实现。
- 多个 `INotificationChannel` 按注入顺序 `foreach` **串行 `await`**，非并行、非后台任务。`INotificationStore` 只允许一个：多个持久化去处只会带来「写了一半」的不一致。

### Leistd.Notifications.AspNetCore.SignalR（实时推送）

- `NotificationHub` 没有可供客户端调用的方法，也不做分组；它只是接收端点。
- `SignalRNotificationChannel.DeliverAsync` 用 SignalR 自带的 `Clients.User(userId)` 寻址；事件名固定为 `NotificationReceived`（前端 `connection.on("NotificationReceived", ...)` 订阅）。
- `DeliverAsync` **不吞异常**，送达失败（如底层 SignalR 传输异常）原样抛出；跨渠道的隔离与日志由 `NotificationPublisher` 统一负责——它逐个渠道捕获、记 `LogError`、继续下一个，因此一个渠道送不到不会让 `PublishToUserAsync` 失败，也不会让后面的渠道收不到。唯一例外是调用方取消（`OperationCanceledException` 且 `ct` 已取消）：如实向上传播。
- `AddNotificationsSignalR` 内部会先调用 `AddNotifications()`（若未单独调用也会补齐 `INotificationPublisher` 注册），因此只需要引用 SignalR 包并调用它，无需再显式调用 `AddNotifications()`。
- `AddNotificationsSignalR` 的 SignalR 部分只调用基座的 `AddSignalRAmbientContext()`：SignalR 注册、Hub 调用的环境上下文与 `UserIdentifier` 解析都由基座提供，本组件不重复注册，也不碰 `HubOptions`。
- `MapNotificationHub` 只映射通知自身的 Hub，**不会**代为映射 `Leistd.RealTime` 的业务实时 Hub；如项目同时需要业务实时事件，需另行调用 `AddRealTimeSignalR` 与 `MapRealTimeHub`。

### Leistd.Notifications.EntityFrameworkCore（持久化）

- `EfCoreNotificationStore<TDbContext>` 为**泛型**实现，绑定到调用方指定的 `TDbContext`（`where TDbContext : DbContext`），通过 `dbContext.Set<NotificationRecord>()` 操作，宿主 DbContext 需自行包含该实体（由 `ConfigureNotifications()` 提供配置）。
- `NotificationRecord` 实现 `ICreationAuditedObject`。`CreationTime` 由发布器定好、`FromDto` 带入：留空转而依赖审计拦截器，等于把落库时间挂在「宿主是否给这个 DbContext 挂了审计拦截器」上——没挂就是 `default(DateTime)`，而通知列表按它排序。`CreatorId` 仍由审计拦截器填充。
- `GetByUserAsync` 按 `CreationTime` **倒序**排序、`Take(maxCount)` 截断（默认 50）。
- `MarkAsReadAsync` **幂等**：`notificationId` 无法解析为 `Guid` 时直接返回；查不到记录，或记录已是 `IsRead: true` 时也直接返回、不产生额外的 `SaveChanges`；仅在确实从未读变为已读时才更新 `IsRead` 与 `ReadAt` 并保存。
- `MarkAllAsReadAsync` 只查询 `IsRead == false` 的记录批量标记；无未读记录时直接返回，不调用 `SaveChangesAsync`。
- 索引：`(UserId, CreationTime)` 支撑"拉取用户通知列表"，`(UserId, IsRead)` 支撑"未读数"查询；表名沿用 EF Core 默认约定（`NotificationRecord`），不额外加框架前缀。

## 注意事项

- **本组件只面向人**：每条通知都有归属用户、写入历史、计入未读数。需要推给「此刻在线的连接」（无归属、无历史）时用 [实时通信](./realtime.md) 的 `IBusinessEventPublisher`——那是瞬态推送的职责，不是没有历史的通知。

- 本家族无独立 Options。心跳、超时、详细错误用 `AddSignalR(o => ...)` 配；`UserIdentifier` 的 claim 解析顺序在 [SignalR 基座](./aspnetcore-signalr.md)的 `HubIdentityOptions`。本组件不依赖实时通信组件。
- 全员公告要进历史时是扇出：受众由业务决定，逐个调用 `PublishToUserAsync`。框架不提供「发给一个组」的入口——那会让受众解析与历史归属两件事混在一处。
- **身份在收件人边界产生**：`PublishToUserAsync` 为每次发布生成 `Id`（`Guid.CreateVersion7().ToString("N")`）与 `CreationTime`（`IClock.Now`），并把同一个对象先交给 Store、再交给所有渠道——因此库里的 ID 与实时推送里的 ID 必然一致，客户端拿推送里的 ID 标记已读一定命中自己那条。同一份 `NotificationInputDto` 扇出给多个用户得到多条独立记录。`NotificationRecord.FromDto` 不再替上游发明 ID：`Id` 解析不出 Guid 直接抛 `ArgumentException`。
- **传给 `PublishToUserAsync` 的 `userId` 必须等于该客户端的 SignalR `UserIdentifier`**，否则推送静默落空。`UserIdentifier` 由 [SignalR 基座](./aspnetcore-signalr.md)的 `HubIdentityOptions.UserIdClaimTypes` 按顺序从 claim 解析（默认 `sub`、`ClaimTypes.NameIdentifier`），只有这一处定义。
- SignalR 投递失败只记日志、不抛异常：调用 `PublishToUserAsync` 成功返回不代表用户端一定收到实时推送（例如客户端未连接、连接已断开），需要"送达确认"的场景仍应依赖持久化历史 + 客户端主动拉取未读数兜底。
- `NotificationHub` 端点默认要求登录（`RequireAuthorization`），未登录客户端无法建立 SignalR 连接、也就收不到任何推送。
- **Hub 调用的上下文与有效性由 SignalR 基座保证**。`AddNotificationsSignalR()` 内部走 `Leistd.AspNetCore.SignalR` 的 `AddSignalRAmbientContext()`：每次 Hub 调用前按连接主体建立 `ICurrentUser` / `ICurrentTenant` / `ICorrelationIdProvider`。**但 `NotificationHub` 没有可供客户端调用的方法**，因此连接建立后不会再触发复评——授权只在握手时执行一次，账号之后被禁用不会主动关闭既有连接。需要立即断连的项目自建终止通道。
- **多副本部署必须配置 SignalR 背板**，否则推送只到达连在本节点的客户端。通知已落库，用户刷新后仍能看到，因此降级较软——但实时性会静默失效。配置方式见 [SignalR 基座](./aspnetcore-signalr.md#多实例部署)。
- **令牌过期本身不会自动断开连接**：Hub 没有配置 `CloseOnAuthenticationExpiration`，SignalR 默认不因令牌到期关闭既有连接。确需到期即断的项目要显式开启该配置，并用真实 SignalR Client 验证。
- **用 Bearer 认证时，浏览器客户端的令牌到不了 Hub**。浏览器的 WebSocket 与 SSE 接口设不了自定义请求头，令牌只能拼进 query；若宿主只接受 `Authorization: Bearer`，握手会失败。处理方式（按 Hub 路径定向搬运，含完整示例）见[实时通信组件文档](./realtime.md#bearer-认证下的-hub-令牌传递)——两个 Hub 面对的是同一个问题，配方不在此重复；照抄时把路径换成本组件实际映射的 `MapNotificationHub` 路径（默认 `/hubs/notifications`）。用 Cookie 会话时不涉及本条。

## 相关

- [当前用户与身份信息](./security.md)
