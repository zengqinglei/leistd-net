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
分发（claim 类型配在它的 `MultiTenancyOptions` 上），链路标识随 `Leistd.Tracing.Core`。
没装的维度就是没有，不会给猜测值。

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

        if (currentUser.IsInRole("admin"))
        {
        }
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
| `Id` | 用户唯一标识，取 `sub` 或 `NameIdentifier` claim 并解析为 `Guid`，解析失败/缺失返回 `null` |
| `TenantId` | 所属租户，取 `tenant_id` claim 并解析为 `Guid`；宿主用户返回 `null`。这是主体 claim 的直读值，运行时权威租户上下文是 [多租户组件](./multi-tenancy.md) 的 `ICurrentTenant` |
| `Username` | 依次取 `preferred_username` / `name` / `Name` claim，均无返回 `null` |
| `Name` | 标准身份名称，依次取 `name` / `Name` claim，不回退 `given_name` |
| `Email` | 邮箱，依次取 `email` / `Email` claim |
| `PhoneNumber` | 取 `MobilePhone` claim |
| `GetRoles()` | 返回 `role` 与 `Role` claim 合并去重（不区分大小写）的角色名数组；无主体返回空数组 |
| `IsInRole(roleName)` | 角色是否存在，不区分大小写 |
| `FindClaim(claimType)` | 指定类型的第一个 `Claim`，不存在返回 `null` |
| `FindClaims(claimType)` | 指定类型的全部 `Claim` 数组，无则返回空数组 |
| `GetAllClaims()` | 当前主体的全部 `Claim` 数组，无主体返回空数组 |

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
| `TenantId` | `tenant_id` | 所属租户 Id（宿主用户无此 claim）。认证端签发主体时写入；多租户解析链以它为最高优先来源，已登录用户的租户由此定案 |

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

## 注意事项

- 各属性在缺失对应 claim 时返回 `null`（`Id` 在 claim 无法解析为 `Guid` 时同样返回 `null`），调用方需做空值处理。
- `ICurrentUser.GetRoles()` / `IsInRole()` 同时识别 `role` 与标准 `ClaimTypes.Role` 两种 claim，且角色比较不区分大小写。
- `Change(...)` 基于 `AsyncLocal` 支持异步传播和嵌套，但返回的 `IDisposable` 必须释放。
- `HttpContextCurrentPrincipalAccessor` 依赖 `IHttpContextAccessor`，在没有 HTTP 上下文的后台任务里 `Principal` 为 `null`；此类场景用 `IAmbientContext.Begin(...)` 显式建立系统主体。
- 领域层/应用层应只引用 `Leistd.Security.Core`，避免把 ASP.NET Core 依赖泄漏进核心层。
