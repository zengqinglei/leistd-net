# SignalR 基座

解析 SignalR 连接主体的用户标识，为每次 Hub 调用建立环境上下文（主体、租户、链路标识），并复评宿主的授权策略。

## 何时使用

Hub 握手是一次 HTTP 请求，会走完整中间件管道；WebSocket 升级之后的**方法调用不经中间件**，主体、租户、链路标识与端点策略在其中都不成立。本包补的就是这段落差。

| 场景 | 推荐 |
| --- | --- |
| 宿主映射了任何 Hub，且 Hub 方法里要读当前用户、租户或写日志 | 引入本包 |
| 需要按用户寻址推送（`Clients.User(...)`） | 引入本包，按需配置 `UserIdClaimTypes` |
| 需要让"账号被禁用/锁定"在**已建立连接的下一次 Hub 方法调用**上生效 | 引入本包，并在宿主默认策略里表达账号有效性 |
| 使用 `Leistd.RealTime` 或 `Leistd.Notifications` 的 SignalR 包 | 无需直接引入，它们已依赖本包 |

## 安装

```bash
dotnet add package Leistd.AspNetCore.SignalR
```

## 注册

```csharp
builder.Services.AddSignalRAmbientContext(); // 内含 AddSignalR() 与环境上下文
```

带选项：

```csharp
builder.Services.AddSignalRAmbientContext(options =>
{
    options.PolicyName = "HubAccess";                        // 默认用宿主的 DefaultPolicy
    options.RevalidationInterval = TimeSpan.FromSeconds(30); // 默认每次调用都复评
    options.UserIdClaimTypes = ["user_id"];                  // 默认 ["sub", ClaimTypes.NameIdentifier]
});
```

`AddSignalRAmbientContext` 幂等：realtime 与 notifications 同时安装时也只挂一份过滤器。

心跳、超时和详细错误仍通过 SignalR 的 `HubOptions` 配置：

```csharp
builder.Services.AddSignalR(options =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(10);
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});
```

## 使用

Hub 方法内可直接读环境态，与 HTTP 路径写法一致：

```csharp
public class OrderHub(ICurrentUser currentUser, ICurrentTenant currentTenant) : Hub
{
    public Task<string> WhoAmI() =>
        Task.FromResult($"{currentUser.Id} @ {currentTenant.Id?.ToString() ?? "host"}");
}
```

宿主为 HTTP 路径写的授权 handler 无需改动即可在 Hub 上复评生效：

```csharp
options.DefaultPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .AddRequirements(new ActiveUserRequirement())   // 已建连接在下一次方法调用时复评
    .Build();
```

## 接口参考

| 成员 | 说明 |
| --- | --- |
| `AddSignalRAmbientContext(services, configure?)` | 注册 SignalR、`AmbientContextHubFilter` 与 `ClaimsSignalRUserIdProvider`；幂等 |
| `AmbientContextHubFilter : IHubFilter` | 全局过滤器，覆盖方法调用、连接建立与断开 |
| `ClaimsSignalRUserIdProvider : IUserIdProvider` | 按 `UserIdClaimTypes` 顺序解析 SignalR `UserIdentifier`；**只替换** SignalR 自带的 `DefaultUserIdProvider`（后者只认 `ClaimTypes.NameIdentifier`），宿主已注册的实现保持不动，注册在本方法之前或之后都可以 |

## 实现行为

### 用户标识

- 按 `UserIdClaimTypes` 顺序取第一个非空 claim 作为 `Context.UserIdentifier`，供 `Clients.User(...)` 寻址。
- 读的是连接主体（`HubConnectionContext.User`）——`IUserIdProvider` 是 SignalR 基础设施边界；业务代码仍用 `ICurrentUser`。

### 环境上下文

- `OnConnectedAsync`、`InvokeMethodAsync`、`OnDisconnectedAsync` 三处都在 `IAmbientContext.Begin(连接主体)` 的作用域内执行。
- 建立哪些维度取决于已注册的贡献者：主体（`Leistd.Security.Core`）、租户（`Leistd.MultiTenancy.AspNetCore`）、链路标识（`Leistd.Tracing.Core`）。未安装的维度就是没有，不会给猜测值。
- 连接主体未认证（`Identity?.IsAuthenticated != true`，含 ASP.NET Core 给匿名连接的非 null 空主体）时**照常建立上下文**——链路标识与宿主自定义贡献者仍需要——但不做授权复评：没有身份可复评。主体为 `null` 时用空 `ClaimsPrincipal` 代入。

### 有效性复评

- 只在 `InvokeMethodAsync` 上复评；框架不主动断开已建立连接。纯接收 Hub 只在握手时授权，令牌到期关闭由 SignalR 的 `CloseOnAuthenticationExpiration` 决定。
- **在环境上下文之内执行**，因此宿主为 HTTP 路径写的授权 handler（读 `ICurrentUser` 等环境态）可原样生效，账号有效性只有一处定义。
- 不通过时 `HubCallerContext.Abort()` 并抛 `HubException`，客户端需重连并重新认证。
- 节流状态存放在 `HubCallerContext.Items`，随连接生命周期。
- 宿主未注册 `IAuthorizationService` 时跳过复评。

## 配置项

`HubIdentityOptions`：

| 属性 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `PolicyName` | `string?` | `null` | 复评所用策略名；`null` 时取 `IAuthorizationPolicyProvider.GetDefaultPolicyAsync()` |
| `RevalidationInterval` | `TimeSpan?` | `null` | 两次复评的最小间隔；`null` 表示每次调用都复评 |
| `UserIdClaimTypes` | `IReadOnlyList<string>` | `["sub", ClaimTypes.NameIdentifier]` | 解析 `UserIdentifier` 的 claim 顺序，取第一个非空值 |

## 多实例部署

**默认没有背板时，`Clients.Group(...)` 与 `Clients.User(...)` 只到达连在本节点的客户端。** 发布方在 A 节点、订阅方连在 B 节点时消息静默丢失——没有异常、没有日志。跑多副本必须配置背板：

```csharp
builder.Services.AddSignalR().AddStackExchangeRedis(redisConnectionString);
```

宿主自行安装 `Microsoft.AspNetCore.SignalR.StackExchangeRedis`；本包不带任何背板 Provider，也不代为注册——副本数是部署侧的决定，框架检测不到。背板与本包的注册顺序无关。

背板只解决跨节点路由。共享在线状态需宿主自建注册表；断线后的消息补拉需持久化，可使用[通知组件](./notifications.md)。

## 注意事项

- `AddSignalRAmbientContext` 自己补齐 Hub 调用所需的非 HTTP 环境上下文，宿主无需先注册。同时有 Controller/HTTP 路径时再调宿主 security 包的注册入口，把主体来源换成 `HttpContext.User`；两者调用顺序无关。
- 复评默认不节流；高频 Hub 应按实测配置 `RevalidationInterval`。
- 复评评估的是策略的 `Requirements`，不涉及认证方案——身份来自握手时已认证的连接主体。
- 本包只提供基座，不映射任何 Hub 端点，也不注册背板。
- 本包不配置 `HubOptions`：心跳、超时、详细错误是 SignalR 自身的选项，由宿主用 `AddSignalR(o => ...)` 直接配置。
- 显式把 `UserIdClaimTypes` 配成空集合即表示不解析用户标识，此时按用户寻址的推送全部落空。

## 相关

- [当前用户与身份信息](./security.md)
- [多租户](./multi-tenancy.md)
- [链路追踪](./tracing.md)
- [实时通信](./realtime.md)
- [通知](./notifications.md)
