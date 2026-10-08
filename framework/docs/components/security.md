# 身份与会话安全

提供当前主体访问、请求客户端信息、服务端 Cookie 票据和可选的一次码能力。

## 何时使用

| 场景 | 使用 |
| --- | --- |
| 应用服务/领域服务中读取当前登录用户的 Id、用户名、角色、Claim | 注入 `ICurrentUser` |
| 机器客户端场景下识别调用方（ClientId） | 注入 `ICurrentClient` |
| 直接访问原始 `ClaimsPrincipal`，或在后台任务/测试中临时切换身份 | 注入 `ICurrentPrincipalAccessor` |
| 在领域/应用层使用平台中立的身份访问 | 引用 `Leistd.Security.Core` |
| 身份验证器、恢复码或短期验证码摘要 | 引用可选 `Leistd.Security.OneTimeCodes`，无 Web、Identity 或 DDD 依赖 |
| ASP.NET Core 宿主，需要从 `HttpContext` 取真实身份 | 引用 `Leistd.Security.AspNetCore` 并注册 |

## 安装

```bash
dotnet add package Leistd.Security.Core
dotnet add package Leistd.Security.AspNetCore
```

一次码能力单独安装 `Leistd.Security.OneTimeCodes`；其原生 DI/Options 依赖随包传递。

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
并注册 `IHttpContextAccessor`；两者的调用顺序无关。客户端信息可按需独立调用 `AddRequestClientInfo()`，不会随 `AddSecurity()` 自动登记。

`Begin(...)` 建立哪些维度取决于已注册的贡献者：租户维度随 `Leistd.MultiTenancy.AspNetCore`
分发，链路标识随 `Leistd.Tracing.Core`。没装的维度就是没有，不会给猜测值。

主体标识与租户的 claim 类型只在 `ClaimTypeOptions` 一处配置，框架各处都从这里读取。签发主体的宿主改了 claim 名时：

```csharp
builder.Services.Configure<ClaimTypeOptions>(options =>
{
    options.TenantId = "tid";                  // 默认 tenant_id
    options.UserIds = ["oid", "sub"];          // 默认 sub，其次 NameIdentifier
});
```

请求客户端信息由 `Leistd.Security.RequestContext.IRequestClientInfo` 提供，HTTP 宿主按需注册：

```csharp
builder.Services.AddRequestClientInfo();
```

默认实现为 Transient，调用时读取当前请求的 IP 与 User-Agent；无请求时两者为 null，空白 User-Agent 为 null。IP 取 RemoteIpAddress，宿主负责先执行转发头处理并配置受信代理。注册幂等，不覆盖宿主提供的接口实现。

### 服务端 Cookie 票据

浏览器票据可显式接入服务端存储：

```csharp
builder.Services.AddAuthentication("Session").AddCookie("Session");
builder.Services.AddDistributedTicketStore("Session");
```

宿主先提供 `IDistributedCache`、`IDataProtectionProvider` 和 `IDistributedLock`，并配置共享密钥环、应用名与缓存/锁实现。`Leistd.Security.AspNetCore` 因票据能力传递引用 `Leistd.Lock.Core`，不会替宿主选择这些实现。

入口登记单例 `ITicketStore`，保留宿主预先登记的实现；每个命名 Cookie 方案只挂载一次，配置委托按调用叠加。不同方案共用存储配置，Cookie 寿命、属性和原生事件由宿主决定。原生 callback、Events 子类与 EventsType 均保留，同请求同方案的事件共用实例。

`DistributedTicketStoreOptions` 默认绑定 `Leistd:Security:Tickets`，也可传 `configSectionPath`；先绑定后应用委托，启动校验：

| 属性 | 默认值 | 约束 |
| --- | --- | --- |
| KeyPrefix | Leistd:AuthTicket: | 非空白 |
| FallbackLifetime | 5 分钟 | 正值；仅在票据未指定到期时间时使用 |

默认实现将票据与 OAuth 令牌保护后存入缓存，浏览器仅持引用。显式再次登录换引用版本，旧引用不能读取或撤销新票据；滑动续期不能复活已删除或到期票据。读写和删除传递取消令牌，持锁操作同时响应失锁取消。

直接服务端重载面向可信调用方；HTTP 读取、续期与删除校验 Cookie 引用版本；显式登录由原生事件标记。`DistributedTicketStore.TicketKeyProperty` 保存本机制的缓存键，需要它的宿主替换实现须提供相同元数据。数据保护用途固定，密钥环隔离由宿主的应用名负责。

### 验证码摘要

```csharp
builder.Services.AddVerificationCodeDigest();
```

默认 `IVerificationCodeDigest` 为单例 HMAC-SHA256 实现，可由宿主预先注册替换。`VerificationCodeOptions` 绑定 `Leistd:Security:VerificationCodes`，也可指定 `configSectionPath`；先绑定再应用配置委托。`Key` 是 Base64 格式、至少 32 字节的稳定服务端密钥，各副本和重启使用同一值。非空无效配置启动即失败，消息指明传入配置节。

缺失密钥允许解析服务，默认实现使用时抛出配置异常；宿主决定功能何时启用并补充“启用时必需密钥”的启动或业务设置验证。组件不生成回落密钥，不读取业务功能开关。

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
using System.Security.Claims;

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

### 身份验证器与恢复码

```csharp
var secret = Totp.GenerateSecret();
var manualKey = Totp.FormatSecret(secret);
var uri = Totp.BuildUri("Example", "user@example.com", secret);
var now = DateTimeOffset.UtcNow;
var code = Totp.ComputeCode(secret, Totp.TimeStepAt(now));
long? usedStep = Totp.Verify(secret, code, now, lastUsedStep: null);
var recoveryCodes = RecoveryCodes.Generate();
var recoveryDigest = RecoveryCodes.Hash(recoveryCodes[0]);
```

TOTP 使用 RFC 6238 的 HMAC-SHA1、30 秒步长和 6 位码，容忍前后各一步；默认生成 20 字节随机密钥。`Verify` 返回命中的步序号，格式错误、不匹配或不大于 `lastUsedStep` 时返回 null。时间使用 `DateTimeOffset`，调用方显式提供；空密钥、负时间步等编程错误抛 BCL 异常。

`FormatSecret` 导出大写无填充 Base32，`ParseSecret` 容忍大小写、空白、分隔符和正确尾填充，非法或不完整编码返回 null；Base32 实现不作为公共文本工具发布。`BuildUri` 编码密钥并转义发行方与账号。

恢复码每组 10 个，每个为 16 个随机 Base32 字符（80 位），按 4 字符分组；`Hash` 去掉空白和分隔符、转小写后计算 SHA-256。调用方保护 TOTP 密钥、只展示一次恢复码明文，并原子保存已用时间步或消费摘要。组件不存储使用记录、不限制业务尝试次数，也不接管事务。

验证码摘要使用带密钥 HMAC-SHA256，`Matches` 按固定时间比较；格式损坏摘要返回 false。验证码有效期、用途、租户/收件人绑定、尝试次数与原子消费仍由宿主实现。[RFC 6238](https://www.rfc-editor.org/rfc/rfc6238)、[RFC 4648](https://www.rfc-editor.org/rfc/rfc4648)。

## 接口参考

### `Leistd.Security.Users.ICurrentUser`

| 成员 | 说明 |
| --- | --- |
| `IsAuthenticated` | 当前主体是否已认证：任一身份已认证即为 `true`（与官方授权管线判定"已认证用户"一致），无主体时为 `false`。标识、名字、租户仍只取自带标识的主体身份 |
| `SubjectId` | 主体标识原始值，按 `ClaimTypeOptions.UserIds` 读取；机器主体是 `client:<client_id>`。审计等"任何主体都要留得下标识"的场景用它 |
| `Id` | 在 `SubjectId` 之上只接受 `Guid` 的自然人用户 Id；机器主体等非 GUID 标识为 `null` |
| `TenantId` | 所属租户，按 `ClaimTypeOptions.ReadTenant` 读取；宿主用户返回 `null`，租户 claim 非法时抛 `InvalidOperationException`（不当作宿主）。这是主体 claim 的直读值，运行时权威租户上下文是 [多租户组件](./multi-tenancy.md) 的 `ICurrentTenant` |
| `Username` | 依次取 `preferred_username` / `name` / `Name` claim，均无返回 `null` |
| `Name` | 标准身份名称，依次取 `name` / `Name` claim，不回退 `given_name` |
| `Email` | 邮箱，依次取 `email` / `Email` claim |
| `FindClaim(claimType)` | 指定类型的第一个 `Claim`，跨全部身份查找（官方 `ClaimsPrincipal.FindFirst`），不存在返回 `null` |
| `FindClaims(claimType)` | 指定类型的全部 `Claim`（多值的角色、scope、amr），与 `FindClaim` 同一范围；没有主体时为空 |
| `IsInRole(role)` | 当前用户是否属于该角色：只看主体身份，按其 `RoleClaimType` 精确匹配；没有带用户标识的身份时按整个主体判断 |

`Username`、`Name`、`Email` 只在主体身份（`ClaimTypeOptions.FindSubjectIdentity`）上读取，与 `SubjectId`、`TenantId` 同源：主体身份没带 `name` 时，不会取到其他认证身份上的名字。主体上没有带用户标识的身份时按整个主体读取。

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
| `Subject` | `sub` | 主体标识（OIDC 标准）：自然人是 GUID 用户 Id，机器主体带 `client:` 前缀（见下文 `ClientSubject`）；审计等需要任何主体都留下标识的场景读它的原始值 |
| `ClientId` | `client_id` | OAuth2/OIDC 客户端标识符 |
| `SessionId` | `sid` | 会话标识符（OIDC 标准） |
| `IdentityProvider` | `idp` | 身份提供者（如 github / google / microsoft） |
| `IsSuperAdmin` | `is_super_admin` | 是否超级管理员（权限授权的超管判定约定来源） |
| `TenantId` | `tenant_id` | `ClaimTypeOptions.TenantId` 的默认值。读写租户 claim 一律经 `ClaimTypeOptions`，不直接用这个常量 |
| `ImpersonatorUserId` | `impersonator_id` | 模拟登录时真实操作人的用户 Id；非模拟场景没有此 claim |
| `ImpersonatorUserName` | `impersonator_name` | 模拟登录时真实操作人的显示名快照；非模拟场景没有此 claim |

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

> 多个认证方案合并主体时仍遵守该契约；用户访问令牌的跨服务使用见[服务间调用客户端](./service-client.md)。

### `Leistd.Security.Claims.ClaimsPrincipalExtensions`（是否匿名）

| 成员 | 说明 |
| --- | --- |
| `HasAuthenticatedIdentity()` | 主体的任一身份已认证即为 `true`，`null` 为 `false`；与官方授权管线一致，框架各处判断匿名都用它。不要用只看第一个身份的 `ClaimsPrincipal.Identity.IsAuthenticated` |

### `Leistd.Security.Claims.ClaimTypeOptions`（claim 类型与读取规则）

| 成员 | 说明 |
| --- | --- |
| `UserIds` | 主体标识的读取顺序，默认 `sub`，其次 `ClaimTypes.NameIdentifier` |
| `TenantId` | 租户 claim 类型，默认 `tenant_id`；值必须是租户 GUID，没有即宿主 |
| `FindSubjectIdentity(principal)` | 主体身份：按顺序第一个带用户标识（按 `UserIds`）的身份；没有时为 `null`。标识、租户、名字、邮箱都取自它 |
| `FindUserId(principal)` | 在主体身份上按 `UserIds` 取第一个非空白的原始值 |
| `ReadTenant(principal)` | 返回 `TenantClaim`，租户取自主体身份。非法：同一身份内多条（即使值相同）或非 GUID；其他带用户标识的身份带着与主体身份不同的租户（含主体身份为宿主）。不带用户标识的补充身份只在主体身份没有租户时提供租户；带用户标识而无租户 claim 的其他身份不参与判定；各身份带同一租户合法 |

`ICurrentUser.Id` 在原始值之上只接受 GUID；审计、SignalR 寻址等场景读原始值。

```csharp
bool isNaturalPerson = Guid.TryParse(claimTypes.Value.FindUserId(principal), out _);
```

## 注意事项

- 各属性在缺失对应 claim 时返回 `null`（`Id` 在 claim 无法解析为 `Guid` 时同样返回 `null`），调用方需做空值处理。
- 业务代码判断当前用户的角色用 `ICurrentUser.IsInRole`（只看主体身份）；授权策略 `RequireRole` 与官方 `ClaimsPrincipal.IsInRole` 看整个主体。两者都只认身份的 `RoleClaimType`、角色名区分大小写。自行构造 `ClaimsIdentity` 时，`roleType` 要与写入角色 claim 的类型一致（如 OIDC 的 `role`），否则判定静默为 `false`。
- `Change(...)` 基于 `AsyncLocal` 支持异步传播和嵌套，但返回的 `IDisposable` 必须释放。
- `HttpContextCurrentPrincipalAccessor` 依赖 `IHttpContextAccessor`，在没有 HTTP 上下文的后台任务里 `Principal` 为 `null`；此类场景用 `IAmbientContext.Begin(...)` 显式建立系统主体。
- 领域层/应用层使用 `Leistd.Security.Core` 与按需的 `Leistd.Security.OneTimeCodes`，避免引入 ASP.NET Core 集成包。
