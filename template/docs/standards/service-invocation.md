# 服务间调用

本项目使用 Leistd.ServiceClient 的强类型客户端、关联标识、官方 OpenIddict.Client 机器认证及 Token Exchange。身份来自签发方令牌，业务代码面向接口；随包文档 docs/service-client.md 说明框架入口与错误契约。

## 调用其他服务

引用目标服务的 Client 包，注册工作负载身份，再为返回的 IHttpClientBuilder 明确选择认证模式：

```csharp
builder.Services.AddServiceAuthentication();
builder.Services.AddOrderServiceClient(builder.Configuration).AddClientCredentials();
builder.Services.AddBillingServiceClient(builder.Configuration).AddTokenExchange();
```

机器调用只代表客户端，范围绑定在 Leistd:ServiceClients:{Name}:Scope。用户调用从当前 Resource 请求读取已验证的 Bearer 令牌并交换，不能通过设置 ICurrentUser、ICurrentTenant 或请求头制造用户凭据。Identity/Standalone 的 Cookie 和后台用户上下文不提供交换证明；用户委托需要已验证的用户访问令牌。

远端失败以 RemoteServiceException 携带业务码、远端 traceId 和错误字段；协议取令牌失败为 ServiceClientException，默认 API 返回安全的 502。可预期业务失败由本服务明确翻译。

## 被其他服务调用

接收端验证令牌签名、issuer、audience 和有效期，再从同一个主体读取用户与租户。旧的用户/租户委托头不参与认证与租户判定；机器令牌即使附带伪造头，也不能通过要求自然人的默认策略。纯工作负载端点单独声明机器策略。机器调用不会自动转发环境租户，租户须作为路由或请求参数显式传递；机器端点校验调用权限及租户有效性。

Token Exchange 令牌保留用户、租户与身份库的用户名/邮箱/显示名，带 act 说明调用方；不继承来源角色和超管标记。下游使用本地授权主体和权限决定业务操作。非法租户 claim 失败关闭，不回退宿主。诊断入口 GET /api/v1/service-info/whoami 返回当前用户和 client 身份。

## Identity 与资源服务对接

Identity 的 OAuth:ApiResources 登记 orders-api、billing-api 等 API，每项成为同名 scope。Resource 的 Authentication:Audience 使用自己的 API ID，Authentication:Issuer 与 Identity 的 OAuth:Issuer 精确一致，含路径与尾斜杠。

开放应用仅使用 implicit consent，不提供同意类型输入和同意页。登记三类权限组合：

- SPA 为 web/public、authorization code、PKCE；只授予 openid/profile/email/roles 与自己的 API scope，登记完整 /auth/callback 和登出回调源地址，不申请下游 scope。
- 调用方为 service/confidential，client ID 与来源 API 受众一致（例如 orders-api），启用 Token Exchange grant、ept:token、aud:billing-api、scp:billing-api。subject 令牌必须面向 orders-api，但无需带 billing-api scope；目标权限取调用方应用的登记值。只接受单跳访问令牌，不支持 actor_token 和请求覆盖身份。
- 机器调用启用 client credentials 与目标 scope；租户回源授予 tenant-routing.read，迁移身份单独授予 tenant-migration.read。工作负载不混用用户授权流。需要机器回源的交换客户端可同时启用 client credentials。

配置示例（Secret 由 Leistd__ServiceAuth__ClientSecret 注入）：

```json
{
  "Authentication": { "Issuer": "https://login.example.com/", "Audience": "orders-api" },
  "Leistd": {
    "ServiceAuth": { "Authority": "https://login.example.com/", "ClientId": "orders-api" },
    "ServiceClients": {
      "Identity": { "BaseAddress": "https://login.example.com/", "Scope": "tenant-routing.read" },
      "Billing": { "BaseAddress": "https://billing.example.com/", "TokenExchange": { "Audience": "billing-api", "Scope": "billing-api" } }
    }
  }
}
```

Resource 已注册用户访问令牌适配器；Billing 经 AddTokenExchange 注册，租户回源的 Identity 客户端经 AddClientCredentials 注册。工作负载身份全局注册一次，客户端配置名与注册名一致。Identity CORS 允许资源前端的源，前端 authority 与客户端登记地址一致；回调采用普通路径路由，不带 fragment。

交换 JWT 有效期 120 秒且不超过 subject exp；进程内 HybridCache 按官方客户端返回的到期时间提前 10 秒失效，键含完整 subject 摘要，不把 Bearer 写入 Redis。没有真实用户令牌时拒绝委托，不回退为机器身份。401 清除缓存，重试由业务层按幂等性决定。

独立库先由各服务 DbMigrator 预迁移，再登记租户命名连接；只登记 default 时，各服务在租户库内使用自己的 schema。机器令牌和路由均有缓存，撤销权限不会主动清空热缓存；冷实例取被撤销范围时返回 502，日志保留官方 invalid_request 与 ID2051 诊断。

## 发布本服务的 Client 包

src/{ProjectName}.Client 只依赖 Refit 与服务客户端框架，不引用服务内部程序集；DTO 独立演进。新增接口经 Refit 特性声明，并由 AddRefitServiceClient 注册，以统一还原远端异常。Client 注册返回 IHttpClientBuilder，由消费宿主选择机器认证或用户交换，不自行猜测身份模式。以 NuGet 发布并按服务版本升级。

认证变更需运行本项目的 OIDC 与服务调用集成测试，并在部署环境验证跨服务 Token Exchange 正反例、租户成员关系与用户资料一致性。
