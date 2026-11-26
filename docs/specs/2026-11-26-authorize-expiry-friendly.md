# 授权请求缓存过期与依赖方回调失败的友好页

状态：进行中 · 风险档：高（认证协议路径，只改模板） · 关联：待定

## 1. 需求

### 问题与背景

一个下游业务项目的演示站，页面放置一天后会自动跳到 Identity 登录页。用户登录完回到 `/connect/authorize`，页面直接显示 OpenIddict 的原始错误 `invalid_token / ID2019`。

对照模板 develop `ac2b2585` 核实，问题同样存在：

- **Identity（`OpenIddictServer` 形态，`OpenIddictServerExtensions.cs`）：**
  - 开启了 `EnableAuthorizationRequestCaching()` 和 `EnableEndSessionRequestCaching()`，没有覆盖 request token 的寿命，沿用 OpenIddict 默认的 1 小时；
  - `UseAspNetCore()` 没有开 `EnableErrorPassthrough()`。request token 过期后，原 `redirect_uri` 已不可知，OpenIddict 在控制器之前直接输出协议原文。
- **依赖方（`RemoteTokenAuth` 形态，`RemoteTokenAuthenticationExtensions.cs`）：** `OnRemoteFailure` 只 `HandleResponse()` 并返回空的 400。correlation 超时（默认 15 分钟）、state 无效、用户取消授权，最终都是浏览器的空白错误页。

### 取舍依据

- `docs/architecture/design-principles.md` §5。
- **优先官方机制：** OpenIddict 的 `EnableErrorPassthrough()` 加 `GetOpenIddictServerResponse()`；ASP.NET Core OIDC 处理器的 `OnRemoteFailure` 和 `OnAccessDenied`。
- **安全：** 「重新登录」的目标只取已登记的值，从不取请求参数，避免开放重定向；不复用过期请求里的任何状态，MFA 和 step-up 照常执行；不自动重新挑战，避免跳转循环。
- **UI：** 沿用模板现有错误页的 Spartan Empty 写法（`template/docs/standards/frontend-ui.md`），按原型图实现。
  - 原型图：<https://claude.ai/artifact/7yTkUV7gXUP9Ayv5kFz1oP>
- **文案：** 按模板已启用的语言（`en`、`zh-CN`）补齐词条；单语言形态用英文。

### 目标与非目标

- **目标：**
  - 用户不再看到协议原文或空白的 400，每种失败都有一个说明清楚、可以操作的页面；
  - 能合法回调依赖方的错误、机器端点的错误，行为都不变。
- **非目标：**
  - 不延长请求缓存寿命；
  - 不让前端推测 request token 的有效期；
  - 不恢复原深链；
  - 页面上不显示错误码、traceId 或协议原文；
  - 不改框架。

### 功能要求与验收

#### R1 Identity 交互端点的本地协议错误（`OpenIddictServer`）

- 启用 `UseAspNetCore().EnableErrorPassthrough()`。
- 授权端点和退出端点的控制器，用 `HttpContext.GetOpenIddictServerResponse()` 读到错误后，按种类导向 Identity 前端的友好页。
- 能合法回调依赖方的错误（`redirect_uri` 有效），照常由 OpenIddict 带着 OAuth 错误回调，不变。
- 实施时要证明：`EnableErrorPassthrough` 只影响授权端点和退出端点。其他端点各自保持既有的协议约定：
  - `/connect/token`：状态码和 JSON 错误体不变；
  - `/connect/userinfo`：质询的状态码、`WWW-Authenticate` 标头，以及空响应体都不变；
  - 能合法回调依赖方的授权错误：回调时带的错误码和 `state` 不变。

| 种类 | 触发条件 | 原型图画板 |
| --- | --- | --- |
| `login-expired` | 授权端点：ID2019（request token 过期或无效），以及其他无法安全回调的浏览器协议错误 | `IdentityExpired`；无登记入口时为 `IdentityUnknownClient` |
| `logout-expired` | 退出端点：退出请求缓存过期或无效；以及退出确认页的确认凭据失效（`isValid: false`） | 通常为无应用版本（见 R2） |

#### R2 「重新登录」与「返回」的目标

- 用请求里的 `client_id` 查已登记的开放应用。应用登记了 `HomeUrl`（`OpenApplicationSettings.ReadHomeUrl`，系统切换器用的同一个值）时：
  - 主按钮文字带应用的本地化显示名，例如「重新登录 {应用名}」「返回 {应用名}」；
  - 主按钮指向这个 `HomeUrl`。
- 查不到应用、应用没有 `HomeUrl`、或 `client_id` 缺失时：
  - 只显示「返回 Identity 首页」（指向 `/`）；
  - 不显示应用名，不产生任何外部跳转。
- **退出流程的应用来源。** 真实的退出确认流程里，`/auth/logout-confirm` 只收到 `request_uri` 和受保护的 `confirmation`，确认表单发往 `/connect/logout` 时也不带 `client_id`。应用标识藏在确认凭据里，而确认凭据默认 10 分钟就会失效。所以：
  - **确认页重载时凭据已失效：** 显示 `logout-expired` 的无应用版本，代替现在的无效确认页；
  - **一直开着确认页，等 request token 过期后再提交：** 协议端点导向 `logout-expired`，同样是无应用版本；
  - **有应用版本**（「返回 {应用名}」）只在请求本身带 `client_id` 时出现。不为此改动确认流程，也不在测试里人为补 `client_id`。
- **目标地址由服务端从登记数据算出。** 前端只能拿到算好的结果（应用名和地址），不能接受请求参数里给的任何 URL。具体做法实施方可以选：
  - 前端友好页带 `client_id` 去调用一个匿名接口，接口只返回已登记应用的显示名和 `HomeUrl`；
  - 或者服务端导向前端时，只带一个可以再次查证的标识。
  
  无论哪种，都不能把 URL 本身放进查询参数。

#### R3 依赖方回调失败（`RemoteTokenAuth`）

- **生效条件：** 只在 `ResourceBrowserSession` 形态生效。回调钩子和 `/api/v1/auth/login` 都属于这一形态；`RemoteTokenAuth` 里的纯 Bearer Resource 不生成这部分服务端、前端和测试代码，生成验证要覆盖这种裁剪。
- 保留官方的 `OnAccessDenied`。`OnRemoteFailure` 和 `OnAccessDenied` 都调用 `HandleResponse()`，然后重定向到依赖方前端的 `/auth/sign-in-failed?reason=…`。路径命名按模板路由规范，可以调整。原因分三类：

| reason | 归类 | 原型图画板 | 按钮 |
| --- | --- | --- | --- |
| `expired` | correlation 失败、state 缺失或无法解保护 | `RpExpired` | 「重新登录」 |
| `denied` | `access_denied` 等授权被拒或用户取消 | `RpDenied` | 「重新登录」+「返回首页」 |
| `error` | 其他 | `RpOther` | 「重新登录」 |

- **「重新登录」：** 指向依赖方自己的登录入口（模板现有的 `/api/v1/auth/login`），由用户点击后重新发起挑战。**不自动重新挑战。**
- **「返回首页」：** 依赖方的公开首页 `/`。
- **归类方式：**
  - 锁定的 ASP.NET Core OIDC 10.0.15，对 state 缺失、state 解保护失败、correlation 失败，都调用 `HandleRequestResult.Fail(string, …)`。这些情况包装成同一种 `AuthenticationFailureException`，没有稳定的错误码。
  - 因此允许**只在服务端**按上游这几条特定的失败消息识别为 `expired`。消息常量写在一处，并用**真实的 OIDC 处理器**测试锁住：上游消息一旦变化，测试就会变红。
  - 识别不出来的一律归为 `error`。
  - 查询串只带固定的 reason 值，异常消息不进查询参数，也不显示在页面上。
  - 不重写 state 或 correlation 的校验。

#### R4 UI 与文案

- **结构：** 按原型图实现，结构与 `workspace-denied` 一致，用 Spartan Empty：
  - `hlmEmpty` 外层 `main` 为 `flex min-h-svh items-center justify-center bg-background`；
  - `hlmEmptyMedia variant="icon"` 放图标；
  - `hlmEmptyTitle`、`hlmEmptyDescription` 放标题和说明；
  - `hlmEmptyContent` 放按钮，主按钮用 `hlmBtn` 默认样式，次按钮用 `variant="outline"`。
- **不要画进页面：** 原型图里的地址栏和「设计说明」是画板上的注释，不进页面。
- **文案：** 使用下表，以 `zh-CN` 为准，`en` 为对应译文；单语言形态用英文。

| 页面 | 标题 | 说明 | 按钮 |
| --- | --- | --- | --- |
| login-expired（有应用） | 登录请求已过期 / This sign-in request has expired | 页面停留的时间较长，这次登录请求已经失效。请回到 {应用名} 重新登录，系统会为你发起一次新的登录。 / You stayed on the page for a while and this sign-in request is no longer valid. Go back to {app} and sign in again to start a new sign-in. | 重新登录 {应用名} / Sign in to {app} again；返回 Identity 首页 / Back to Identity home |
| login-expired（无应用） | 同上 | 这次登录请求已经失效。请回到你原来使用的应用，重新发起登录。 / This sign-in request is no longer valid. Go back to the app you were using and sign in again. | 返回 Identity 首页 / Back to Identity home |
| logout-expired | 退出请求已失效 / This sign-out request has expired | 这次退出请求已经过期，你的会话可能已经结束。如果仍处于登录状态，请回到 {应用名} 重新退出。 / This sign-out request has expired and you may already be signed out. If you are still signed in, go back to {app} and sign out again. | 返回 {应用名} / Back to {app}；返回 Identity 首页 / Back to Identity home |
| RP expired | 登录已超时 / Sign-in timed out | 在登录页停留的时间过长，这次登录没有完成。请重新登录。 / You stayed on the sign-in page too long and the sign-in did not finish. Please sign in again. | 重新登录 / Sign in again |
| RP denied | 登录没有完成 / Sign-in was not completed | 登录已被取消，或者 Identity 拒绝了这次登录。如果需要继续使用，请重新登录。 / The sign-in was cancelled or refused. Sign in again to continue. | 重新登录 / Sign in again；返回首页 / Back to home |
| RP error | 登录时出现问题 / Something went wrong while signing in | 这次登录没有完成，请重新登录。如果多次失败，请联系管理员。 / The sign-in did not finish. Please sign in again. If it keeps failing, contact your administrator. | 重新登录 / Sign in again |

- 无应用时，`logout-expired` 的说明里用「回到原来的应用」，代替「回到 {应用名}」；按钮只有「返回 Identity 首页」。

#### R5 验收

1. 时钟越过请求缓存寿命后（用 `FakeTimeProvider` 或配置缩短寿命，不靠真实等待），授权端点和退出端点分别显示对应的友好页，不再出现协议原文。先写修复前失败的用例。
2. 点「重新登录 {应用名}」后，经登记的 `HomeUrl` 发起一次新的授权，并完整走完登录，包括 MFA。真实浏览器验证一次。
3. `client_id` 未登记、应用缺少 `HomeUrl`、`client_id` 缺失：只回 Identity 首页，页面里不出现任何外部链接。
4. 能合法回调的授权错误，照常带 OAuth 错误回调；`/connect/token` 的错误仍是 JSON。
5. 依赖方：以下三种情况经真实的 OIDC 处理器，分别到达三种友好页：
   - correlation 过期，或 correlation Cookie 缺失、被篡改；
   - `access_denied`；
   - 其他异常。
   
   退出确认页重载时凭据已失效、确认页开着直到 request token 过期后再提交，这两条入口也分别验收。「重新登录」能完成新的登录；Identity 持续拒绝时停在友好页，不出现自动跳转循环。
6. UI 与原型图一致：
   - 实施后，在真实浏览器里对 6 个页面各截一张图，中英各一套，和原型图逐一对照，写进 §4；
   - 前端 spec 断言标题、说明、按钮的文字和链接目标，断言用户能看到的结果。
7. 本地化与单语言两种形态都能构建，测试通过；`check-i18n` 通过；纯 Bearer 的 Resource 形态不生成依赖方友好页相关的代码。

## 2. 方案

- **改动范围：** 全在模板：
  - Identity：服务端配置、授权和退出控制器、前端友好页及路由；
  - 依赖方：`OnRemoteFailure`、`OnAccessDenied`，以及前端友好页和路由。
- **复用：** 复用现有的开放应用登记、`HomeUrl` 读取方法和本地化显示名，不新增登记字段。
- **回滚：** 按提交撤回即可，不涉及数据和迁移。

## 3. 计划

| 步骤 | 内容 | 验证 |
| --- | --- | --- |
| 1 | 修复前失败的用例：Identity 过期显示原文、依赖方返回空 400 | 受影响的集成测试 |
| 2 | R1、R2 Identity 服务端加友好页 | 集成测试全量；机器端点回归 |
| 3 | R3 依赖方 | 集成测试全量 |
| 4 | R4 前端页面和词条 | 前端全量；`check-i18n`；两种形态 |
| 5 | 端到端闭环 | 真实浏览器走 R5 第 2、5、6 条；完整 `verify.ps1`，Identity 和 Resource 各一种场景；`scripts/check-all.ps1`；`template-package/test-template-package.ps1` |

## 4. 验证

（实施后填写。）
