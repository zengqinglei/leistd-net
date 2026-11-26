# 授权请求缓存过期与依赖方回调失败的友好页

状态：进行中 · 风险档：高（认证协议路径，只改模板；2026-11-26 按用户确认改为自动重新授权为主、友好页兜底，见 R0、R6） · 关联：待定

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
- **安全：** 「重新登录」的目标只取已登记的值，从不取请求参数，避免开放重定向；不复用过期请求里的任何状态，MFA 和 step-up 照常执行；自动重来受依赖方唯一的一份预算约束，只做一次，避免跳转循环。
- **UI：** 沿用模板现有错误页的 Spartan Empty 写法（`template/docs/standards/frontend-ui.md`），按原型图实现。
  - 原型图：<https://claude.ai/artifact/7yTkUV7gXUP9Ayv5kFz1oP>
- **文案：** 按模板已启用的语言（`en`、`zh-CN`）补齐词条；单语言形态用英文。

### 目标与非目标

- **目标：**
  - 可恢复的过期自动恢复，并回到原深链（R6）；不可恢复的失败，都有一个说明清楚、可以操作的页面；用户不再看到协议原文或空白的 400；
  - 能合法回调依赖方的错误、机器端点的错误，行为都不变。
- **非目标：**
  - 不延长请求缓存寿命；
  - 不让前端推测 request token 的有效期；
  - 页面上不显示错误码、traceId 或协议原文；
  - 不改框架。

### 功能要求与验收

#### R0 方向调整（2026-11-26，用户确认）

用户确认的体验：**登录成功后直接回到依赖方，中间不插提示页，并回到用户最初打开的页面（深链）。**

OAuth 的成功回调必须带原请求的授权码和 `state`（RFC 6749 §4.1.2）。旧事务过期后，没法直接回到原 `redirect_uri`，只能由依赖方重新发起一次有效授权。这时用户已经有 Identity 会话，这一轮不需要再输入凭据。因此：

- **自动重新授权是主路径**，见 R6；
- **R1～R4 的友好页降为兜底**，只在自动重来不可用或失败时显示：
  - 应用未登记，或没有登记登录发起地址；
  - 自动重来一次之后仍然失败；
  - 被拒绝（`access_denied`）；
  - 退出请求过期。

#### R6 自动恢复与深链（主路径）

借鉴 OIDC Core 1.0 §4（第三方发起登录）的做法，但不声称实现完整的 `initiate_login_uri` 标准：完整标准要求同时接受 GET 和 POST，并理解 `login_hint` 等参数，而模板现有的登录入口只有 GET。这次只做模板内第一方的「登录恢复入口」。等确实需要和第三方互操作时，再交付完整的标准入口。

**登记**

- 开放应用增加一个可选设置：登录恢复地址。用应用描述符的扩展 `Properties` 保存，存法和校验与 `HomeUrl` 相同：绝对 https 地址；开发环境只允许项目约定的 loopback 使用 http。
- 只有登记了这个地址的应用才启用自动恢复。
- 模板的第一方系统注册时，把它设为依赖方的恢复入口（见下文）。
- 开放应用的管理接口和表单增加这个字段，放在 `HomeUrl` 旁边。

**Identity 侧**

- 授权端点在透传中遇到 ID2019，并且请求里能取得 `client_id`、该应用已登记且启用了恢复时：直接重定向到它的恢复地址，带上 `iss=<issuer>`，不显示页面。
- 不满足条件时，显示 R1 的 `login-expired` 友好页。其他无法回调的错误、合法回调，以及 `prompt=none` 的非交互语义，都按原样处理。
- ID2019 无法区分「过期」和「令牌记录已失效」。两种情况都表示原请求不可继续用，统一交给依赖方判断：依赖方没有续接记录，就落到兜底页。
- Identity 不自己做重试标记，重试预算只在依赖方。

**依赖方侧（`ResourceBrowserSession`）**

依赖方持有续接记录和唯一的一份重试预算。

- **续接记录。** `/api/v1/auth/login?returnUrl=…` 发起挑战时，写入一个受 Data Protection 保护的一方 Cookie，属性为 HttpOnly、SameSite=Lax、Path 限定在认证路径。内容包括：
  - 已验证的站内路径，含查询串；片段由前端在 `returnUrl` 里编码带上；
  - 首次挑战的时间；
  - 是否已经自动重试。
  
  可恢复的时窗从首次挑战起算，默认 1 天。成功登录后清除。
- **两条恢复入口共用这份预算：**
  1. **恢复入口**（GET，复用现有的 challenge 逻辑）：
     - `iss` 缺失或不等于预配置的签发方：明确失败，显示 R3 的 `error` 友好页，不降级成普通 challenge；
     - 续接记录存在、在时窗内、尚未重试：标记为已重试，用记录里的路径重新发起官方 OIDC challenge；
     - 否则显示 R3 的 `expired` 友好页。
  2. **回调失败**（`OnRemoteFailure`），且失败事件带有已解保护的 `AuthenticationProperties` 时（也就是 state 已解开、correlation 失败的情况）：
     - 用其中的 `RedirectUri` 作为原深链。这条路径深链跟着交易走，不会互相覆盖；
     - 预算仍以续接记录为准：尚未重试时，标记为已重试，自动重新挑战一次；否则显示友好页；
     - state 缺失或无法解保护时没有原属性，直接显示友好页。
- **拒绝优先。** 回调里带 `error=access_denied` 时不自动重试，包括它和 correlation 失败同时发生的情况（官方管线先检查 correlation），停在 R3 的 `denied` 兜底页。
- **已知限制。** 同一浏览器里，同一应用同时有两次待完成的登录时，「Identity 交接」这条入口只能用最近一次的续接记录，后一次覆盖前一次。这种情况很少见，按设计原则 §5 不为它增加按交易关联的机制。

**认证要求**

- 新挑战由依赖方生成新的 `state`、`nonce` 和 PKCE，经原登记的回调建立会话，再转到续接记录里的路径。
- Identity 会话满足新请求的要求时会被复用，此时用户不需要再次输入凭据。`prompt=login`、`max_age`、MFA 和 step-up 照常由现有流程执行。
- 恢复时用依赖方当前配置的认证参数，与它平常发起登录时一致。

**验收**

1. **Identity 授权缓存先失效。** 依赖方会话过期后，自动跳到 Identity 登录页。等 request token 过期后（用假时钟或缩短寿命）再登录，含 MFA。登录后不经过任何中间页，直接回到最初的路径，含查询串和片段，例如 `/w/{key}/platform/users?tab=roles#top`。用真实浏览器验证。
2. **依赖方 correlation 先失效。** 在 Identity 登录页停留超过 correlation 寿命后再登录：不经过中间页，直接回到原路径。
3. **一次恢复里两种失效先后出现。** 同一次恢复先后遇到两种失效，只自动重试一次，第二次停在友好页，不出现循环。
4. **两次待完成的登录。** 走 correlation 失败那条路径时，两次各自回到自己的路径。
5. **兜底。** 以下情况都要验证会停在兜底页：
   - 缺少续接记录；
   - `iss` 缺失或不匹配；
   - 应用没有登记恢复地址；
   - 明确拒绝，包括拒绝和 correlation 失败同时发生；
   - 再次失败。
6. **安全。** 续接路径只接受站内相对路径，外部 URL 被拒绝。
7. **回归。** 原有的 MFA、step-up、协议回调、机器端点测试保持通过。Identity 会话已失效时，重新授权会要求重新认证。

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

- **「重新登录」：** 指向依赖方自己的登录入口（模板现有的 `/api/v1/auth/login`），由用户点击后重新发起挑战。友好页是兜底：可恢复的情况由 R6 自动重来一次，只有自动重来不可用或预算已用完时，才落到这里。
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
- **复用：** 复用现有的开放应用登记、`HomeUrl` 读取方法和本地化显示名；新增的只有一个可选的应用属性「登录恢复地址」（R6），存法与 `HomeUrl` 相同。
- **依赖方续接：** 用 Data Protection 保护的一方 Cookie 承载续接记录，不新增框架组件；correlation 失败那条路径直接用官方失败事件里的 `AuthenticationProperties`。
- **回滚：** 按提交撤回即可，不涉及数据和迁移。

## 3. 计划

| 步骤 | 内容 | 验证 |
| --- | --- | --- |
| 1 | 修复前失败的用例：Identity 过期显示原文、依赖方返回空 400 | 受影响的集成测试 |
| 2 | R1、R2 Identity 服务端加友好页 | 集成测试全量；机器端点回归 |
| 3 | R3 依赖方 | 集成测试全量 |
| 4 | R4 前端页面和词条 | 前端全量；`check-i18n`；两种形态 |
| 4b | R6：登录恢复地址的登记与表单、Identity 交接、依赖方续接记录与恢复入口、`OnRemoteFailure` 自动重来；先写修复前失败的用例 | 集成测试全量；真实 OIDC 处理器；R6 验收 1～7 |
| 5 | 端到端闭环 | 真实浏览器走 R5 第 2、5、6 条；完整 `verify.ps1`，Identity 和 Resource 各一种场景；`scripts/check-all.ps1`；`template-package/test-template-package.ps1` |

## 4. 验证

（实施后填写。）
