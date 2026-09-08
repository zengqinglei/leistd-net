# 实时通信

实时通信组件通过 SignalR 按资源向客户端推送业务事件，并提供订阅授权扩展点。

## 何时使用

| 场景 | 推荐 |
| --- | --- |
| 需要把"某资源发生了变更"实时推给正在关注它的客户端（按 `resourceKey` 订阅） | 引入 `Leistd.RealTime.AspNetCore.SignalR`，注入 `IBusinessEventPublisher` |
| 需要限制客户端只能订阅有权限的资源 | 实现并注册自定义 `IRealTimeSubscriptionAuthorizer`（无条件生效，没有开关） |
| 仅编写业务代码（发布事件），不关心底层传输 | 只引用 `Leistd.RealTime.Core` 中的接口 |

实时事件不持久化。需要标题、内容、已读状态和历史记录时，使用 [通知组件](./notifications.md)。

## 安装

```bash
dotnet add package Leistd.RealTime.Core
dotnet add package Leistd.RealTime.AspNetCore.SignalR
```

## 注册

在 `Program.cs` 注册：

```csharp
builder.Services.AddRealTimeSignalR();

// 订阅授权必须显式选择，二者取一：
builder.Services.AddSingleton<IRealTimeSubscriptionAuthorizer, MyResourceAuthorizer>();
// 或者明确表示公共资源随便订阅：
builder.Services.AddAllowAllRealTimeSubscriptions();
```

未注册授权器时 `MapRealTimeHub()` 让宿主起不来——「谁能订阅什么」是必须由宿主做出的决定，
框架不给默认值。

映射 Hub 端点（需登录）：

```csharp
app.MapRealTimeHub();
```

`AddRealTimeSignalR` 注册 SignalR 基座（Hub 调用的环境上下文与用户标识解析）与事件发布，**不注册任何授权器**；`MapRealTimeHub` 在 `RealTimeHubPath` 映射需登录的 Hub，并在授权器缺失时抛异常。

心跳、超时、详细错误用 `AddSignalR(o => ...)` 配；解析 `UserIdentifier` 的 claim 顺序在 SignalR 基座的 `HubIdentityOptions.UserIdClaimTypes`。

## 使用

发布资源事件：

```csharp
public class ProductProfileService(IBusinessEventPublisher eventPublisher)
{
    public async Task UpdateProfileAsync(string ownerId, ProductProfileDto dto)
    {
        await eventPublisher.PublishToResourceAsync(
            resourceKey: $"product-profile:{ownerId}",
            eventName: "ProductProfileUpdated",
            @event: dto);
    }
}
```

客户端需先调用 Hub 的 `Subscribe("product-profile:{ownerId}")` 方法加入该资源分组，才能收到 `ProductProfileUpdated` 事件。

## 接口参考

| 成员 | 说明 |
| --- | --- |
| `IBusinessEventPublisher` | 业务事件推送器，面向"资源订阅"的细粒度实时推送，不感知通知领域模型 |
| `IBusinessEventPublisher.PublishToResourceAsync<TEvent>(resourceKey, eventName, @event, ct)` | 推送事件给订阅了指定资源的客户端；`TEvent : class` |
| `IRealTimeSubscriptionAuthorizer` | 实时资源订阅授权器 |
| `IRealTimeSubscriptionAuthorizer.AuthorizeAsync(context, ct)` | 判断当前用户是否允许订阅指定资源，返回 `bool` |
| `RealTimeSubscriptionContext` | 订阅的 `ResourceKey` 和当前 `UserId`；租户等其它维度直接注入 `ICurrentTenant` 读取，Hub 调用内已由基座建立 |
| `AllowAllRealTimeSubscriptionAuthorizer : IRealTimeSubscriptionAuthorizer` | 始终返回 `true`；**不是默认注册**，经 `AddAllowAllRealTimeSubscriptions()` 显式选用 |
| `RealTimeHub : Hub` | 业务事件 Hub；提供 `Subscribe(resourceKey)` / `Unsubscribe(resourceKey)` 两个客户端可调用方法 |

## 实现行为

- Hub 只做资源订阅，不建任何用户分组：按用户寻址用 SignalR 自带的 `Clients.User(userId)`。
- `Subscribe` **无条件**经过 `IRealTimeSubscriptionAuthorizer`，被拒绝时抛出 `HubException`；`Unsubscribe` 始终允许。
- 资源组名为 `resource:{resourceKey}`，不会自动拼租户；租户隔离必须由 `IRealTimeSubscriptionAuthorizer` 判定。推送失败只记录错误。

## 配置项

`RealTimeOptions`（`Leistd.RealTime.Core` 包）：

| 属性 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `RealTimeHubPath` | `string` | `"/hubs/realtime"` | 业务事件 Hub 路径 |

SignalR 传输层的配置不在本组件：心跳、超时、详细错误是 SignalR 的 `HubOptions`，用户标识解析在 [SignalR 基座](./aspnetcore-signalr.md)的 `HubIdentityOptions`。

## Bearer 认证下的 Hub 令牌传递

用 Cookie 会话时不涉及本节。

浏览器的 WebSocket 与 SSE 无法设置自定义请求头，SignalR 因此通过 `?access_token=` 传递 Bearer 令牌。

若宿主只接受 `Authorization: Bearer`（例如显式关闭了 query 形式的令牌提取），需要在认证中间件之前，把 Hub 路径上的 query 令牌搬进请求头：

```csharp
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/hubs/realtime") &&
        context.Request.Headers.Authorization.Count == 0 &&
        context.Request.Query.TryGetValue("access_token", out var token) &&
        token.Count == 1 &&
        !string.IsNullOrWhiteSpace(token[0]))
    {
        context.Request.Headers.Authorization = $"Bearer {token[0]}";
    }

    await next();
});
```

该中间件必须放在 `UseAuthentication()` 之前，路径必须与 `RealTimeHubPath` 一致。只接受单个非空令牌，且仅在缺少 `Authorization` 头时采信 query；不要将范围放宽到全部 API 或所有 Hub。

## 注意事项

- **多副本部署必须配置 SignalR 背板**，否则发布方所在节点之外的订阅者收不到事件，且静默无信号。配置方式见 [SignalR 基座](./aspnetcore-signalr.md#多实例部署)。
- 本组件不提供在线状态查询；多实例在线状态需要宿主维护共享连接注册表。
- 订阅授权**没有开关**：授权器无条件参与每一次 `Subscribe`。未注册授权器时宿主启动失败；`AddAllowAllRealTimeSubscriptions()` 是「公共资源随便订阅」的显式选择。
- `PublishToResourceAsync` 推送失败只记日志、不抛异常：调用成功返回不代表订阅方一定收到消息（例如客户端未连接/未订阅该资源）。
- **Hub 方法调用的上下文与有效性由 SignalR 基座保证**。`AddRealTimeSignalR()` 内部走
  `Leistd.AspNetCore.SignalR` 的 `AddSignalRAmbientContext()`：每次 Hub 调用前按连接主体建立
  `ICurrentUser` / `ICurrentTenant` / `ICorrelationIdProvider`，并复评宿主的默认授权策略，
  不通过即 `Abort()` 连接。**复评只发生在客户端调用 Hub 方法时**——账号被禁用后，既有连接要到下一次 `Subscribe`/`Unsubscribe` 才会被中止；只被动接收事件的连接不会触发复评。框架不主动关闭既有连接。
  复评频率可用 `HubIdentityOptions.RevalidationInterval` 节流（默认每次调用都评）。

## 相关

- [通知](./notifications.md)
- [当前用户与身份信息](./security.md)
