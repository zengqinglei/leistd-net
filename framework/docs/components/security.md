# 当前用户与身份信息

`ICurrentUser` / `ICurrentClient` 暴露当前身份，`ICurrentPrincipalAccessor` 提供底层 `ClaimsPrincipal` 和临时切换能力。

## 何时使用

| 场景 | 使用 |
| --- | --- |
| 应用服务/领域服务中读取当前登录用户的 Id、用户名、角色、Claim | 注入 `ICurrentUser` |
| 机器客户端场景下识别调用方（ClientId） | 注入 `ICurrentClient` |
| 直接访问原始 `ClaimsPrincipal`，或在后台任务/测试中临时切换身份 | 注入 `ICurrentPrincipalAccessor` |
| 在领域/应用层使用平台中立的身份访问 | 引用 `Leistd.Security.Core` |
| ASP.NET Core 宿主，需要从 `HttpContext` 取真实身份 | 引用 `Leistd.Security.AspNetCore` 并注册 |

## 安装

```bash
dotnet add package Leistd.Security.Core
dotnet add package Leistd.Security.AspNetCore
```

## 注册

在 Web 宿主的 `Program.cs` 注册：

```csharp
builder.Services.AddSecurity();
```

非 HTTP 宿主（后台作业、消息消费者）注册 `Leistd.Security.Core` 的入口即可：

```csharp
services.AddAmbientContext();
```

两者注册的内容：

| 接口 | `AddAmbientContext()` | `AddSecurity()` | 生命周期 |
| --- | --- | --- | --- |
| `ICurrentPrincipalAccessor` | `CurrentPrincipalAccessor`（只认显式建立的主体） | `HttpContextCurrentPrincipalAccessor` | Singleton |
| `ICurrentUser` | `CurrentUser` | 同左 | Transient |
| `ICurrentClient` | `CurrentClient` | 同左 | Transient |
| `IAmbientContext` | `AmbientContext` | 同左 | Transient |

`AddSecurity()` 就是在 `AddAmbientContext()` 之上把主体来源换成 `HttpContext.User`，
并注册 `IHttpContextAccessor`；两者的调用顺序无关。本组件没有中间件。

`Begin(...)` 建立哪些维度取决于已注册的贡献者：租户维度随 `Leistd.MultiTenancy.AspNetCore`
分发，链路标识随 `Leistd.Tracing.Core`。没装的维度就是没有，不会给猜测值。

主体标识与租户的 claim 类型只在 `ClaimTypeOptions` 一处配置，框架里读写这两类 claim 的每一处都从这里取
（当前用户、租户解析、权限判定、SignalR 寻址、操作记录、服务间还原）。签发主体的宿主改了 claim 名时：

```csharp
builder.Services.Configure<ClaimTypeOptions>(options =>
{
    options.TenantId = "tid";                  // 默认 tenant_id
    options.UserIds = ["oid", "sub"];          // 默认 sub，其次 NameIdentifier
});
```

## 使用

注入 `ICurrentUser`，直接读取强类型属性与方法：

```csharp
public class OrderService(ICurrentUser currentUser)
{
    public Task PlaceOrderAsync()
    {
        if (!currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException();

        Guid? userId = currentUser.Id;
        string? username = currentUser.Username;
        return Task.CompletedTask;
    }
}
```

OAuth2 client credentials 场景识别调用方客户端：

```csharp
public class ReportService(ICurrentClient currentClient)
{
    public void Export()
    {
        if (currentClient.IsAuthenticated)
        {
            string? clientId = currentClient.ClientId;
        }
    }
}
```

后台任务或测试用 `IAmbientContext` 同时建立已注册的主体、租户与链路维度：

```csharp
public class SystemJob(IAmbientContext ambientContext, ICurrentUser currentUser)
{
    public void Run(ClaimsPrincipal systemPrincipal)
    {
        using (ambientContext.Begin(systemPrincipal))
        {
            _ = currentUser.Id;
        }
    }
}
```

非 HTTP 宿主注册 `AddAmbientContext()` 即可（见[注册](#注册)）；`ICurrentPrincipalAccessor.Change`
仍然可用，但只切主体这一维。

## 接口参考

### `Leistd.Security.Users.ICurrentUser`

| 成员 | 说明 |
| --- | --- |
| `IsAuthenticated` | 当前主体是否已认证（`Principal.Identity.IsAuthenticated`），无主体时为 `false` |
| `SubjectId` | 主体标识原始值，按 `ClaimTypeOptions.UserIds` 读取；机器主体是 `client:<client_id>`。审计等"任何主体都要留得下标识"的场景用它 |
| `Id` | 在 `SubjectId` 之上只接受 `Guid` 的自然人用户 Id；机器主体等非 GUID 标识为 `null` |
| `TenantId` | 所属租户，按 `ClaimTypeOptions.ReadTenant` 读取；宿主用户返回 `null`，租户 claim 非法时抛 `InvalidOperationException`（不当作宿主）。这是主体 claim 的直读值，运行时权威租户上下文是 [多租户组件](./multi-tenancy.md) 的 `ICurrentTenant` |
| `Username` | 依次取 `preferred_username` / `name` / `Name` claim，均无返回 `null` |
| `Name` | 标准身份名称，依次取 `name` / `Name` claim，不回退 `given_name` |
| `Email` | 邮箱，依次取 `email` / `Email` claim |
| `FindClaim(claimType)` | 指定类型的第一个 `Claim`，跨全部身份查找（官方 `ClaimsPrincipal.FindFirst`），不存在返回 `null` |

`Username`、`Name`、`Email` 只在主体身份（`ClaimTypeOptions.FindSubjectIdentity`）上读取，与 `SubjectId`、`TenantId` 同源：服务间还原出的被代表用户没带 `name` 时，不会取到调用方机器令牌上的名字。主体上没有带用户标识的身份时按整个主体读取。

### `Leistd.Security.Clients.ICurrentClient`

| 成员 | 说明 |
| --- | --- |
| `IsAuthenticated` | `ClientId` 非空时为 `true` |
| `ClientId` | 取 `client_id`（`CustomClaimTypes.ClientId`）claim |

### `Leistd.Security.Claims.ICurrentPrincipalAccessor`

| 成员 | 说明 |
| --- | --- |
| `Principal` | 当前 `ClaimsPrincipal?`：优先返回 `Change` 显式设置的主体，否则回退到底层认证源 |
| `Change(principal)` | 临时切换主体，返回 `IDisposable`；`Dispose` 时恢复上一个主体，支持嵌套；传 `null` 抛 `ArgumentNullException` |

### `Leistd.Security.Claims.CustomClaimTypes`（静态常量）

| 成员 | 值 | 说明 |
| --- | --- | --- |
| `ClientId` | `client_id` | OAuth2/OIDC 客户端标识符 |
| `SessionId` | `sid` | 会话标识符（OIDC 标准） |
| `IdentityProvider` | `idp` | 身份提供者（如 github / google / microsoft） |
| `IsSuperAdmin` | `is_super_admin` | 是否超级管理员（权限授权的超管判定约定来源） |
| `TenantId` | `tenant_id` | `ClaimTypeOptions.TenantId` 的默认值。读写租户 claim 一律经 `ClaimTypeOptions`，不直接用这个常量 |

标准字段直接使用 `System.Security.Claims.ClaimTypes`。

### `Leistd.Security.Claims.ClientSubject`（机器主体 `sub` 契约）

OAuth2 client credentials 令牌代表工作负载。其 `sub` 必须使用 `client:` 前缀，与可解析为 GUID 的用户 `sub` 隔离，避免机器主体被误认为用户。

| 成员 | 说明 |
| --- | --- |
| `Prefix` | 机器主体 `sub` 的前缀，常量 `client:` |
| `Format(clientId)` | 由 `client_id` 构造机器主体 `sub`（签发端使用） |
| `Matches(subject, clientId)` | 判定 `sub` 是否正是该 `client_id` 的机器主体（消费端使用） |

```csharp
identity.AddClaim(new Claim("sub", ClientSubject.Format(request.ClientId!)));

if (ClientSubject.Matches(principal.FindFirst("sub")?.Value, clientId)) { /* 受信的服务调用 */ }
```

> 服务间调用的用户上下文恢复直接依赖该契约，见[服务间调用客户端](./service-client.md)的信任边界。

### `Leistd.Security.Claims.ClaimTypeOptions`（claim 类型与读取规则）

| 成员 | 说明 |
| --- | --- |
| `UserIds` | 主体标识的读取顺序，默认 `sub`，其次 `ClaimTypes.NameIdentifier` |
| `TenantId` | 租户 claim 类型，默认 `tenant_id`；值必须是租户 GUID，没有即宿主 |
| `FindSubjectIdentity(principal)` | 主体身份：按顺序第一个带用户标识（按 `UserIds`）的身份；没有时为 `null`。标识、租户与名字、邮箱这类描述"这个人"的 claim 都取自它 |
| `FindUserId(principal)` | 在主体身份上按 `UserIds` 取第一个非空白的原始值 |
| `ReadTenant(principal)` | 返回 `TenantClaim`：用户标识与租户取自同一个身份——按顺序第一个带用户标识的身份（主体身份）；同一身份内多条（即使值相同）或非 GUID 为非法；其他带用户标识的身份带着与主体身份不同的租户（含主体身份为宿主）为非法，这样同一请求携带的两份用户凭据拼不出"甲的标识 + 乙的租户"；不带用户标识的身份（服务间调用只委托租户时还原出的身份）只在主体身份没有租户时提供租户；带用户标识而无租户 claim 的其他身份（如服务间调用方的机器身份）不参与判定。同一主体被多个认证方案认证、各身份带同一租户是合法的 |

只共享读取规则，不合并语义：`ICurrentUser.Id` 在原始值之上只接受 GUID；审计、SignalR 寻址等场景读原始值。
同一主体被多个认证方案认证时（策略评估会合并各方案的身份），各身份各带一条相同的租户 claim 是合法的。

```csharp
bool isNaturalPerson = Guid.TryParse(claimTypes.Value.FindUserId(principal), out _);
```

## 注意事项

- 各属性在缺失对应 claim 时返回 `null`（`Id` 在 claim 无法解析为 `Guid` 时同样返回 `null`），调用方需做空值处理。
- 角色判断用官方 `ClaimsPrincipal.IsInRole` 或授权策略 `RequireRole`：它们只认身份的 `RoleClaimType`、角色名区分大小写。自行构造 `ClaimsIdentity` 时，`roleType` 要与写入角色 claim 的类型一致（如 OIDC 的 `role`），否则判定静默为 `false`。
- `Change(...)` 基于 `AsyncLocal` 支持异步传播和嵌套，但返回的 `IDisposable` 必须释放。
- `HttpContextCurrentPrincipalAccessor` 依赖 `IHttpContextAccessor`，在没有 HTTP 上下文的后台任务里 `Principal` 为 `null`；此类场景用 `IAmbientContext.Begin(...)` 显式建立系统主体。
- 领域层/应用层应只引用 `Leistd.Security.Core`，避免把 ASP.NET Core 依赖泄漏进核心层。
