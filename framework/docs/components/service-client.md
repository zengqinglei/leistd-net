# 服务间调用客户端

服务调用统一处理日志、链路识别、用户与租户上下文、OAuth2 认证以及远程错误还原。Refit 是默认的业务客户端编写方式。

## 何时使用

| 场景 | 入口 | 包 |
| --- | --- | --- |
| 声明 Refit 业务客户端 | `AddRefitServiceClient` | `Leistd.ServiceClient.Refit` |
| 为手写客户端装配标准管道 | `AddServiceClient` | `Leistd.ServiceClient.Core` |
| 使用 client credentials 认证 | `AddClientCredentials` | `Leistd.ServiceClient.OAuth` |
| 被调方恢复委托用户上下文 | `AddServiceUserContext` + `UseServiceUserContext` | `Leistd.ServiceClient.AspNetCore` |
| 读取响应或还原远程错误 | `ReadContentAsync` / `EnsureRemoteSuccessAsync` | `Leistd.ServiceClient.Core` |

`Core` 不依赖 ASP.NET Core。OAuth、AspNetCore 和 Refit 包均传递引用 Core。

## 安装

```bash
dotnet add package Leistd.ServiceClient.Refit      # 推荐的调用方入口
dotnet add package Leistd.ServiceClient.OAuth      # client credentials
dotnet add package Leistd.ServiceClient.AspNetCore # 被调方用户上下文恢复
```

声明 Refit 接口的项目还需直接引用 `Refit` 以激活源生成器。接口应为 public；internal 接口需配置 `InternalsVisibleTo`。

## 注册

调用方优先声明 Refit 接口：

```csharp
public class OrderServiceClientOptions : ServiceClientOptions;

public interface IOrderServiceClient
{
    [Get("/api/v1/orders/{id}")]
    Task<OrderDto> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}

builder.Services
    .AddRefitServiceClient<IOrderServiceClient, OrderServiceClientOptions>(
        "OrderService",
        builder.Configuration)
    .AddClientCredentials(builder.Configuration);
```

不要用裸 `AddRefitClient`；它会跳过标准管道，并将远程错误退回 Refit 的 `ApiException`。需要手写实现时改用：

```csharp
builder.Services.AddServiceClient<
    IOrderServiceClient,
    OrderServiceClient,
    OrderServiceClientOptions>("OrderService", builder.Configuration);
```

`AddServiceClient` 和 `AddRefitServiceClient` 都返回 `IHttpClientBuilder`，可继续追加宿主的 resilience handler。本家族不内置通用重试或熔断策略。

被调方在认证与授权之间恢复上下文：

```csharp
builder.Services.AddServiceUserContext(builder.Configuration);

var app = builder.Build();
app.UseAuthentication();
app.UseServiceUserContext();
app.UseMultiTenancy(); // 使用多租户时
app.UseAuthorization();
```

Bearer token 验证不属于本家族，宿主需自行配置 OpenIddict Validation 或等价 JWT 验证。

## 使用

### 请求与响应

Refit 客户端默认使用 System.Text.Json Web 约定。常用形态如下：

| 数据 | 声明 |
| --- | --- |
| JSON | `[Body] OrderDto dto` |
| URL 编码表单 | `[Body(BodySerializationMethod.UrlEncoded)]` |
| 文件上传 | `[Multipart]` + `StreamPart` / `ByteArrayPart` / `FileInfoPart` |
| Query | 方法参数，用 `[Query]` / `[AliasAs]` 调整命名 |
| 原始响应或文件下载 | `Task<HttpResponseMessage>` |

**常规路径是裸载荷**：本框架的服务成功时直出对象、失败时返回 RFC 9457 Problem Details，不包信封。手写客户端使用：

```csharp
var response = await httpClient.GetAsync($"api/v1/orders/{id}");
var order = await response.ReadContentAsync<OrderDto>();
```

`ReadResultAsync<T>()` 是**互操作**路径：被调方产出 `{code, message, data}` 信封时才用它，典型是既有遗留服务；Refit 接口对应写成 `Task<Result<T>>`。两条路径的错误处理相同。

返回 `HttpResponseMessage` 的 Refit 方法不经错误工厂；读取流前必须调用 `EnsureRemoteSuccessAsync()`。

远程错误映射为：

| 响应 | 结果 |
| --- | --- |
| 2xx 且 `code = 0` | 返回 `data` |
| 2xx 且 `code != 0` | 抛 `RemoteServiceException` |
| 非 2xx ProblemDetails | 还原 `code`、`message`、`traceId` 和 `errors` |
| 非 2xx 非 JSON | 保留最多 4096 字符的 `ResponseBody` |
| 网络、超时或反序列化失败 | 抛 `ServiceClientException`；调用方主动取消原样上抛 |

不要将远程状态直接当作本地业务结果。需要转换时，捕获 `RemoteServiceException` 并使用 `RemoteStatusCode`、`ErrorCode` 和 `RemoteTraceId`。未处理的远程 5xx/408/429 对外映射为 503，其它 4xx 映射为 502。

### 调用管道

```text
业务代码 → 日志 → TraceId → 用户头 → 租户头 → Bearer 认证 → 网络
```

- TraceId 复用 `Leistd.Tracing.HttpClient`；未注册追踪组件时直通。
- 用户头读取发送时的 `ICurrentUser`；未注册 Security 时直通。
- 租户头读取 `ICurrentTenant`，与用户转发开关独立。
- `AddClientCredentials` 只在请求没有 `Authorization` 头时介入。收到 401 后使 token 失效、重取并重试一次。

OAuth token 按具名客户端缓存至 `expires_in - ExpirationBuffer`，并发获取使用单飞。token 请求使用独立客户端，不会递归进入业务管道。

### 委托用户上下文

调用方可传递：

| 头 | 来源 | 默认 |
| --- | --- | --- |
| `X-User-Id` | `ICurrentUser.Id` | 开启 |
| `X-Username` | `ICurrentUser.Username` | 开启，UTF-8 URL 编码 |
| `X-Tenant-Id` | `ICurrentTenant.Id` | 开启，独立于用户头开关 |
| 自定义 | `ClaimHeaderMap` | 关闭 |

角色与权限不通过请求头传递。被调方应根据调用方 scope 授权，或按用户 Id 在本地判定。

被调方只在以下条件同时成立时恢复用户上下文：

1. 当前主体已通过认证且包含 `client_id`。
2. `sub` 等于 `ClientSubject.Format(clientId)`，即 `client:<client_id>`。
3. token 持有 `RequiredScope`，默认 `svc.delegate`。

恢复后，用户身份作为主身份，原调用方身份仍保留；`ICurrentUser` 与 `ICurrentClient` 可同时使用。不可信调用默认移除用户头。租户头不在移除范围，它由多租户组件的主体优先规则继续约束。

### 调用日志

日志类别为 `Leistd.ServiceClient.<服务名>`。每次调用记录方法、URI、状态码与耗时；非 2xx 为 Warning，传输异常为 Error。

`LogPayloads = true` 时以 Debug 级别记录截断后的请求/响应体。`Authorization`、`Cookie` 和 `X-User-*` 头始终脱敏。

## 接口参考

| 类型或入口 | 用途 |
| --- | --- |
| `AddRefitServiceClient<TApi, TOptions>` | 注册 Refit 客户端并装配标准管道 |
| `AddServiceClient<TClient, TImpl, TOptions>` | 注册手写强类型客户端 |
| `AddServiceClientPipeline<TOptions>` | 为已有 `IHttpClientBuilder` 装配标准管道 |
| `AddClientCredentials` | 追加 client credentials 认证 |
| `AddServiceUserContext` / `UseServiceUserContext` | 注册并启用被调方上下文恢复 |
| `ReadContentAsync` / `ReadResultAsync` | 读取裸载荷（常规）或 `{code, message, data}` 信封（互操作） |
| `EnsureRemoteSuccessAsync` | 将原始非 2xx 响应还原为远程异常 |
| `ServiceClientException` | 网络、超时或反序列化等客户端故障 |
| `RemoteServiceException` | 远程错误及其状态码、业务码和 TraceId |

## 配置项

`Leistd:ServiceClients:<服务名>`：

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `BaseAddress` | `null` | 下游服务基础地址，结尾自动补 `/` |
| `Timeout` | 30s | 单次调用超时 |
| `LogPayloads` | `false` | 是否记录脱敏且截断的载荷 |
| `MaxPayloadLength` | 4096 | 载荷最大记录长度 |
| `UserContext.Enabled` | `true` | 用户头转发开关 |
| `UserContext.ForwardUsername` | `true` | 是否转发用户名 |
| `UserContext.ForwardTenantId` | `true` | 是否转发租户 Id，独立于 `Enabled` |
| `UserContext.ClaimHeaderMap` | 空 | 额外 claim 到请求头的映射 |

`Leistd:ServiceAuth`：

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `Authority` | `null` | 身份服务基础地址 |
| `TokenEndpoint` | `{Authority}/connect/token` | token 端点 |
| `ClientId` / `ClientSecret` | 空 | 本服务的调用凭据 |
| `Scope` | `null` | 全局默认 scope；客户端节可覆盖 |
| `ExpirationBuffer` | 60s | token 提前刷新缓冲 |

`Leistd:ServiceUserContext`：

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `Enabled` | `true` | 恢复与头移除总开关 |
| `UserIdHeader` / `UsernameHeader` | `X-User-Id` / `X-Username` | 用户标识头 |
| `TenantIdHeader` | `X-Tenant-Id` | 租户头；空字符串关闭恢复 |
| `HeaderClaimMap` | 空 | 额外请求头到 claim 的映射 |
| `RemoveUntrustedHeaders` | `true` | 是否移除不可信用户头 |
| `RequiredScope` | `svc.delegate` | 委托 scope；置空会允许任意机器令牌代表用户 |
| `AuthenticationType` | `ServiceUserContext` | 恢复身份的认证类型 |

## 注意事项

- Ingress/网关必须移除外部来源的 `X-User-*` 头；中间件的默认移除是最后防线。
- 认证服务必须用 `ClientSubject.Format(clientId)` 生成机器主体，并显式授予 `svc.delegate`。
- `ClientSecret` 从环境变量或密钥管理注入，不进入源码或提交的配置。
- 401 自愈只重试一次；第二次 401 作为远程错误返回。
- `LogPayloads` 会缓冲响应体，不应在文件下载等大响应客户端上启用。
- 追踪、当前用户与当前租户都是宿主显式组合的可选能力；未注册时相应 handler 直通。

## 相关

- [链路追踪](./tracing.md)
- [当前用户与身份信息](./security.md)
- [多租户](./multi-tenancy.md)
- [统一 API 响应](./response.md)
- [业务异常与全局异常处理](./exception-handling.md)
