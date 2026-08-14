# 服务间调用客户端

多个后端服务相互调用时，每个调用点都要重复解决同一批问题：请求打到哪里、带什么凭据、TraceId 和用户身份怎么跨服务延续、失败时对端返回了什么。如果各自手写 `HttpClient`，调用就成了黑盒——日志断链、认证各搞一套、错误响应五花八门。

Leistd 把服务间调用收敛为一条标准管道：`AddServiceClient` 注册强类型客户端并装配调用日志、TraceId 透传、用户上下文头注入；`AddClientCredentials` 追加 OAuth2 client credentials 认证（token 缓存与 401 自愈）；被调方用 `UseServiceUserContext` 在受信前提下从请求头恢复用户主体；响应用 `ReadResultAsync` 解包统一响应并把远端错误还原为强类型异常。

## 何时使用

| 场景 | 用法 | 包 |
| --- | --- | --- |
| 编写业务 Client 包：只声明接口 + 特性，HTTP 实现由 Refit 生成（**推荐**） | `AddRefitServiceClient<TApi, TOptions>` | `Leistd.ServiceClient.Refit` |
| 手写客户端实现，需要标准管道（日志/追踪/用户头） | `AddServiceClient<TClient, TImpl, TOptions>` | `Leistd.ServiceClient.Core` |
| 服务间需要 OAuth2 client credentials 认证 | 在返回的 builder 上 `.AddClientCredentials(...)` | `Leistd.ServiceClient.OAuth` |
| 作为被调方，接收携带 `X-User-*` 头的服务调用 | `AddServiceUserContext()` + `UseServiceUserContext()` | `Leistd.ServiceClient.AspNetCore` |
| 解析统一响应 / 还原远端错误 | `response.ReadResultAsync<T>()` 等扩展 | `Leistd.ServiceClient.Core` |

> Core 平台无关（Worker 等非 Web 宿主可用）；OAuth、AspNetCore 均传递引用 Core。
> 典型分工：调用方引 Core + OAuth；被调方引 AspNetCore；双向互调的服务三者都引。

## 安装

```bash
# 调用方核心：注册入口、标准管道、响应解包、异常类型
dotnet add package Leistd.ServiceClient.Core

# 调用方认证：client credentials token 获取/缓存/401 自愈（传递引用 Core）
dotnet add package Leistd.ServiceClient.OAuth

# 被调方：用户上下文恢复中间件（传递引用 Core）
dotnet add package Leistd.ServiceClient.AspNetCore

# Refit 接口式客户端（推荐编写业务 Client 包时使用；传递引用 Core 与 Refit）
dotnet add package Leistd.ServiceClient.Refit
```

> 声明 Refit 接口的项目还需直接引用 `Refit` 包以激活源生成器（接口须 public，
> 或 internal + `InternalsVisibleTo`）。

> 本仓库的模板项目通过中央包管理（CPM）统一版本，添加时无需写版本号。

## 调用方（推荐）：Refit 接口式客户端

业务 Client 包只声明接口 + Refit 特性，HTTP 实现由源生成器产出——URL 拼接、query 编码、multipart 构造均不再手写：

```csharp
public class OrderServiceClientOptions : ServiceClientOptions;

public interface IOrderServiceClient
{
    [Get("/api/v1/orders/{id}")]
    Task<OrderDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    [Multipart]
    [Post("/api/v1/orders/{id}/attachments")]
    Task UploadAsync(Guid id, [AliasAs("file")] StreamPart file, CancellationToken cancellationToken = default);
}
```

注册（配置节与手写路径相同，`Leistd:ServiceClients:OrderService`）：

```csharp
builder.Services
    .AddRefitServiceClient<IOrderServiceClient, OrderServiceClientOptions>(
        "OrderService", builder.Configuration)
    .AddClientCredentials(builder.Configuration);
```

规则：

- **一律经 `AddRefitServiceClient` 注册**，不要用裸 `AddRefitClient`——否则错误语义退回 Refit 默认的 `ApiException`，与手写路径的 `RemoteServiceException` 契约分叉。
- 统一 `RefitSettings`（`ServiceClientRefitSettings.Create()`）：System.Text.Json Web 默认序列化 + 非 2xx 经 `ExceptionFactory` 还原为 `RemoteServiceException`；可传入自定义 `RefitSettings` 覆盖。
- 无法内联源生成的方法形态（multipart、原始响应等）由 `Leistd.ServiceClient.Refit` 自带的 `Refit.Reflection` 反射构建器承接，业务包无需处理 RF006 诊断。
- 远端返回统一响应信封（`Result<T>`）的服务：接口返回类型直接声明 `Task<Result<OrderDto>>`，或返回 `Task<HttpResponseMessage>` 后用 `ReadResultAsync<T>()` 解包。

## 数据格式规范（Refit 路径）

| 格式 | 写法 | 注意 |
| --- | --- | --- |
| JSON 请求体 | `[Body] OrderDto dto` | 默认 STJ Web 约定（camelCase），与 Leistd 服务端一致 |
| 表单 `x-www-form-urlencoded` | `[Body(BodySerializationMethod.UrlEncoded)]`，接受 `IDictionary` 或普通对象 | 对象的公共可读属性→字段；`[AliasAs]` 重命名字段 |
| 文件上传 `multipart/form-data` | 方法标 `[Multipart]`，参数用 `StreamPart(stream, fileName, contentType)` / `ByteArrayPart` / `FileInfoPart` | 字段名用 `[AliasAs]` 指定；旧 `AttachmentName` 特性已废弃禁用 |
| 二进制/文件下载 | 返回 `Task<HttpResponseMessage>`，自行读 `Content` 流 | **原始响应不经 `ExceptionFactory`**：读流前必须先 `await response.EnsureRemoteSuccessAsync()` 完成错误还原 |
| Query 参数 | 方法参数自动拼接；`[Query]` / `[AliasAs]` 控制命名与格式 | 值自动 URL 编码（含中文/保留字符） |
| 响应元数据 | `Task<ApiResponse<T>>`（状态码/头） | 仅诊断场景；常规业务方法直接 `Task<T>` |

## 调用方（基线）：手写客户端

为下游服务定义强类型客户端与配置类型：

```csharp
public class OrderServiceClientOptions : ServiceClientOptions;

public interface IOrderServiceClient
{
    Task<OrderDto?> GetAsync(Guid id);
}

public class OrderServiceClient(HttpClient httpClient) : IOrderServiceClient
{
    public async Task<OrderDto?> GetAsync(Guid id)
    {
        var response = await httpClient.GetAsync($"api/v1/orders/{id}");
        // 远端启用了 Leistd.Response 统一包装 → ReadResultAsync 解包 {code,message,data}；
        // 远端直接返回 DTO（如本仓库模板生成的服务）→ 改用 ReadContentAsync<OrderDto>()
        return await response.ReadResultAsync<OrderDto>();
    }
}
```

在 `Program.cs` 注册（配置绑定 `Leistd:ServiceClients:OrderService`）：

```csharp
builder.Services
    .AddServiceClient<IOrderServiceClient, OrderServiceClient, OrderServiceClientOptions>(
        "OrderService", builder.Configuration)
    .AddClientCredentials(builder.Configuration);
```

配置分两层——**调用身份全局一次**（一个服务作为调用方只有一个 client_id/secret），
目标服务级差异只有地址与可选 scope：

```json
{
  "Leistd": {
    "ServiceAuth": {
      "Authority": "http://identity-service",
      "ClientId": "inventory-service",
      "ClientSecret": "<来自密钥管理，勿入库>"
    },
    "ServiceClients": {
      "OrderService": {
        "BaseAddress": "http://order-service",
        "Timeout": "00:00:30",
        "Scope": "order-api"
      },
      "UserService": { "BaseAddress": "http://user-service" }
    }
  }
}
```

`Leistd:ServiceAuth` 可含默认 `Scope`；客户端节的 `Scope` 存在时覆盖它。

`AddServiceClient` 返回 `IHttpClientBuilder`，可继续叠加宿主自己的处理器（如
`Microsoft.Extensions.Http.Resilience` 的 `.AddStandardResilienceHandler()`）；SDK 不内置重试熔断。

### 调用管道

`AddServiceClient` 装配的处理器链（自外向内）：

```
业务代码 → 调用日志 → TraceId 透传 → X-User-* 注入 → Bearer 认证(OAuth 包) → 网络
```

- **TraceId 透传**复用 `Leistd.Tracing.HttpClient`：宿主注册了 `AddCorrelationIdCore` /
  `AddCorrelationId` 才生效，未注册时该环节直通，本组件不代为注册。
- **用户头注入**依赖 `ICurrentUser`：宿主注册了 `Leistd.Security`（Web 宿主 `AddSecurity()`）才生效。
- **Bearer 认证**由 `AddClientCredentials` 追加在最内层：401 重试对日志与上层透明。

## 用户上下文传递

调用方把当前用户写入请求头（仅在请求尚无同名头时追加）：

| 头 | 来源 | 默认 |
| --- | --- | --- |
| `X-User-Id` | `ICurrentUser.Id` | 转发 |
| `X-User-Name` | `ICurrentUser.Username`（UTF-8 URL 编码） | 转发（`ForwardUserName` 可关） |
| 自定义 | `UserContext.ClaimHeaderMap`（claim → 头名，URL 编码） | 不转发 |

角色、权限**不经头传递**：被调方对服务调用的授权应基于调用方 client 的 scope，或按用户 Id 本地判定。

后台任务无 HTTP 上下文时，先用 `ICurrentPrincipalAccessor.Change(...)` 设定主体再调用，
用户头即可正常携带（见[当前用户与身份信息](./security.md)）。

## 被调方：恢复用户上下文

```csharp
builder.Services.AddServiceUserContext(builder.Configuration); // 绑定 Leistd:ServiceUserContext

var app = builder.Build();
app.UseAuthentication();
app.UseServiceUserContext(); // 必须在 UseAuthentication 之后、UseAuthorization 之前
app.UseAuthorization();
```

**信任边界**（三个条件同时满足才采信 `X-User-*` 头）：

1. 当前主体已通过认证且含 `client_id` claim；
2. `sub` 是该 client 的机器主体（`ClientSubject` 契约，即 `client:<client_id>`）；
3. 令牌持有**委托 scope**（`RequiredScope`，默认 `svc.delegate`）。

满足时把用户身份作为**主身份**加入 `HttpContext.User` 并保留调用方 client 身份，
此后 `ICurrentUser`（用户）与 `ICurrentClient`（调用方服务）双通道可用；不满足时按配置
**剥离**这些头，阻断伪造链路。

**委托 scope 是安全默认（fail-closed）**：认证成功只说明调用方是已认证的工作负载，不等于它有权
代表用户。若不区分两者，任何拿到 client credentials 令牌的客户端（包括只该同步公开数据的第三方
集成）只要知道用户 Id 就能冒充该用户，继承其角色与直授权限。认证服务需在客户端注册时**显式授予**
该 scope（OpenIddict 中为权限项 `scp:svc.delegate`），调用方在
`Leistd:ServiceClients:<服务名>:Scope` 配置它以在取令牌时申请。

**签发端必须遵循同一契约**：认证服务签发 client credentials 令牌时，`sub` 用
`ClientSubject.Format(clientId)` 构造（`Leistd.Security.Core` 的
[`ClientSubject`](./security.md)）。这既是本组件的信任判据，也把机器主体与自然人主体
（`sub` 是用户 GUID）隔离在不可碰撞的两个命名空间——否则 `client_id` 由创建者任意指定，
挑一个已存在的用户 Id 就能让机器令牌被解析成那个人。

被调方的 Bearer token 验证不属于本组件：宿主自行配置 OpenIddict Validation（或等价 JWT 验证）
指向身份服务 issuer。

## 响应解包与错误还原

| 远端响应 | 行为 |
| --- | --- |
| 2xx 且 `code = 0` | `ReadResultAsync<T>()` 返回 `data`；`ReadResultAsync()` 用于无数据的 `Result` |
| 2xx 且 `code ≠ 0` | 抛 `RemoteServiceException`（`ErrorCode` = 信封 code） |
| 非 2xx（ProblemDetails） | 抛 `RemoteServiceException`，解析 `code`/`message`/`traceId`/`errors` |
| 非 2xx（非 JSON 体） | 抛 `RemoteServiceException`，`ResponseBody` 保留原始体（截断 4096 字符） |
| 网络失败 / 超时 / 反序列化失败 | 抛 `ServiceClientException`（调用方主动取消除外，原样上抛） |
| 远端 `[NoWrap]` 端点 | `ReadContentAsync<T>()` 直接反序列化；文件流直接读 `Content`，先调 `EnsureRemoteSuccessAsync()` |

远端错误**不映射回本地业务异常**——远端 404 不等于本地资源不存在。调用方捕获
`RemoteServiceException` 后按需自行翻译：

```csharp
try
{
    var order = await orderServiceClient.GetAsync(id);
}
catch (RemoteServiceException ex) when (ex.StatusCode == 404)
{
    // ex.ErrorCode 远端业务码；ex.RemoteTraceId 可直接用于跨服务日志检索
}
```

## 调用日志

日志类别 `Leistd.ServiceClient.<服务名>`：

- **Information**：每次调用一行摘要 `{Service} {Method} {Uri} 响应 {StatusCode}，耗时 {ElapsedMs}ms`；
  非 2xx 为 **Warning**，传输层异常为 **Error**。TraceId 由链路追踪组件的日志 Scope
  （`leistd.correlationId.traceId`）附着，需日志库启用 Scope 富化。
- **Debug**（`LogPayloads = true`）：请求/响应体按 `MaxPayloadLength` 截断；
  `Authorization`、`Cookie`、`X-User-*` 头一律脱敏为 `***`。

## 接口参考

### `Leistd.ServiceClient`（Core）

| 成员 | 说明 |
| --- | --- |
| `AddServiceClient<TClient, TImpl, TOptions>(services, serviceName, IConfiguration)` | 注册强类型客户端并装配标准管道，Options 绑定 `Leistd:ServiceClients:<serviceName>`；返回 `IHttpClientBuilder` |
| `AddServiceClient<TClient, TImpl, TOptions>(services, serviceName, Action<TOptions>)` | 同上，委托配置版 |
| `ServiceClientOptions` | 配置基类：`BaseAddress`、`Timeout`、`LogPayloads`、`MaxPayloadLength`、`UserContext` |
| `UserContextForwardingOptions` | 用户头转发：`Enable`、`ForwardUserName`、`ClaimHeaderMap` |
| `ServiceClientHeaders` | 头名常量：`UserId`（`X-User-Id`）、`UserName`（`X-User-Name`） |
| `ServiceClientScopes` | scope 常量：`Delegation`（`svc.delegate`，代表用户调用的授权开关） |
| `AddServiceClientPipeline<TOptions>(builder, serviceName)` | 在既有 `IHttpClientBuilder` 上装配标准能力（BaseAddress/Timeout/日志/追踪/用户头），供 Refit 等注册形态复用 |
| `ReadResultAsync<T>()` / `ReadResultAsync()` | 解包统一响应，失败抛 `RemoteServiceException` |
| `ReadContentAsync<T>()` | 未包装端点直接反序列化（同样先做错误还原） |
| `EnsureRemoteSuccessAsync()` | 仅做非 2xx → `RemoteServiceException` 还原，供文件流等场景 |
| `CreateRemoteErrorAsync()` | 从非 2xx 响应构造 `RemoteServiceException`（只构造不抛出，供 ExceptionFactory 等挂载点） |
| `ServiceClientException` | 客户端侧异常（继承 `Leistd.Core` 的 `CommonException`） |
| `RemoteServiceException` | 远端错误（继承 `ServiceClientException`）：`StatusCode`、`ErrorCode`、`RemoteTraceId`、`Errors`、`ResponseBody` |

### `Leistd.ServiceClient.Refit`

| 成员 | 说明 |
| --- | --- |
| `AddRefitServiceClient<TApi, TOptions>(services, serviceName, IConfiguration, RefitSettings?)` | 注册 Refit 接口客户端并装配标准管道，Options 绑定 `Leistd:ServiceClients:<serviceName>`；返回 `IHttpClientBuilder` |
| `AddRefitServiceClient<TApi, TOptions>(services, serviceName, Action<TOptions>, RefitSettings?)` | 同上，委托配置版 |
| `ServiceClientRefitSettings.Create(JsonSerializerOptions?)` | 统一 `RefitSettings`：STJ Web 序列化 + 非 2xx 还原为 `RemoteServiceException` |

### `Leistd.ServiceClient.OAuth`

| 成员 | 说明 |
| --- | --- |
| `AddClientCredentials(builder, IConfiguration)` | 追加 client credentials 认证：全局 `Leistd:ServiceAuth`（调用身份）+ 客户端节 `Scope` |
| `DependencyInjection.ServiceAuthSectionName` | 全局调用身份配置节名常量（`Leistd:ServiceAuth`） |
| `AddClientCredentials(builder, Action<ClientCredentialsOptions>)` | 同上，委托配置版 |
| `ClientCredentialsOptions` | `Authority` / `TokenEndpoint`（默认 `{Authority}/connect/token`）、`ClientId`、`ClientSecret`、`Scope`、`ExpirationBuffer`（默认 60s） |
| `IServiceTokenProvider` | token 获取抽象：`GetAccessTokenAsync(clientName)` / `Invalidate(clientName)` |
| `ClientCredentialsTokenProvider` | 默认实现（Singleton）：按具名客户端缓存、过期缓冲、并发单飞 |

### `Leistd.ServiceClient.AspNetCore`

| 成员 | 说明 |
| --- | --- |
| `AddServiceUserContext(services, IConfiguration)` | 注册恢复配置（绑定 `Leistd:ServiceUserContext`）与认证阶段的 ClaimsTransformation |
| `AddServiceUserContext(services, Action<ServiceUserContextOptions>?)` | 同上，委托配置版 |
| `UseServiceUserContext()` | 启用中间件（`UseAuthentication` 之后、`UseAuthorization` 之前）：剥离不受信头 + 兜底恢复 |
| `ServiceUserContextClaimsTransformation` | `IClaimsTransformation` 实现：在每次认证内恢复用户主体（含授权策略按 scheme 重认证的路径） |
| `ServiceUserContextOptions` | `Enable`、`UserIdHeader`、`UserNameHeader`、`HeaderClaimMap`、`RemoveUntrustedHeaders`、`RequiredScope`（默认 `svc.delegate`）、`AuthenticationType` |

## 实现行为

### Leistd.ServiceClient.Core

- `AddServiceClient` 内部调用 `AddHttpClient<TClient, TImpl>(serviceName, ...)`：`BaseAddress` 结尾自动补 `/`，`Timeout` 应用到 `HttpClient.Timeout`。
- 管道各环节按可选能力**在构建时探测**：`ICorrelationIdProvider` 未注册则追踪环节直通，`ICurrentUser` 未注册或 `UserContext.Enable=false` 则用户头环节直通。处理器实例随 `HttpClientFactory` 的 handler 生命周期（默认 2 分钟）轮换，期间的 Options 变更在轮换后生效。
- 日志处理器把传输层异常包装为 `ServiceClientException`；`OperationCanceledException` 且调用方令牌已取消时原样上抛。
- 用户头注入读取的是**发送时刻**的 `ICurrentUser`（底层 `AsyncLocal`），处理器被缓存复用不影响每请求取值。

### Leistd.ServiceClient.Refit

- `AddRefitServiceClient` = `AddRefitClient<TApi>(settings, httpClientName: serviceName)` + `AddServiceClientPipeline<TOptions>`——Refit 客户端与手写客户端共享同一条 handler 管道与配置节。
- `ServiceClientRefitSettings.Create()` 的 `ExceptionFactory` 对非 2xx 响应调用 `CreateRemoteErrorAsync` 构造 `RemoteServiceException`；返回 `HttpResponseMessage` 的方法不经该钩子（拿到原始响应）。
- 包自带 `Refit.Reflection`：无法内联源生成的方法（RF006）自动落到反射构建器，可生成的方法仍走源生成实现。

### Leistd.ServiceClient.OAuth

- token 请求走独立具名客户端 `Leistd.ServiceClient.OAuth.Token`（不带认证/用户头处理器，避免管道递归），表单为标准 `grant_type=client_credentials` + `client_id` + `client_secret` [+ `scope`]。
- 令牌按具名客户端缓存至 `expires_in - ExpirationBuffer`；并发获取经 `SemaphoreSlim` 单飞，同一时刻同名客户端只有一个 token 请求在途。
- 认证处理器在请求**尚无** `Authorization` 头时才介入；收到 401 时失效缓存、强制重取并克隆请求重试一次（请求体已预缓冲，克隆完整），仍 401 则原样返回。
- token 端点不可达或返回非 2xx 抛 `ServiceClientException`（含端点与响应体片段）。

### Leistd.ServiceClient.AspNetCore

- **恢复发生在认证阶段**：`AddServiceUserContext` 注册的 `ServiceUserContextClaimsTransformation`（`IClaimsTransformation`）在每次 `AuthenticateAsync` 内生效。仅靠中间件改写 `HttpContext.User` 不够——授权策略显式声明认证 scheme 时，`PolicyEvaluator` 会按 scheme 重认证并覆盖 `HttpContext.User`，中间件改写的主体在该路径上会被丢弃。转换幂等（已恢复的主体原样返回）。
- **不吞掉宿主已有的 `IClaimsTransformation`**：ASP.NET Core 只消费单个实现（后注册者覆盖先注册者），因此 `AddServiceUserContext` 把注册时已存在的**默认**实现（keyed 注册属独立空间，不受影响）包进组合——**先宿主既有转换（租户、外部身份等 claims 富化），再用户上下文恢复**（恢复会更换主身份，应基于富化后的主体）。重复调用不叠加。宿主若在其**之后**才注册自己的转换，仍会覆盖该组合；此时由宿主负责组合（`ServiceUserContextClaimsTransformation` 是公共类型，可直接注入调用）。
- **组合保留宿主注册的生命周期与释放语义**：沿用被包装注册的生命周期（宿主常把转换注册为 Scoped，它往往依赖请求级服务），不会把 scoped 依赖提升为单例；内层由组合创建后 DI 不再跟踪它，释放责任随所有权转移到组合——宿主自行 `new` 的实例（`ImplementationInstance`）容器本就不拥有，不代为释放；内层**仅**实现 `IAsyncDisposable` 时同步释放作用域会抛 `InvalidOperationException`，与原生 DI 行为一致（请用 `await using` / `DisposeAsync`）。
- 中间件职责：不受信时剥离用户头；受信但认证阶段未恢复时兜底恢复 `HttpContext.User`。`Enable=false` 时完全直通（不恢复也不剥离）。
- 受信判定：主体已认证 + 含 `client_id` claim + `sub` 匹配 `ClientSubject.Format(clientId)`（`sub` 缺失时回退 `ClaimTypes.NameIdentifier`）+ 持有 `RequiredScope`（默认 `svc.delegate`；同时识别空格分隔的 `scope` claim 与 OpenIddict 的多值 `oi_scp` claim）。`RequiredScope` 置空即关闭该校验——那意味着任何机器令牌都能代表任意用户，仅在部署上另有等价管控时才这么做。
- 恢复时构造 `sub` / `preferred_username` / 自定义映射 claim 的 `ClaimsIdentity`（`AuthenticationType` 默认 `ServiceUserContext`）置于主体首位，原有身份全部保留。
- 受信但无 `X-User-Id` 头：服务以自身身份调用，主体保持不变。
- 不受信且 `RemoveUntrustedHeaders=true`（默认）：从请求中移除 `UserIdHeader`、`UserNameHeader` 与 `HeaderClaimMap` 声明的所有头。

## 配置项 / Options

`ServiceClientOptions`（每客户端一个派生类型，绑定 `Leistd:ServiceClients:<服务名>`）：

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `BaseAddress` | `null` | 下游服务基础地址，结尾自动补 `/` |
| `Timeout` | 30s | 单次调用超时 |
| `LogPayloads` | `false` | Debug 级别记录请求/响应载荷（脱敏、截断） |
| `MaxPayloadLength` | 4096 | 载荷日志截断长度 |
| `UserContext.Enable` | `true` | 用户头转发开关 |
| `UserContext.ForwardUserName` | `true` | 是否转发 `X-User-Name` |
| `UserContext.ClaimHeaderMap` | 空 | 额外 claim → 头名映射 |

`ClientCredentialsOptions`（分层绑定：全局 `Leistd:ServiceAuth` → 客户端节 `Leistd:ServiceClients:<服务名>:Scope`）：

| 属性 | 默认值 | 配置位置 | 说明 |
| --- | --- | --- | --- |
| `Authority` | `null` | 全局 | 身份服务基础地址 |
| `TokenEndpoint` | `{Authority}/connect/token` | 全局 | token 端点，设置后覆盖默认推导 |
| `ClientId` / `ClientSecret` | 空 | 全局 | 本服务的调用凭据（Secret 来自密钥管理，环境变量 `Leistd__ServiceAuth__ClientSecret`） |
| `Scope` | `null` | 全局默认 + 客户端节覆盖 | 申请的 scope，空格分隔多个 |
| `ExpirationBuffer` | 60s | 全局 | 提前刷新缓冲 |

`ServiceUserContextOptions`（绑定 `Leistd:ServiceUserContext`）：

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `Enable` | `true` | 总开关；`false` 时不恢复也不剥离 |
| `UserIdHeader` / `UserNameHeader` | `X-User-Id` / `X-User-Name` | 头名 |
| `HeaderClaimMap` | 空 | 额外头 → claim 映射（与调用方 `ClaimHeaderMap` 对应） |
| `RemoveUntrustedHeaders` | `true` | 不受信时剥离用户头 |
| `RequiredScope` | `svc.delegate` | 要求调用方 token 持有的委托 scope；置空则关闭校验（不建议） |
| `AuthenticationType` | `ServiceUserContext` | 恢复身份的 AuthenticationType |

## 注意事项

- **网关必须剥离外部来源的内部头**：`X-User-*` 只应在服务网格内出现。中间件默认剥离不受信头是最后防线，Ingress/网关层应同步配置剥离。
- `ClientSecret` 不要写入源码或提交的配置文件，从环境变量或密钥管理注入。
- 401 自愈只重试一次；重试仍 401 说明凭据本身失效（client 被禁用/密钥轮换未同步），会以 `RemoteServiceException` 形式浮出。
- `LogPayloads` 会完整缓冲响应体，勿在文件下载等大响应客户端上开启。
- 追踪与用户头转发是**跟随宿主显式组合**的：忘记 `AddCorrelationIdCore` / `AddSecurity()` 不报错，只是对应能力静默缺失；联调时先检查这两项注册。
- 远端错误经 `RemoteServiceException` 上抛后，若不捕获会被本方全局异常处理器按 500 归一化——对可预期的远端失败（如库存不足）应在调用方捕获并翻译为本方业务异常。

## 相关

- [链路追踪](./tracing.md)
- [当前用户与身份信息](./security.md)
- [统一 API 响应](./response.md)
- [业务异常与全局异常处理](./exception.md)
