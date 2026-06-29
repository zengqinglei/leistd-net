# 当前用户与身份信息

业务代码经常需要回答「现在是谁在调用」：当前登录用户的 Id、用户名、角色，或在 API Key 调用场景下识别是哪个客户端。这些信息都藏在 ASP.NET Core 的 `ClaimsPrincipal`（即 `HttpContext.User`）里，但直接读 Claim 既繁琐又分散——各处自行约定 Claim 名称、自行 `Guid.TryParse`，很容易写错且难以测试。

Leistd 把「读取当前身份」收敛为一组强类型抽象：`ICurrentUser` / `ICurrentClient` 暴露常用属性与方法，`ICurrentPrincipalAccessor` 负责提供底层 `ClaimsPrincipal` 并支持临时切换身份。业务代码只注入这些接口，不再直接碰 `HttpContext`，从而既简化了取值，也让后台任务、单元测试能方便地模拟用户。

## 何时使用

| 场景 | 使用 |
| --- | --- |
| 应用服务/领域服务中读取当前登录用户的 Id、用户名、角色、Claim | 注入 `ICurrentUser` |
| API Key 认证场景下识别调用方客户端（ClientId、ApiKeyId） | 注入 `ICurrentClient` |
| 直接访问原始 `ClaimsPrincipal`，或在后台任务/测试中临时切换身份 | 注入 `ICurrentPrincipalAccessor` |
| 仅依赖抽象编写领域/应用层代码 | 只引用 `Leistd.Security.Core` |
| ASP.NET Core 宿主，需要从 `HttpContext` 取真实身份 | 引用 `Leistd.Security.AspNetCore` 并注册 |

> 领域层/应用层只需引用 `Leistd.Security.Core`（纯抽象，无 ASP.NET 依赖）；只有在 Web 宿主项目里才引用 `Leistd.Security.AspNetCore` 完成 DI 注册。

## 安装

```bash
# 抽象与默认实现（领域/应用层引用；AspNetCore 包已传递引用，通常无需单独添加）
dotnet add package Leistd.Security.Core

# ASP.NET Core 集成（Web 宿主项目引用，提供基于 HttpContext 的访问器与 DI 扩展）
dotnet add package Leistd.Security.AspNetCore
```

> 本仓库的模板项目通过中央包管理（CPM）统一版本，添加时无需写版本号。

## 配置 Provider

在 Web 宿主的 `Program.cs` 注册：

```csharp
builder.Services.AddSecurity();
```

`AddSecurity` 完成以下注册：

| 接口 | 实现 | 生命周期 |
| --- | --- | --- |
| `ICurrentPrincipalAccessor` | `HttpContextCurrentPrincipalAccessor` | Singleton |
| `ICurrentUser` | `CurrentUser` | Transient |
| `ICurrentClient` | `CurrentClient` | Transient |

同时内部调用 `AddHttpContextAccessor()` 注册 `IHttpContextAccessor`。另提供 `UseSecurity(this IApplicationBuilder)` 作为预留的中间件扩展点（当前为空实现，可不调用）。

## 使用

注入 `ICurrentUser`，直接读取强类型属性与方法：

```csharp
public class OrderAppService(ICurrentUser currentUser)
{
    public Task PlaceOrderAsync()
    {
        if (!currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException();

        Guid? userId = currentUser.Id;           // 来自 sub / NameIdentifier
        string? name = currentUser.Username;     // preferred_username / name / Name

        if (currentUser.IsInRole("admin"))       // 角色判断（不区分大小写）
        {
            // —— 管理员逻辑 ——
        }
        return Task.CompletedTask;
    }
}
```

API Key 场景识别调用方客户端：

```csharp
public class ReportAppService(ICurrentClient currentClient)
{
    public void Export()
    {
        if (currentClient.IsAuthenticated)
        {
            string? clientId = currentClient.ClientId;   // client_id claim
            Guid? apiKeyId = currentClient.ApiKeyId;      // api_key_id claim
        }
    }
}
```

在后台任务或测试中临时切换身份（`Change` 返回的 `IDisposable` 释放时自动恢复）：

```csharp
public class SystemJob(ICurrentPrincipalAccessor accessor, ICurrentUser currentUser)
{
    public void Run(ClaimsPrincipal systemPrincipal)
    {
        using (accessor.Change(systemPrincipal))
        {
            // 此作用域内 currentUser 反映 systemPrincipal
            _ = currentUser.Id;
        } // 自动恢复到之前的身份
    }
}
```

## 接口参考

### `Leistd.Security.Users.ICurrentUser`

| 成员 | 说明 |
| --- | --- |
| `IsAuthenticated` | 当前主体是否已认证（`Principal.Identity.IsAuthenticated`），无主体时为 `false` |
| `Id` | 用户唯一标识，取 `sub` 或 `NameIdentifier` claim 并解析为 `Guid`，解析失败/缺失返回 `null` |
| `Username` | 依次取 `preferred_username` / `name` / `Name` claim，均无返回 `null` |
| `Name` | 显示名称，依次取 `name` / `GivenName` claim |
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
| `IsAuthenticated` | 是否为 API Key 认证：`ClientId` 非空或 `ApiKeyId` 有值时为 `true` |
| `ClientId` | 取 `client_id`（`CustomClaimTypes.ClientId`）claim |
| `ApiKeyId` | 取 `api_key_id` claim 并解析为 `Guid`，失败/缺失返回 `null` |
| `CreatorId` | API Key 创建者，取 `NameIdentifier` claim 解析为 `Guid` |

### `Leistd.Security.Claims.ICurrentPrincipalAccessor`

| 成员 | 说明 |
| --- | --- |
| `Principal` | 当前 `ClaimsPrincipal?`：优先返回 `Change` 显式设置的主体，否则回退到底层认证源 |
| `Change(principal)` | 临时切换主体，返回 `IDisposable`；`Dispose` 时恢复上一个主体，支持嵌套；传 `null` 抛 `ArgumentNullException` |

### `Leistd.Security.Claims.CustomClaimTypes`（静态常量）

| 成员 | 值 | 说明 |
| --- | --- | --- |
| `ClientId` | `client_id` | 客户端标识符（API Key 认证） |
| `SessionId` | `sid` | 会话标识符（OIDC 标准） |
| `IdentityProvider` | `idp` | 身份提供者（如 github / google / microsoft） |

> 仅定义与 `System.Security.Claims.ClaimTypes` 不同的自定义字段；标准字段请直接用 `ClaimTypes`。

## 实现行为

### Leistd.Security.Core（抽象与默认实现）

- `CurrentPrincipalAccessor` 为抽象基类，用 `AsyncLocal<ClaimsPrincipal?>` 保存 `Change` 设置的主体：读取 `Principal` 时优先返回该值，为空才调用派生类的 `GetClaimsPrincipal()` 回退到真实认证源。`Change` 借助 `AsyncLocal` 在异步调用链内传播，并在 `Dispose` 时还原父级值，可安全嵌套。
- `CurrentUser` / `CurrentClient` 都是无状态包装，按需从 `Principal` 现读 claim，不缓存；多 claim 来源按上面接口参考列出的优先级依次回退。

### Leistd.Security.AspNetCore（HttpContext 集成）

- `HttpContextCurrentPrincipalAccessor` 继承 `CurrentPrincipalAccessor`，`GetClaimsPrincipal()` 返回 `IHttpContextAccessor.HttpContext?.User`；当前无 HTTP 上下文（如后台线程）时为 `null`。
- 访问器以 Singleton 注册，但其依赖的 `IHttpContextAccessor` 内部用 `AsyncLocal` 维护请求级上下文，因此每个请求看到的是各自的 `User`。

## 配置项 / Options

当前无配置项。

## 注意事项

- 各属性在缺失对应 claim 时返回 `null`（`Id` / `ApiKeyId` / `CreatorId` 在 claim 无法解析为 `Guid` 时同样返回 `null`），调用方需做空值处理。
- `ICurrentUser.GetRoles()` / `IsInRole()` 同时识别 `role` 与标准 `ClaimTypes.Role` 两种 claim，且角色比较不区分大小写。
- `Change(...)` 返回的 `IDisposable` 必须用 `using` 包裹或显式 `Dispose`，否则切换的身份不会恢复，可能污染后续逻辑。
- `HttpContextCurrentPrincipalAccessor` 依赖 `IHttpContextAccessor`，在没有 HTTP 上下文的后台任务里 `Principal` 为 `null`；此类场景请先用 `Change(...)` 显式设置系统主体。
- 领域层/应用层应只引用 `Leistd.Security.Core`，避免把 ASP.NET Core 依赖泄漏进核心层。

## 相关

- [组件总览](./README.md)
- [依赖注入](./dependency-injection.md)
