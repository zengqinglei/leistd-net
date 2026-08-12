# 服务间调用

本项目基于 `Leistd.ServiceClient.*` 组件与其他服务互调：调用日志、TraceId 透传、用户上下文传递与 OAuth2 client credentials 认证由标准管道承担，业务代码只面向强类型客户端。组件完整用法见随包文档（任一 `Leistd.ServiceClient.*` 包内 `docs/service-client.md`）。

## 调用其他服务

1. 引用目标服务发布的 Client 包（形态见本项目 `src/{ProjectName}.Client`）。
2. 在 `Program.cs` 注册并在配置中给出地址与凭据：

```csharp
builder.Services.AddOrderServiceClient(builder.Configuration);
```

```json
{
  "Leistd": {
    "ServiceClients": {
      "OrderService": {
        "BaseAddress": "http://order-service",
        "Auth": {
          "Authority": "http://identity-service",
          "ClientId": "本服务在身份服务注册的 client_id",
          "ClientSecret": "从环境变量/密钥管理注入，勿提交",
          "Scope": "order-api"
        }
      }
    }
  }
}
```

3. 业务代码注入客户端接口调用；远端错误以 `RemoteServiceException`（含远端 `traceId`、业务 `code`）抛出，可预期失败应捕获并翻译为本服务的业务异常。

当前用户的 Id/用户名会自动经 `X-User-Id` / `X-User-Name` 头传给被调方；后台任务先用 `ICurrentPrincipalAccessor.Change(...)` 设定主体再调用。

## 被其他服务调用

- `Program.cs` 已注册 `AddServiceUserContext` + `UseServiceUserContext`（OpenIddict 场景）：仅当调用方以 client credentials 令牌通过认证（`sub == client_id`）时才采信 `X-User-*` 头并恢复用户主体，其余请求一律剥离这些头。**网关/Ingress 必须同步剥离外部来源的 `X-User-*` 头**。
- 调用方凭据在本服务的「开放应用」中注册（confidential 客户端 + `client_credentials` 授权），凭据仅创建时返回一次。
- 默认授权策略要求可用的自然人用户：服务间调用需携带受信 `X-User-Id`；确需面向纯工作负载的端点，单独声明策略，不放宽默认策略。
- 调用诊断：`GET /api/v1/service-info`（匿名探活）、`GET /api/v1/service-info/whoami`（回显恢复出的用户与调用方 client）。

## 发布本服务的 Client 包

`src/{ProjectName}.Client` 是本服务的强类型调用客户端（**Refit 接口式**，HTTP 实现由源生成器产出）：

- 只依赖 `Leistd.ServiceClient.Refit` / `Leistd.ServiceClient.OAuth` + `Refit`（激活源生成器），不引用服务内部程序集；DTO 在包内自带，与服务端 DTO 独立演进。
- 新增对外接口时在 Refit 接口上补充方法与特性（`[Get]`/`[Post]`/`[Multipart]` 等）及对应 DTO，并保持 `Add{ProjectName}Client` 注册入口不变。数据格式写法（JSON/表单/multipart 文件/二进制下载）见组件随包文档 `docs/service-client.md` 的「数据格式规范」。
- **一律经 `AddRefitServiceClient` 注册**（`Add{ProjectName}Client` 已封装）：错误统一还原为 `RemoteServiceException`，不暴露 Refit 的 `ApiException`。
- 以 NuGet 包形式随服务版本发布（`dotnet pack`），消费方按服务版本升级。

集成测试 `ServiceInvocationTests` 覆盖「取令牌 → 受信恢复用户 → 伪造头阻断」闭环，改动认证或用户上下文相关代码后必须保持其通过。
