# 实时通信

实时通信组件为应用提供**服务端主动推送业务事件**的能力：业务代码在数据变更时通过统一接口把事件推给"订阅了该资源"的客户端，无需客户端轮询；同时提供基于连接的**在线状态**跟踪，以及客户端订阅资源时的**授权校验**扩展点。典型场景包括：产品档案变更后刷新所有正在查看该档案的客户端、工作流状态变更时通知相关方、展示某用户/某资源当前是否有人在线。

Leistd 通过 `IBusinessEventPublisher` 抽象发布入口、`IPresenceService` 抽象在线状态查询、`IRealtimeSubscriptionAuthorizer` 抽象订阅授权，业务代码只依赖这三个接口；当前提供基于 SignalR 的实现 `Leistd.RealTime.AspNetCore.SignalR`。

## 何时使用

| 场景 | 推荐 |
| --- | --- |
| 需要把"某资源发生了变更"实时推给正在关注它的客户端（按 `resourceKey` 订阅） | 引入 `Leistd.RealTime.AspNetCore.SignalR`，注入 `IBusinessEventPublisher` |
| 需要查询某用户是否在线、或列出所有在线用户 | 注入 `IPresenceService` |
| 需要限制客户端只能订阅有权限的资源 | 实现并注册自定义 `IRealtimeSubscriptionAuthorizer`，并开启 `RequireSubscriptionAuthorization` |
| 仅编写业务代码（发布事件/查询在线状态），不关心底层传输 | 只引用 `Leistd.RealTime.Core` 中的接口 |

> 与**通知（Notifications）组件**的区别：实时通信是**通用的业务事件通道**——按任意 `resourceKey` 订阅、事件不持久化、也没有"通知"这个领域概念；通知组件面向**站内通知**——有标题/内容/已读状态、可持久化历史记录。两者共用同一套 SignalR 基础设施（`RealTimeOptions`），但 Hub、发布接口、语义都彼此独立，可单独引入其中一个。

## 安装

```bash
# 抽象（业务代码引用；实现包已传递引用，通常无需单独添加）
dotnet add package Leistd.RealTime.Core

# SignalR 实现
dotnet add package Leistd.RealTime.AspNetCore.SignalR
```

> 本仓库的模板项目通过中央包管理（CPM）统一版本，添加时无需写版本号。

## 配置 Provider

在 `Program.cs` 注册：

```csharp
builder.Services.AddRealTimeSignalR(options =>
{
    options.RealTimeHubPath = "/hubs/realtime"; // 默认值，可省略
    options.RequireSubscriptionAuthorization = true; // 需要按权限校验订阅时开启
});
```

映射 Hub 端点（需登录）：

```csharp
app.MapRealTimeHub();
```

DI 方法说明：

| 方法 | 所属包 | 作用 |
| --- | --- | --- |
| `AddRealTime()` | `Leistd.RealTime.Core` | 以 `TryAddSingleton` 注册 `IRealtimeSubscriptionAuthorizer`→`AllowAllRealtimeSubscriptionAuthorizer`（默认允许所有订阅） |
| `AddRealTimeSignalR(configure?)` | `Leistd.RealTime.AspNetCore.SignalR` | 内部调用 `AddRealTime()` + `AddSignalR()`；以 **Singleton** 注册 `IUserIdProvider`→`ClaimsSignalRUserIdProvider`、`IPresenceService`→`SignalRPresenceService`、`IBusinessEventPublisher`→`SignalRBusinessEventPublisher`；并绑定 `RealTimeOptions`（详见[配置项](#配置项)） |
| `MapRealTimeHub()` | `Leistd.RealTime.AspNetCore.SignalR` | 映射 `RealTimeHub` 端点，路径取自 `RealTimeOptions.RealTimeHubPath`（默认 `/hubs/realtime`），要求已登录（`RequireAuthorization`） |

若业务需要自定义订阅授权规则，注册自己的 `IRealtimeSubscriptionAuthorizer` 实现覆盖默认值（需在 `AddRealTimeSignalR`/`AddRealTime` 之后注册，或使用 `Replace`）：

```csharp
builder.Services.AddSingleton<IRealtimeSubscriptionAuthorizer, MyResourcePermissionAuthorizer>();
```

## 使用

**发布业务事件**——注入 `IBusinessEventPublisher`，在资源变更后推送给订阅方：

```csharp
public class ProductProfileService(IBusinessEventPublisher eventPublisher)
{
    public async Task UpdateProfileAsync(string ownerId, ProductProfileDto dto)
    {
        // —— 主业务流程：更新档案 ——

        await eventPublisher.PublishToResourceAsync(
            resourceKey: $"product-profile:{ownerId}",
            eventName: "ProductProfileUpdated",
            @event: dto);
    }
}
```

客户端需先调用 Hub 的 `Subscribe("product-profile:{ownerId}")` 方法加入该资源分组，才能收到 `ProductProfileUpdated` 事件。

**查询在线状态**——注入 `IPresenceService`：

```csharp
public class UserStatusService(IPresenceService presenceService)
{
    public Task<bool> IsUserOnlineAsync(string userId) => presenceService.IsOnlineAsync(userId);

    public Task<IReadOnlyList<string>> GetOnlineUsersAsync() => presenceService.GetOnlineUserIdsAsync();
}
```

## 接口参考

`Leistd.RealTime` 命名空间（`Leistd.RealTime.Core` 包）：

| 成员 | 说明 |
| --- | --- |
| `IBusinessEventPublisher` | 业务事件推送器，面向"资源订阅"的细粒度实时推送，不感知通知领域模型 |
| `IBusinessEventPublisher.PublishToResourceAsync<TEvent>(resourceKey, eventName, @event, ct)` | 推送事件给订阅了指定资源的客户端；`TEvent : class` |
| `IPresenceService` | 在线状态服务，跟踪用户实时连接状态 |
| `IPresenceService.IsOnlineAsync(userId, ct)` | 检查指定用户是否在线 |
| `IPresenceService.GetOnlineUserIdsAsync(ct)` | 获取所有在线用户 ID 列表 |
| `IRealtimeSubscriptionAuthorizer` | 实时资源订阅授权器 |
| `IRealtimeSubscriptionAuthorizer.AuthorizeAsync(context, ct)` | 判断当前用户是否允许订阅指定资源，返回 `bool` |
| `RealtimeSubscriptionContext` | 订阅上下文 `record`：`ResourceKey`（资源标识）、`UserId`（当前连接用户标识，匿名连接为 `null`） |
| `AllowAllRealtimeSubscriptionAuthorizer : IRealtimeSubscriptionAuthorizer` | 默认授权器，始终返回 `true`，避免影响公共资源订阅场景 |

`Leistd.RealTime.AspNetCore.SignalR` 命名空间（`Leistd.RealTime.AspNetCore.SignalR` 包）：

| 成员 | 说明 |
| --- | --- |
| `RealTimeHub : Hub` | 业务事件 Hub；提供 `Subscribe(resourceKey)` / `Unsubscribe(resourceKey)` 两个客户端可调用方法 |
| `ClaimsSignalRUserIdProvider : IUserIdProvider` | 按 `RealTimeOptions.UserIdClaimTypes` 配置顺序解析 SignalR `UserIdentifier` |
| `SignalRPresenceService : IPresenceService` | 基于内存计数的在线状态实现（单机） |
| `SignalRBusinessEventPublisher : IBusinessEventPublisher`（internal） | 基于 SignalR 组播的业务事件推送实现 |

## 实现行为

### Leistd.RealTime.AspNetCore.SignalR

- **连接与在线状态**：`RealTimeHub.OnConnectedAsync` 取 `ICurrentUser.Id`（取不到则回退 `Context.UserIdentifier`），非空时将连接加入组 `user:{userId}`，并调用 `SignalRPresenceService.UserConnected(userId)`；`OnDisconnectedAsync` 对称调用 `UserDisconnected(userId)`。取不到用户标识的匿名连接不参与在线状态跟踪。
- **多连接计数语义**：`SignalRPresenceService` 用 `ConcurrentDictionary<string, int>` 记录"用户 ID → 连接数"，而非布尔值。同一用户开多个标签页/多端登录时，每次 `OnConnectedAsync` 计数 +1、每次 `OnDisconnectedAsync` 计数 -1；`IsOnlineAsync` 判断计数 `> 0` 即在线，因此**只要该用户还有任意一条连接存活就算在线**，必须所有连接都断开、计数归零后才判为离线，归零时会从字典中移除该 key（避免内存泄漏）。当前为**单进程内存**实现，多实例部署下同一用户连接到不同实例时状态不共享（需 Redis Backplane，见下）。
- **订阅授权流程**：客户端调用 Hub 的 `Subscribe(resourceKey)` 方法时，仅当 `RealTimeOptions.RequireSubscriptionAuthorization` 为 `true` 才会调用 `IRealtimeSubscriptionAuthorizer.AuthorizeAsync`（携带 `ResourceKey` 与当前用户 ID 构造的 `RealtimeSubscriptionContext`）；返回 `false` 时抛出 `HubException("Subscription forbidden.")`，客户端订阅失败。`RequireSubscriptionAuthorization` 为 `false`（默认）时**完全跳过授权器调用**，直接加入组 `resource:{resourceKey}`。`Unsubscribe(resourceKey)` 不做任何授权校验，直接移出该组。
- **事件推送**：`SignalRBusinessEventPublisher.PublishToResourceAsync` 向组 `resource:{resourceKey}` 发送 `eventName` 事件（`IHubContext<RealTimeHub>.Clients.Group(...).SendAsync(eventName, @event, ct)`），前端通过 `connection.on(eventName, ...)` 订阅。推送失败（异常）只记 `LogError`，**不向上抛出**。
- **UserId 解析回退链**：`ClaimsSignalRUserIdProvider.GetUserId` 按 `RealTimeOptions.UserIdClaimTypes` 中配置的 claim 类型**顺序遍历**，取连接 `User` 中第一个非空的 claim 值返回；全部为空或未配置任何 claim 类型时返回 `null`。`AddRealTimeSignalR` 在调用方未显式配置 `UserIdClaimTypes` 时，默认填充 `["sub", ClaimTypes.NameIdentifier]`（兼容 OpenIddict/OAuth2 的 `sub` 与标准 `nameidentifier`）；`Leistd.RealTime.Core` 自身保持中立、默认值为空数组。
- **Redis Backplane**：`RealTimeOptions.EnableRedisBackplane` / `RedisConnectionString` 当前仅为**预留字段**，源码未接入实际的 Redis 背板逻辑，配置后不会生效。

## 配置项

`RealTimeOptions`（`Leistd.RealTime.Core` 包）：

| 属性 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `RealTimeHubPath` | `string` | `"/hubs/realtime"` | 业务事件 Hub 路径 |
| `KeepAliveInterval` | `TimeSpan` | `15` 秒 | 心跳间隔 |
| `ClientTimeoutInterval` | `TimeSpan` | `30` 秒 | 客户端超时 |
| `EnableDetailedErrors` | `bool` | `false` | 是否启用详细错误（开发环境） |
| `UserIdClaimTypes` | `IReadOnlyList<string>` | `[]`（Core 默认空；`AddRealTimeSignalR` 未显式配置时填充为 `["sub", ClaimTypes.NameIdentifier]`） | 解析 SignalR `UserIdentifier` 的 claim 类型，按顺序取第一个非空值 |
| `EnableRedisBackplane` | `bool` | `false` | 是否启用 Redis Backplane（多实例扩展），**当前为预留，未实现** |
| `RedisConnectionString` | `string?` | `null` | Redis 连接字符串（启用 Backplane 时使用），**预留** |
| `RequireSubscriptionAuthorization` | `bool` | `false` | 是否在客户端订阅资源时启用授权校验；关闭时兼容公共资源订阅场景 |

## 注意事项

- `SignalRPresenceService` 的在线状态是**单进程内存计数**，多实例部署下不同实例各自维护独立状态，不能跨实例查询在线情况；`EnableRedisBackplane` 目前不会解决这个问题（未实现）。
- `RequireSubscriptionAuthorization` 默认为 `false`：默认情况下任何已连接客户端都可以 `Subscribe` 任意 `resourceKey`，如需按权限限制订阅，必须显式开启该配置并注册自定义 `IRealtimeSubscriptionAuthorizer`。
- `PublishToResourceAsync` 推送失败只记日志、不抛异常：调用成功返回不代表订阅方一定收到消息（例如客户端未连接/未订阅该资源）。
- `SignalRBusinessEventPublisher` 为 `internal` 类，业务代码只能通过 `IBusinessEventPublisher` 接口使用，不能直接引用具体类型。
- **授权只在握手阶段执行一次**。`Subscribe` 只核对订阅规则，不复检账号是否仍然可用；SignalR 也不会对已建立的连接重跑端点策略。账号在连接之后被禁用或锁定时，那条连接仍可继续订阅与接收事件。要让既有连接也立即失效，需要连接注册表加跨节点终止通道，由业务项目按自身敏感度实现。

## 相关

- [组件总览](./README.md)
- [通知](./notifications.md)
- [依赖注入](./dependency-injection.md)
- [当前用户与身份信息](./security.md)
