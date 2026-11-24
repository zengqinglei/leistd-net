# 统一的"需要认证"异常与令牌交换 invalid_grant

状态：已完成 · 风险档：高 · 关联：#313、#316

## 1. 需求

### 问题与背景

下游业务项目在 dev 环境发现：员工的 Identity SSO 会话结束后（别处退出、换账号登录、空闲或绝对时长到期），BFF 代表用户做 Token Exchange，被 Identity 以 `invalid_grant` 拒绝。框架把它当作上游故障答 502，前端提示网络错误，也不回登录页。模板的续期路径早已把 `invalid_grant` 当作会话结束（答 401），交换路径口径不一致。

另查实：
- 框架没有统一的"未认证"异常。文档示例抛 BCL 的 `UnauthorizedAccessException`，但全局异常处理没有映射它，示例代码实际得到 500。
- 模板工作区交换只捕获 OpenIddict 的 `ProtocolException`。框架的 `ResponseFailureGuard` 挂在整个官方客户端上，实际抛出的是 `RemoteServiceException`，所以这处捕获一直没生效：工作区不存在时答的是 502，而不是 403。
- 模板 Identity 的交换把受众、作用域、客户端类型这些配置问题也答成 `invalid_grant`。

### 目标与非目标

- 框架提供统一的"需要（重新）认证"信号，默认 401，前端据错误码重新登录；令牌交换被以 `invalid_grant` 拒绝时，改抛这个信号。
- 模板的工作区交换与框架口径一致：`invalid_grant` 答 401，`invalid_target` 答 403。
- 模板 Identity 的交换错误码按 RFC 6749 / 8693 分开：`invalid_grant` 只表示本人凭据不再被接受。
- **非目标：**
  - 框架不结束宿主的本地会话：会话归宿主管。模板 BFF 的登录入口会无条件挑战签发方，不会形成循环，所以不另加捕获。
  - 不改模板"会话在续期时判定、交换时不判定"的既有设计。

### 功能要求与验收

- **R1：** Given 宿主注册了全局异常处理，When 业务抛出 `AuthenticationRequiredException`，Then 答 401，错误码为 `Authentication:Required`，未启用本地化时 detail 为英文安全文案；中英默认译文随 `ExceptionHandling.Core` 分发并由 `AddGlobalExceptionHandler` 登记；宿主用 `MapCode` 覆盖时，无论在注册前还是注册后调用都生效。（`GlobalExceptionPipelineTests`）
- **R2：** Given 身份服务以 `invalid_grant` 拒绝 Token Exchange，When 业务调用经 `AddTokenExchange` 发出，Then 抛 `AuthenticationRequiredException`，原 `RemoteServiceException` 保留为内层异常；其他 OAuth 错误仍抛 `RemoteServiceException`。（`OfficialClientTests`）
- **R3：** Given 令牌端点返回 `invalid_grant`，Then 令牌响应守卫只记 Information；其余 OAuth 错误、以及发现文档或 JWKS 端点里的同名 error 照常记 Error。（`ResponseFailureTests`）
- **R4：** Given Resource 浏览器会话进入工作区，When 工作区交换被以 `invalid_grant` 拒绝，Then 答 401 并带 `Authentication:Required`；被以 `invalid_target` 拒绝时答 403。（`ResourceBrowserSessionTests`）
- **R5：** Given 模板 Identity 收到 Token Exchange（工作负载以 client_secret 认证），Then 配置类拒绝不答 `invalid_grant`：
  - OpenIddict 原生验证：未登记受众或资源答 `invalid_target`，未登记 scope 答 `invalid_scope`，参数组合或令牌类型不受支持、或 scope 未获授答 `invalid_request`，调用方没有交换权限答 `unauthorized_client`；
  - 模板约束：调用方不是机密客户端、或不拥有主体令牌的资源，答 `unauthorized_client`；受众、作用域或工作区不对，答 `invalid_target`；
  - 只有本人凭据不再被接受时答 `invalid_grant`：主体令牌过期或撤销、已带委托方、是机器主体，或用户不在所声明的租户。

  （`TenantOidcFlowTests` 的拒绝矩阵逐项断言错误码；`OpenApplicationSessionBindingTests` 断言停用账号的工作区交换答 `invalid_grant`）

### 已确认的决策

用户 2026-11-24 确认：
- 采用框架级统一异常，不按 OAuth 错误码改映射；
- 命名为 `AuthenticationRequiredException`，不用 `AuthenticationException`（与 BCL 的 TLS 认证异常重名），也不复用 `UnauthorizedAccessException`（表示操作系统拒绝访问）；
- 一次开发完成并测试闭环，然后提交、推送、合并。

## 2. 方案

- **`Leistd.ExceptionHandling.AuthenticationRequiredException`**：在 `ExceptionHandling.Core`，继承 `BusinessException`，固定码 `Authentication:Required`。
  - 默认 401，由 `AddGlobalExceptionHandler` 用 `MapDefaultCode` 登记。
  - 中英默认文案嵌入发码的 `ExceptionHandling.Core`，由 `AddGlobalExceptionHandler` 经 `AddJsonLocalizationResources` 登记，宿主资源可覆盖。
- **401 与 403 不按"是否已登录"自动判断**：令牌交换被拒时本地 Cookie 仍有效，用户算已登录，自动判断会错答 403。所以两个含义分开：这个异常固定答 401；无权限走带错误码的 `BusinessException` 映射为 403。文档里抛 `UnauthorizedAccessException` 的三处示例（`IPermissionChecker`、数据权限、`ICurrentUser`）一并改正。
- **`TokenExchangeDelegatingHandler`**：在取令牌处捕获 `RemoteServiceException { IsOAuthError: true, ErrorCode: invalid_grant }`，改抛新异常。
- **`ResponseFailureGuard`**：令牌响应里的 `invalid_grant` 降为 Information，判据与 `IsOAuthError` 相同；发现文档与 JWKS 端点不受影响。
- **模板**：
  - 工作区票据提供者改为按框架异常的 OAuth 错误码捕获。
  - `ConnectController` 拆分拒绝条件。
  - 资源归属检查改答 `unauthorized_client`。官方同类检查答 `invalid_grant`，这里偏离官方的理由：调用方无权交换是部署配置问题，按 `invalid_grant` 处理会让用户在登录页与应用之间无限跳转。
- **401 不带 `WWW-Authenticate`：** 异常发生在认证之后的业务代码里，处理器不知道宿主的认证方式；需要标准质询的 Bearer API 由宿主在 `ProblemDetailsOptions.CustomizeProblemDetails` 里，对本异常且最终 401 的端点写入适用的 `WWW-Authenticate`，写进组件文档。
- **与 RFC 8693 的关系：** RFC 8693 §2.2.2 要求对无效或策略不接受的主体令牌答 `invalid_request`；OpenIddict 沿用 RFC 6749 把过期、撤销等答成 `invalid_grant`，框架以 OpenIddict 的兼容口径为约定，并在文档写明其他签发方的代码可能不同。
- **兼容：** Token Exchange 的 `invalid_grant` 从 `RemoteServiceException` 改为 `AuthenticationRequiredException`。按类型捕获的调用方需要调整，作为破坏性变更发布。

## 3. 计划

| 任务 | 产物 | 验证 |
| --- | --- | --- |
| 统一异常与默认映射、文案 | `AuthenticationRequiredException`、`AddGlobalExceptionHandler`、本地化资源 | 异常处理与本地化单测 |
| 交换入口与日志级别 | `TokenExchangeDelegatingHandler`、`ResponseFailureGuard` | 服务调用单测（官方客户端 + TestServer） |
| 随包文档与示例 | exception-handling、service-client、security、authorization-data-scope、`IPermissionChecker` | 文档 API 漂移闸门 |
| 模板 Identity 错误码与工作区交换 | `ConnectController`、`ResourceOwnerAuthorizedPartyHandler`、`ResourceWorkspaceTicketProvider`、模板规范 | identity-workspace、resource-workspace 集成测试与快速检查 |

**不做：**
- 模板 BFF 的统一会话清理中间件：登录入口无条件挑战签发方，没有循环；异常处理中间件会清空响应头，在那里删 Cookie 也不可靠。
- 工作区交换中"用户已停用"的单独判定：停用会撤销令牌，官方验证已先答 `invalid_grant`，多加一次查询测不出差别。

## 4. 结果

- **交付：** 本规格随同一个 PR 提交，按依赖顺序分三次提交：`feat(exception-handling)`、`fix(service-client)!`、`fix(template)`。
- **覆盖边界：** 拒绝矩阵在 client_secret 接线下验证；"调用方不是机密客户端"与"无交换权限""不拥有资源"同答 `unauthorized_client`，前者未单独构造公开客户端用例。
- **审查：** Codex 第一轮提出 4 条，已处理：默认译文移到发码的 `ExceptionHandling.Core`；日志降级限定在令牌响应；R5 与文档区分原生验证和模板约束，拒绝矩阵逐项断言错误码；`WWW-Authenticate` 不加，理由见 §2。
- **偏差：** 原计划在工作区交换里区分"用户已停用"与"没有工作区"。变异验证发现这段分支走不到（停用会撤销令牌），已删除，测试保留对结果的断言。
- **顺带修复：** 模板工作区交换原有的 `ProtocolException` 捕获一直没生效，`invalid_target` 会答 502，现在答 403。
- **验收：**
  - 框架：异常处理、服务调用、本地化单测通过；`check-all` 52 道闸门通过。
  - 模板：identity-workspace 与 resource-workspace 的快速检查和相关集成测试通过。
  - 全场景矩阵由 CI 承担。
- **已上收的常驻文档：** `framework/docs/components/exception-handling.md`、`service-client.md`；`template/docs/standards/auth.md`、`service-invocation.md`。
- **后续缺陷（下游反馈，已修复）：** 原用例只用普通 `AddHttpClient` 挂令牌处理器，没走 `AddServiceClient` / `AddRefitServiceClient` 的标准管道。标准管道最外层的 `TransportFailureHandler` 会把调用方主动取消以外、所有非 `ServiceClientException` 的异常按故障分类器包成 `ServiceClientException`（连接失败归 `Unavailable`、超时归 `Timeout`、无效响应归 `InvalidResponse`，其余归 `Unknown`）。`AuthenticationRequiredException` 落在 `Unknown`，因此答 500。修法：该处理器对 `BusinessException` 一并原样抛出，因为内层处理器抛出的业务异常已经是本服务对调用方的语义，不是传输故障。
  - 只放行 `AuthenticationRequiredException` 一种也能修好本例，但宿主自加的处理器抛其他业务异常时会再犯，所以按基类放行。
  - 回归用例：
    - `TransportFailureHandlerTests.A_business_failure_from_an_inner_handler_passes_through_unchanged` 与 `RefitTransportFailureTests.Business_failures_from_the_pipeline_pass_through_unchanged`：都用普通 `BusinessException`，锁定按基类放行；Refit 一条覆盖 `Task<T>` 与 `Task<ApiResponse<T>>`。
    - `OfficialClientTests` 的 invalid_grant 与 invalid_target 两条用例改为同时覆盖普通客户端和标准管道两种形态。
    - 修复前，标准管道形态的 invalid_grant 用例和业务异常透传用例失败。
  - 模板 `IdentityAccountPreferences` 的写入路径（Token Exchange）本来就让重新认证异常上抛，读取路径（Client Credentials）仍捕获依赖故障，代码不改，升级框架后生效。
