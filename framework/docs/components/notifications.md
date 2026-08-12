# 通知

通知组件为应用提供**站内通知**能力：业务代码通过统一的发布接口触发通知，框架负责补齐通知信息、（可选）持久化到数据库、并通过 SignalR 实时推送给指定用户、指定分组或全体在线用户。典型场景包括：审批结果提醒、数据变更提示、系统公告、工作流状态变更通知等，兼顾"通知历史可查"与"到达即弹出"两种诉求。

Leistd 通过 `INotificationPublisher` 抽象业务侧的发布入口，内部再拆分为持久化（`INotificationStore`）与实时投递（`INotificationSender`）两个可选、可插拔的关注点：不接持久化包时通知只走实时推送、不接实时推送包时发布器可只落库，二者互不依赖对方存在。

## 何时使用

| 场景 | 推荐 |
| --- | --- |
| 需要给用户展示"通知历史"（铃铛列表、未读数） | 引入 `Leistd.Notifications.EntityFrameworkCore`，注册 `INotificationStore` |
| 需要通知到达即时弹出/刷新，无需等待用户刷新页面 | 引入 `Leistd.Notifications.AspNetCore.SignalR`，注册 `INotificationSender` |
| 同时需要历史记录 + 实时推送（最常见） | 两个实现包都引入，业务只注入 `INotificationPublisher` |
| 仅编写业务代码（发布通知），不关心底层持久化/传输 | 只引用 `Leistd.Notifications.Core` 中的接口 |

> 发布到用户组（`PublishToGroupAsync`）与全体（`PublishToAllAsync`）**不会写入持久化存储**，仅用于实时广播场景（详见[实现行为](#实现行为)）；需要保留历史记录的通知必须走 `PublishToUserAsync`。

## 安装

```bash
# 抽象 + 默认发布器（业务代码引用；实现包已传递引用，通常无需单独添加）
dotnet add package Leistd.Notifications.Core

# 持久化（存历史通知、未读数）
dotnet add package Leistd.Notifications.EntityFrameworkCore

# 实时推送（基于 SignalR）
dotnet add package Leistd.Notifications.AspNetCore.SignalR
```

> 本仓库的模板项目通过中央包管理（CPM）统一版本，添加时无需写版本号。

## 配置 Provider

在 `Program.cs` / DI 配置中按需注册：

```csharp
// EF Core 持久化（TDbContext 需已在 OnModelCreating 中调用 ConfigureNotifications）
services.AddNotificationsEfCore<MyProjectDbContext>();

// SignalR 实时推送（内部会调用 AddNotifications 注册 INotificationPublisher）
builder.Services.AddNotificationsSignalR(opt =>
{
    // 复用 Leistd.RealTime 的 RealTimeOptions 配置项（KeepAliveInterval、EnableRedisBackplane 等）
});
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
app.MapNotificationHub(); // 默认路径 /hubs/notifications，可传参覆盖
```

DI 方法说明：

| 方法 | 所属包 | 作用 |
| --- | --- | --- |
| `AddNotifications()` | `Leistd.Notifications.Core` | 以 **Transient** 注册 `INotificationPublisher`→`NotificationPublisher`。仅注册发布器本身，不含存储/传输 |
| `AddNotificationsEfCore<TDbContext>()` | `Leistd.Notifications.EntityFrameworkCore` | 以 **Transient** 注册 `INotificationStore`→`EfCoreNotificationStore<TDbContext>`（基于指定 `DbContext`） |
| `ConfigureNotifications()` | `Leistd.Notifications.EntityFrameworkCore` | `ModelBuilder` 扩展，应用 `NotificationRecordConfiguration`；在 `OnModelCreating` 中调用 |
| `AddNotificationsSignalR(configure?)` | `Leistd.Notifications.AspNetCore.SignalR` | 内部调用 `AddNotifications()` + `AddNotificationSignalRTransport()`，并以 **Singleton** 注册 `INotificationSender`→`SignalRNotificationSender`。**不**注册持久化，也不注册实时业务 Hub |
| `AddNotificationSignalRTransport(configure?)` | `Leistd.Notifications.AspNetCore.SignalR` | 注册通知 SignalR 传输所需的最小基础设施（`AddSignalR`、`RealTimeOptions`、`IUserIdProvider`），供 `AddNotificationsSignalR` 内部调用，一般无需单独调用 |
| `MapNotificationHub(path?)` | `Leistd.Notifications.AspNetCore.SignalR` | 映射 `NotificationHub` 端点，默认路径 `/hubs/notifications`（`DefaultNotificationHubPath`），要求已登录（`RequireAuthorization`） |

> `INotificationStore` 与 `INotificationSender` 均以 `IEnumerable<T>` 形式注入 `NotificationPublisher`：可同时注册多个实现（如多个存储），发布时会逐一遍历调用。

## 使用

**发布通知**——注入 `INotificationPublisher`，发布给指定用户：

```csharp
public class OrderNotifier(INotificationPublisher notificationPublisher)
{
    public async Task ApproveOrderAsync(string userId, string orderNo)
    {
        // —— 主业务流程 ——
        await notificationPublisher.PublishToUserAsync(userId, new NotificationOutputDto
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
| `INotificationPublisher.PublishToUserAsync(userId, notification, ct)` | 推送给指定用户；`userId` 为 `string` |
| `INotificationPublisher.PublishToGroupAsync(groupName, notification, ct)` | 推送给指定用户组；`groupName` 为 `string` |
| `INotificationPublisher.PublishToAllAsync(notification, ct)` | 推送给所有在线用户 |
| `INotificationSender` | 通知投递器，只负责传输、不负责持久化，由具体传输实现（如 SignalR）实现 |
| `INotificationSender.SendToUserAsync/SendToGroupAsync/SendToAllAsync` | 与 `INotificationPublisher` 对应的三个投递方法，签名一致 |
| `INotificationStore` | 通知持久化接口，可选实现 |
| `INotificationStore.SaveAsync(notification, userId, ct)` | 保存通知 |
| `INotificationStore.GetByUserAsync(userId, maxCount = 50, ct)` | 按创建时间**倒序**获取用户通知列表，默认最多 50 条 |
| `INotificationStore.MarkAsReadAsync(notificationId, userId, ct)` | 标记单条通知为已读 |
| `INotificationStore.MarkAllAsReadAsync(userId, ct)` | 标记用户所有通知为已读 |
| `INotificationStore.GetUnreadCountAsync(userId, ct)` | 获取用户未读通知数量 |
| `NotificationOutputDto` | 通知传输 DTO：`Id`（默认 `Guid.CreateVersion7()` 的 `"N"` 格式字符串）、`Title`、`Content?`、`Type`（默认 `NotificationTypes.System`）、`Link?`、`Icon?`、`IsRead`、`CreationTime`、`RelatedEntityId?`、`RelatedEntityType?`、`Metadata?`（`Dictionary<string, object>?`） |
| `NotificationTypes` | 通知类型字符串常量：`System`="System"、`DataChange`="DataChange"、`Workflow`="Workflow"；业务可自定义任意字符串，不限于这三个 |

## 实现行为

### Leistd.Notifications.Core（`NotificationPublisher` 默认发布器）

- **`PublishToUserAsync` 会写入持久化存储**：先遍历注入的所有 `INotificationStore` 依次 `SaveAsync`，再遍历所有 `INotificationSender` 依次 `SendToUserAsync`。
- **`PublishToGroupAsync` 与 `PublishToAllAsync` 不写入任何存储**，只遍历 `INotificationSender` 完成实时投递——这是源码中的明确不对称设计：组播/广播通知不落库、不计入用户的历史通知与未读数。
- 三个方法发布前都会通过 `EnsureCreationTime` 补齐时间：若传入的 `notification.CreationTime` 为默认值（`default`），用 `IClock` 归一化后的当前时间（`clock.Normalize(clock.Now)`）填充；已显式赋值的 `CreationTime` 不会被覆盖。
- 多个 `INotificationStore` / `INotificationSender` 均按注入顺序 `foreach` **串行 `await`**，非并行、非后台任务。

### Leistd.Notifications.AspNetCore.SignalR（实时推送）

- `NotificationHub` 在客户端连接（`OnConnectedAsync`）时，取 `ICurrentUser.Id`（取不到则回退 `Context.UserIdentifier`），加入 SignalR 组 `user:{userId}`；取不到用户标识则只记警告日志，不加入任何组。
- `SignalRNotificationSender` 的投递目标：`SendToUserAsync` 发到组 `user:{userId}`（即与 `NotificationHub` 加入的组前缀严格一致）；`SendToGroupAsync` 直接发到调用方传入的 `groupName`（不加前缀，与用户组是不同的组命名空间）；`SendToAllAsync` 发给 `Clients.All`。三者统一使用事件名 `NotificationReceived`（`SendAsync` 的方法名，前端通过 `connection.on("NotificationReceived", ...)` 订阅）。
- 三个投递方法内部都用 `try/catch` 包裹 `SendAsync`：推送失败（如底层 SignalR 传输异常）只记 `LogError`，**不向上抛出**，不会导致 `PublishTo*Async` 失败。
- `AddNotificationsSignalR` 内部会先调用 `AddNotifications()`（若未单独调用也会补齐 `INotificationPublisher` 注册），因此只需要引用 SignalR 包并调用它，无需再显式调用 `AddNotifications()`。
- `AddNotificationSignalRTransport` 复用 `Leistd.RealTime` 的 `RealTimeOptions`（`KeepAliveInterval`、`ClientTimeoutInterval`、`EnableDetailedErrors`、`EnableRedisBackplane` 等），并在 `UserIdClaimTypes` 未显式配置时默认使用 `["sub", ClaimTypes.NameIdentifier]`；同时以 `TryAddSingleton` 注册 `IUserIdProvider`→`ClaimsSignalRUserIdProvider`（已存在注册则不覆盖）。
- `MapNotificationHub` 只映射通知自身的 Hub，**不会**代为映射 `Leistd.RealTime` 的业务实时 Hub；如项目同时需要业务实时事件，需另行调用 `AddRealTimeSignalR` 与 `MapRealTimeHub`。

### Leistd.Notifications.EntityFrameworkCore（持久化）

- `EfCoreNotificationStore<TDbContext>` 为**泛型**实现，绑定到调用方指定的 `TDbContext`（`where TDbContext : DbContext`），通过 `dbContext.Set<NotificationRecord>()` 操作，宿主 DbContext 需自行包含该实体（由 `ConfigureNotifications()` 提供配置）。
- `NotificationRecord` 实现 `ICreationAuditedObject`，`CreationTime` / `CreatorId` 由审计拦截器在 `SaveChanges` 时统一填充，`SaveAsync` 自身不手动赋值。
- `GetByUserAsync` 按 `CreationTime` **倒序**排序、`Take(maxCount)` 截断（默认 50）。
- `MarkAsReadAsync` **幂等**：`notificationId` 无法解析为 `Guid` 时直接返回；查不到记录，或记录已是 `IsRead: true` 时也直接返回、不产生额外的 `SaveChanges`；仅在确实从未读变为已读时才更新 `IsRead` 与 `ReadAt` 并保存。
- `MarkAllAsReadAsync` 只查询 `IsRead == false` 的记录批量标记；无未读记录时直接返回，不调用 `SaveChangesAsync`。
- 索引：`(UserId, CreationTime)` 支撑"拉取用户通知列表"，`(UserId, IsRead)` 支撑"未读数"查询；表名沿用 EF Core 默认约定（`NotificationRecord`），不额外加框架前缀。

## 配置项 / Options

`Leistd.Notifications.Core` / `Leistd.Notifications.EntityFrameworkCore` 当前无独立 Options 类。`AddNotificationsSignalR` / `AddNotificationSignalRTransport` 复用 `Leistd.RealTime.RealTimeOptions`（连接保活、Redis 背板、用户标识 Claim 类型等），配置方式与实时组件一致。

## 注意事项

- `PublishToGroupAsync` / `PublishToAllAsync` 不写入 `INotificationStore`：若业务需要"组内通知也能在历史列表看到"，需自行在业务代码中额外调用 `INotificationStore.SaveAsync`（对每个目标用户分别保存），组件不会隐式补齐。
- `NotificationOutputDto.Id` 默认由 DTO 构造时生成（`Guid.CreateVersion7().ToString("N")`），持久化层 `NotificationRecord.FromDto` 会尝试用它解析为实体 `Guid` 主键；若传入的 `Id` 不是合法 Guid 字符串，会静默改为新生成的 `Guid.CreateVersion7()`（即持久化后的 Id 可能与发布时传入的字符串不同）。
- SignalR 用户组前缀 `user:{userId}` 是硬编码约定，`NotificationHub` 加入组与 `SignalRNotificationSender.SendToUserAsync` 发送组必须保持一致，不要在业务代码中自行拼接同名字符串发到 `SendToGroupAsync`（那是不同的组命名空间）。
- SignalR 投递失败只记日志、不抛异常：调用 `PublishToUserAsync` 成功返回不代表用户端一定收到实时推送（例如客户端未连接、连接已断开），需要"送达确认"的场景仍应依赖持久化历史 + 客户端主动拉取未读数兜底。
- `NotificationHub` 端点默认要求登录（`RequireAuthorization`），未登录客户端无法建立 SignalR 连接、也就无法加入 `user:{userId}` 组。
- **授权只在握手阶段执行一次**。SignalR 不会对已建立的连接重跑策略，因此账号在连接之后被禁用、锁定或删除时，那条连接仍会继续收到推送，直到客户端、服务端或传输层实际断开。特别注意**令牌过期不会自动断开**：Hub 没有配置 `CloseOnAuthenticationExpiration`，SignalR 默认不会仅因令牌到期就关闭既有连接。确需到期即断的项目要显式开启该配置，并用真实 SignalR Client 验证。要让既有连接也立即失效，需要连接注册表加跨节点终止通道，由有明确敏感度要求的业务项目自行实现；本组件不提供，也不应把业务用户仓储反向塞进来。
- **改用 Bearer 认证时要为 Hub 路径放开 query 传令牌**。浏览器的 WebSocket 与 SSE 设不了自定义请求头，SignalR 客户端配了 `accessTokenFactory` 之后，negotiate 与长轮询发 `Authorization` 头，WebSocket/SSE 只能把令牌拼进 query。若宿主（如本仓库模板）只接受 `Authorization: Bearer`，需在认证中间件之前按路径把 query 里的 `access_token` 搬进请求头——**只对 Hub 路径**，不要为此放开全部 API：令牌进 URL 会进网关访问日志、APM、浏览器历史与 Referer。用 Cookie 会话时不涉及本条。

## 相关

- [组件总览](./README.md)
- [依赖注入](./dependency-injection.md)
- [当前用户与身份信息](./security.md)
