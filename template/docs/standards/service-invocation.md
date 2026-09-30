# 服务间调用

本项目基于 `Leistd.ServiceClient.*` 组件与其他服务互调：调用日志、关联标识透传（链路由官方 `traceparent` 传播）、用户上下文传递与 OAuth2 client credentials 认证由标准管道承担，业务代码只面向强类型客户端。组件完整用法见随包文档（任一 `Leistd.ServiceClient.*` 包内 `docs/service-client.md`）。

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

当前用户的 Id/用户名会自动经 `X-User-Id` / `X-Username` 头传给被调方；后台任务先用 `ICurrentPrincipalAccessor.Change(...)` 设定主体再调用。

## 被其他服务调用

- `Program.cs` 已注册 `AddServiceUserContext` + `UseServiceUserContext`（OpenIddict 场景）。采信 `X-User-*` 头并恢复用户主体需**同时**满足：调用方以 client credentials 令牌通过认证、其 `sub` 为机器主体契约形态（`client:<client_id>`）、且令牌持有委托 scope `svc.delegate`；任一不满足都会剥离这些头。**网关/Ingress 必须同步剥离外部来源的 `X-User-*` 头**。
- 调用方凭据在 Identity 服务的「开放应用」中注册（confidential 客户端 + `client_credentials` 授权），凭据仅创建时返回一次。
- **代表用户调用需额外授予 `svc.delegate`**（开放应用的权限项 `scp:svc.delegate`）：拿到机器令牌只代表调用方是已认证的工作负载，不等于有权代表用户——不授予该 scope 的客户端即使知道用户 Id 也无法冒充。调用方侧在 `Leistd:ServiceClients:<服务名>:Scope` 同时写目标 API 的 scope 与该 scope（空格分隔，如 `order-api svc.delegate`）：委托 scope 只表示"可以代表用户"，令牌能调用哪个 API 由目标 API 的 scope 决定。
- `svc.delegate` 客户端的 Secret 一旦泄露，攻击者就能以任意用户和租户的身份调用所有接受该令牌受众的服务。
  该 scope 只授予完全受信的工作负载，并按目标 API 分别授权；委托头没有权威成员关系证明。
- 默认授权策略要求可用的自然人用户：服务间调用需携带受信 `X-User-Id`；确需面向纯工作负载的端点，单独声明策略，不放宽默认策略。
- 调用诊断：`GET /api/v1/service-info`（匿名探活）、`GET /api/v1/service-info/whoami`（回显恢复出的用户与调用方 client）。

## Identity 与资源服务对接

Identity 的 `OAuth:ApiResources` 登记每个下游 API 的受众（例如 `orders-api`、`billing-api`），
各自同时成为同名 scope。资源服务的 `Authentication:Audience` 使用对应值；
`Authentication:Issuer` 使用 Identity 的 `OAuth:Issuer`，包括路径与尾斜杠。
令牌受众由授予的 scope 推出，申请 `orders-api` 的令牌不能调用 `billing-api`。

在 Identity 的「开放应用」创建两类客户端：

- SPA：`web`、`public`、authorization code、强制 PKCE；授予 `openid profile email roles` 与本 API 的 scope，
  登记完整回调 `/auth/callback` 和登出回调源地址。前端 `oidc.authority` 与签发方一致，
  `oidc.clientId` 与登记的 Client ID 一致。普通路径路由必须能回退到 SPA，回调地址不带 fragment。
- 服务调用方：`service`、`confidential`、client credentials；只授予要访问的 API scope。
  代表用户时追加 `svc.delegate`，资源服务回源读取租户连接时追加 `tenant-routing.read`。
  机器 scope 不得与用户授权流混用，`tenant-migration.read` 只给一次性迁移身份。

Identity 的 `Cors:AllowedOrigins` 需允许资源服务前端的源，以供发现、JWKS、token 和 userinfo 请求。
本机 Angular 前端开发服务器已允许 localhost 来源，开发代理转发 API 请求；生产 API 仍只认
`Cors:AllowedOrigins`（默认为空）。浏览器登录使用 Identity 前端地址，
因此签发方也应使用该地址；API 内部调用地址可以另配。

调用方配置示例（凭据由环境变量或密钥管理提供）：

```json
{
  "Authentication": {
    "Issuer": "https://login.example.com/",
    "Audience": "orders-api"
  },
  "Leistd": {
    "ServiceAuth": {
      "Authority": "https://login.example.com/",
      "ClientId": "orders-machine"
    },
    "ServiceClients": {
      "Identity": {
        "BaseAddress": "https://login.example.com/",
        "Scope": "tenant-routing.read"
      },
      "Billing": {
        "BaseAddress": "https://billing.example.com/",
        "Scope": "billing-api svc.delegate"
      }
    }
  }
}
```

`Leistd__ServiceAuth__ClientSecret` 注入创建机密客户端时只返回一次的 Secret。
`Billing` 的强类型客户端需经 `AddRefitServiceClient(...).AddClientCredentials(...)` 注册；
配置节名称与注册名一致。租户回源由已注册的 `Identity` 客户端承担，默认路由前缀
`/api/v1/tenant-connections`，若改名，两边同步配置。

独立库先用各服务的 DbMigrator 预迁移，再在 Identity 创建租户时登记命名连接；
只登记 `default` 时，各服务在同一租户库中使用自己的 schema。
回源结果与机器令牌都有缓存，撤销 scope 不会清空热实例缓存；冷实例请求被撤销的 scope 时取令牌失败，
未处理的依赖拒绝返回 502，日志保留远端 400、`invalid_request` 与 scope 权限诊断（OpenIddict `ID2051`）。

委托头证明的是调用工作负载有权声明用户上下文。资源服务不向 Identity 校验
`X-User-Id` 与 `X-Tenant-Id` 是否为真实成员关系；受信调用方必须从当前用户和租户上下文生成它们，
不可直接转发外部输入。需要身份服务逐次确认委托关系时，应另行设计 Token Exchange 或成员校验契约。
用户标识和租户 claim 必须属于同一个身份，混合身份的非法声明会被拒绝。

## 发布本服务的 Client 包

`src/{ProjectName}.Client` 是本服务的强类型调用客户端（**Refit 接口式**，HTTP 实现由源生成器产出）：

- 只依赖 `Leistd.ServiceClient.Refit` / `Leistd.ServiceClient.OAuth` + `Refit`（激活源生成器），不引用服务内部程序集；DTO 在包内自带，与服务端 DTO 独立演进。
- 新增对外接口时在 Refit 接口上补充方法与特性（`[Get]`/`[Post]`/`[Multipart]` 等）及对应 DTO，并保持 `Add{ProjectName}Client` 注册入口不变。数据格式写法（JSON/表单/multipart 文件/二进制下载）见组件随包文档 `docs/service-client.md` 的「数据格式规范」。
- **一律经 `AddRefitServiceClient` 注册**（`Add{ProjectName}Client` 已封装）：错误统一还原为 `RemoteServiceException`，不暴露 Refit 的 `ApiException`。
- 以 NuGet 包形式随服务版本发布（`dotnet pack`），消费方按服务版本升级。

集成测试 `ServiceInvocationTests` 覆盖「取令牌 → 受信恢复用户 → 伪造头阻断」闭环，改动认证或用户上下文相关代码后必须保持其通过。
