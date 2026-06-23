# 安全 / 当前用户（`security`）
> 从认证主体（`ClaimsPrincipal`）中读取当前用户、当前客户端信息，并支持在后台任务/测试中临时切换主体。

## 包

| 包 | 角色 | 何时引用 |
| --- | --- | --- |
| `Leistd.Security.Core` | 与宿主无关的安全抽象与实现：`ICurrentUser`、`ICurrentClient`、`ICurrentPrincipalAccessor` 及自定义 Claim 类型 | 领域层/应用层只需读取当前用户、且不直接依赖 ASP.NET Core 时 |
| `Leistd.Security.AspNetCore` | 基于 `HttpContext.User` 的主体来源实现 + DI 注册扩展 | Web 宿主项目，需要把 HTTP 请求中的认证主体接入上述抽象时 |

## 核心抽象

### `ICurrentPrincipalAccessor`（命名空间 `Leistd.Security.Claims`）
当前认证主体的访问入口，并支持临时切换。

```csharp
ClaimsPrincipal? Principal { get; }
```
返回当前认证主体；优先返回通过 `Change` 显式设置的主体，否则回退到实际认证源（派生类实现）；均无则返回 `null`。

```csharp
IDisposable Change(ClaimsPrincipal principal);
```
临时切换当前主体，返回的 `IDisposable` 在 `Dispose` 时自动恢复到切换前的主体；支持嵌套。`principal` 为 `null` 时抛 `ArgumentNullException`。

### `CurrentPrincipalAccessor`（抽象基类，命名空间 `Leistd.Security.Claims`）
`ICurrentPrincipalAccessor` 的抽象实现。基于 `AsyncLocal<ClaimsPrincipal?>` 维护切换栈，派生类只需实现主体来源。

```csharp
protected abstract ClaimsPrincipal? GetClaimsPrincipal();
```
由派生类返回实际认证主体，未认证返回 `null`。仅当未通过 `Change` 显式设置时才会被调用。

### `ICurrentUser`（命名空间 `Leistd.Security.Users`）
从主体中读取当前用户信息。

```csharp
bool IsAuthenticated { get; }   // Principal.Identity.IsAuthenticated，无主体时 false
Guid? Id { get; }               // 依次取 "sub" / ClaimTypes.NameIdentifier，解析失败返回 null
string? Username { get; }       // 依次取 "preferred_username" / "name" / ClaimTypes.Name
string? Name { get; }           // 依次取 "name" / ClaimTypes.GivenName
string? Email { get; }          // 依次取 "email" / ClaimTypes.Email
string? PhoneNumber { get; }    // ClaimTypes.MobilePhone
string[] GetRoles();            // 合并 "role" 与 ClaimTypes.Role，去重(忽略大小写)；无主体返回空数组
bool IsInRole(string roleName); // 角色匹配忽略大小写
Claim? FindClaim(string claimType);   // 首个匹配 Claim，无则 null
Claim[] FindClaims(string claimType); // 所有匹配 Claim，无则空数组
Claim[] GetAllClaims();               // 全部 Claim，无主体返回空数组
```

### `ICurrentClient`（命名空间 `Leistd.Security.Clients`）
API Key 认证场景下读取当前客户端信息。

```csharp
bool IsAuthenticated { get; }   // ClientId 非空 或 ApiKeyId 有值
string? ClientId { get; }       // Claim "client_id"
Guid? ApiKeyId { get; }         // Claim "api_key_id"，解析失败返回 null
Guid? CreatorId { get; }        // ClaimTypes.NameIdentifier（API Key 创建者），解析失败返回 null
```

### `CustomClaimTypes`（静态常量，命名空间 `Leistd.Security.Claims`）
仅定义与 `System.Security.Claims.ClaimTypes` 不同的自定义字段：`ClientId = "client_id"`、`SessionId = "sid"`、`IdentityProvider = "idp"`。标准字段直接用 `System.Security.Claims.ClaimTypes`。

## 能力实现

### `Leistd.Security.AspNetCore`
注册扩展方法（命名空间 `Leistd.Security.AspNetCore`，类 `DependencyInjection`）：

```csharp
IServiceCollection AddLeistdSecurity(this IServiceCollection services);
IApplicationBuilder UseLeistdSecurity(this IApplicationBuilder app);
```

`AddLeistdSecurity` 行为：
- 调用 `AddHttpContextAccessor()`；
- 以 **Singleton** 注册 `ICurrentPrincipalAccessor` → `HttpContextCurrentPrincipalAccessor`（其 `GetClaimsPrincipal` 返回 `IHttpContextAccessor.HttpContext?.User`，主体绑定通过 `AsyncLocal` 随请求流转，单例安全）；
- 以 **Transient** 注册 `ICurrentUser` → `CurrentUser`、`ICurrentClient` → `CurrentClient`。

`UseLeistdSecurity` 当前为预留中间件扩展点，不做任何处理，直接返回 `app`。

> 单元测试或非 HTTP 宿主中，可自行派生 `CurrentPrincipalAccessor` 实现 `GetClaimsPrincipal()` 作为主体来源。

## 最小可用示例

```csharp
using Leistd.Security.AspNetCore;
using Leistd.Security.Users;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLeistdSecurity();   // 注册当前用户/客户端/主体访问器

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseLeistdSecurity();                // 预留扩展点

app.MapGet("/me", (ICurrentUser user) => new
{
    user.IsAuthenticated,
    user.Id,
    user.Username,
    Roles = user.GetRoles()
});

app.Run();
```

后台任务中临时切换主体：

```csharp
using var _ = principalAccessor.Change(systemPrincipal); // 退出 using 时自动恢复
// 此作用域内 ICurrentUser / ICurrentClient 读取到的均为 systemPrincipal
```

## 依赖
`Leistd.Ddd.Domain`（`Leistd.Security.Core` 引用）。

## 备注
- `ICurrentPrincipalAccessor` 注册为单例，但其切换语义基于 `AsyncLocal`，因此在不同异步执行流中互不干扰。
- `Change` 返回的 `DisposeAction` 通过 `Interlocked.Exchange` 保证多次 `Dispose` 只恢复一次。
- `CurrentClient.ApiKeyId` 读取的 Claim 类型字符串为 `"api_key_id"`，该常量未在 `CustomClaimTypes` 中暴露，需要写入该 Claim 才能生效。
- `UseLeistdSecurity` 目前是空实现，调用与否不影响 `AddLeistdSecurity` 注册的服务行为。
