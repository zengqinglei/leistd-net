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
    "ServiceAuth": {
      "Authority": "http://identity-service",
      "ClientId": "本服务注册的 client_id",
      "ClientSecret": "从环境变量/密钥管理注入，勿提交"
    },
    "ServiceClients": {
      "OrderService": { "BaseAddress": "http://order-service", "Scope": "order-api" },
      "UserService": { "BaseAddress": "http://user-service" }
    }
  }
}
```

`Leistd:ServiceAuth` 是**本服务自己的调用身份，全局只配一次**（一个服务作为调用方只有一个
client_id/secret），所有服务客户端共享；目标服务级差异只有 `BaseAddress` 与可选的 `Scope`。
ClientSecret 用环境变量注入：`Leistd__ServiceAuth__ClientSecret`。

3. 业务代码注入客户端接口调用；远端错误以 `RemoteServiceException`（含远端 `traceId`、业务 `code`）抛出，可预期失败应捕获并翻译为本服务的业务异常。

当前用户的 Id/用户名会自动经 `X-User-Id` / `X-User-Name` 头传给被调方；后台任务先用 `ICurrentPrincipalAccessor.Change(...)` 设定主体再调用。

## 被其他服务调用

- `Program.cs` 已注册 `AddServiceUserContext` + `UseServiceUserContext`（OpenIddict 场景）。采信 `X-User-*` 头并恢复用户主体需**同时**满足：调用方以 client credentials 令牌通过认证、其 `sub` 为机器主体契约形态（`client:<client_id>`）、且令牌持有委托 scope `svc.delegate`；任一不满足都会剥离这些头。**网关/Ingress 必须同步剥离外部来源的 `X-User-*` 头**。
- 调用方凭据在本服务的「开放应用」中注册（confidential 客户端 + `client_credentials` 授权），凭据仅创建时返回一次。
- **代表用户调用需额外授予 `svc.delegate`**（开放应用的权限项 `scp:svc.delegate`）：拿到机器令牌只代表调用方是已认证的工作负载，不等于有权代表用户——不授予该 scope 的客户端即使知道用户 Id 也无法冒充。调用方侧在 `Leistd:ServiceClients:<服务名>:Scope` 配置该 scope 以在取令牌时申请它。
- 默认授权策略要求可用的自然人用户：服务间调用需携带受信 `X-User-Id`；确需面向纯工作负载的端点，单独声明策略，不放宽默认策略。
- 调用诊断：`GET /api/v1/service-info`（匿名探活）、`GET /api/v1/service-info/whoami`（回显恢复出的用户与调用方 client）。

## 发布本服务的 Client 包

`src/{ProjectName}.Client` 是本服务的强类型调用客户端（**Refit 接口式**，HTTP 实现由源生成器产出）：

- 只依赖 `Leistd.ServiceClient.Refit` / `Leistd.ServiceClient.OAuth` + `Refit`（激活源生成器），不引用服务内部程序集；DTO 在包内自带，与服务端 DTO 独立演进。
- 新增对外接口时在 Refit 接口上补充方法与特性（`[Get]`/`[Post]`/`[Multipart]` 等）及对应 DTO，并保持 `Add{ProjectName}Client` 注册入口不变。数据格式写法（JSON/表单/multipart 文件/二进制下载）见组件随包文档 `docs/service-client.md` 的「数据格式规范」。
- **一律经 `AddRefitServiceClient` 注册**（`Add{ProjectName}Client` 已封装）：错误统一还原为 `RemoteServiceException`，不暴露 Refit 的 `ApiException`。
- 以 NuGet 包形式随服务版本发布（`dotnet pack`），消费方按服务版本升级。

集成测试 `ServiceInvocationTests` 覆盖「取令牌 → 受信恢复用户 → 伪造头阻断」闭环，改动认证或用户上下文相关代码后必须保持其通过。
