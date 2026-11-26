# 授权请求缓存过期与依赖方回调失败的友好页

状态：进行中 · 风险档：高（认证协议路径，只改模板；2026-11-26 按用户确认改为端到端策略：登录只在页面可见时发起、两端时限对齐、超出时间窗由友好页兜底，见 R0、R6、R7） · 关联：待定

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
  - 正常流程（用户在场、在时间窗内）登录后直接回到深链，不出现中间页（R0、R6、R7）；超出时间窗等不可继续的情况，都有一个说明清楚、可以操作的页面；用户不再看到协议原文或空白的 400；
  - 能合法回调依赖方的错误、机器端点的错误，行为都不变。
- **非目标：**
  - 不延长请求缓存寿命；
  - 不让前端推测 request token 的有效期；
  - 页面上不显示错误码、traceId 或协议原文；
  - 不改框架。

### 功能要求与验收

#### R0 端到端策略（2026-11-26，用户确认，取代先前的自动恢复方案）

用户要的体验：登录成功后直接回到依赖方和原来的深链，中间不插提示页。为此从端到端重新梳理流程，不在过期之后补救，而是让正常流程里不会出现过期。

**根因有两个：**
1. **授权交易在用户不在场时就已发起。** 依赖方前端收到 401 后，立刻整页跳转去登录（`http-error-interceptor.ts` 调用 `startLogin`），页面在后台时也照样跳转。
2. **同一笔交易，两端的时限不一致，都是未经调整的默认值。** 依赖方的 correlation / state 是 15 分钟（ASP.NET Core `RemoteAuthenticationTimeout`），Identity 的 request token 是 1 小时（OpenIddict）。

**依据：**
- OAuth 2.0 for Browser-Based Applications（RFC 10017，BFF 模式）：BFF 负责续期；续不上就结束会话，由前端发起一次新的顶层授权。
- OIDC Core §3.1.2：Identity 会话满足要求时，授权会静默完成。
- `RemoteAuthenticationTimeout` 与 `OpenIddictServerOptions.RequestTokenLifetime` 是官方提供的交易时限配置。
- HTML Living Standard 的 Page Visibility API 提供可见状态与变更事件。模板据此延后需要用户参与的登录。

**原则：** 授权交易只在用户在场时发起；整条链路使用同一个交互式登录时间窗；超出时间窗时只有一种兜底方式，即 R1～R4 的友好页。

| 流程 | 行为 |
| --- | --- |
| F1 会话有效 | 不变 |
| F2 依赖方会话结束（401） | 页面可见：立即发起顶层登录，并带上 `returnUrl`（深链）。页面不可见：记下「待登录」，等页面恢复可见时再发起（R6）。Identity 会话有效时静默完成，回到深链；会话失效时登录（含 MFA）后回到深链。全程不出现中间页。 |
| F3 交互式登录时间窗 | 依赖方的 `RemoteAuthenticationTimeout` 与 Identity 的 `OpenIddictServerOptions.RequestTokenLifetime` 使用同一个显式值，默认 1 小时（R7）。 |
| F4 超出时间窗（在登录页停留超过 1 小时） | Identity 侧显示 `login-expired` 友好页（R1、R2），依赖方侧显示 `expired` 友好页（R3），都只需点一次即可重新登录。深链不保留。 |
| F5 退出请求过期 | `logout-expired` 友好页（R1、R2） |
| F6 被拒绝 | 依赖方 `denied` 友好页（R3） |
| F7 机器端点、能合法回调的错误 | 不变 |

**明确不做：**
- 过期之后的自动恢复：恢复地址登记、交互标记、续接记录、重试预算；
- 按失败消息自动重试；
- 延长时限去掩盖「交易在用户不在场时发起」的问题。

#### R6 登录只在页面可见时发起（依赖方前端，`RemoteTokenAuth` 中带浏览器会话的形态）

- 依赖方前端因 401 需要发起整页登录时：
  - `document.visibilityState === 'visible'`：照现状立即发起，`returnUrl` 照现状带当前深链（含查询串和片段）；
  - 页面不可见：先清理会话上下文（与现状一致），只登记一次待发起的登录，并记下当时的深链；等 `visibilitychange` 变为 `visible` 时再发起。多个 401 只发起一次。
- 只改这一个发起入口（`AuthService.startLogin` 或它的调用方），不新增服务、不新增状态库，复用现有的可见性监听写法（`session-context-service`、`signalr-service`）。
- 本地身份形态（`LocalIdentity`）的 401 只是站内导航到本地登录页，不会产生协议交易，不在本条范围内。
- **验收：**
  - 页面不可见时收到 401：不发起导航；恢复可见后只发起一次，`returnUrl` 等于收到 401 时的深链。
  - 页面可见时，行为与现状一致。
  - 用 spec 模拟 `visibilityState` 和 `visibilitychange`，按行为断言。

#### R7 两端交易时限对齐（服务端配置）

- 依赖方 OIDC 处理器的 `RemoteAuthenticationTimeout`，与 Identity 的 `OpenIddictServerOptions.RequestTokenLifetime`（授权请求缓存，以及退出请求缓存若适用）显式设为同一个值，默认 1 小时。在代码里设定，注释写明两者必须一致、以及取值理由（留出改密码、绑定 MFA 等绕路的时间）。依赖方的 nonce 寿命沿用同一值。
- 不新增部署变量，按模板「默认值不进部署变量」的约定。确有需要时，项目可按官方选项自行覆盖。
- **验收：**
  - 用假时钟或缩短的配置验证：在时间窗内（例如 59 分钟）完成登录，直接回到深链；分别验证 Identity 请求过期和依赖方回调时 correlation Cookie 过期，各自落到 F4 的友好页。
  - correlation Cookie 的有效期随 `RemoteAuthenticationTimeout` 变化。

#### R1 Identity 交互端点的本地协议错误（`OpenIddictServer`）

本节描述 F4、F5 的兜底行为（见 R0）。

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

- **「重新登录」：** 指向依赖方自己的登录入口（模板现有的 `/api/v1/auth/login`），由用户点击后重新发起挑战。
- **本节的友好页是 F4、F6 的兜底**（见 R0）。
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

1. 时钟越过交互式登录时间窗（R7）后（用 `FakeTimeProvider` 或配置缩短寿命，不靠真实等待），授权端点和退出端点分别显示对应的友好页，不再出现协议原文。先写修复前失败的用例。
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
| 4b | R6 页面可见时才发起登录；R7 两端时限对齐；先写修复前失败的用例 | 前端 spec；集成测试全量；R6、R7 验收 |
| 5 | 端到端闭环 | 真实浏览器走 F2、F3、F4 和 R5 第 2、5、6 条；完整 `verify.ps1`，Identity 和 Resource 各一种场景；`scripts/check-all.ps1`；`template-package/test-template-package.ps1` |

## 4. 验证

- 修复前的集成测试（`94ece4f3`）：Identity 在缓存授权过期时输出 `invalid_token / ID2019` 的协议原文。Resource 失败回调只设置 400，应用的 StatusCodePages 补成 `application/problem+json`，浏览器没有可操作页面。退出确认页的修复前前端全量用例有 2 条预期失败。
- R6、R7 修复前（`93f0ab86`）：生成 Resource 后执行集成项目的 `dotnet test --logger trx` 和前端 `npm test -- --watch=false`。集成 241 通过、1 条预期失败，correlation Cookie 实际只保留 15 分钟。前端 1010 通过、1 条预期失败，隐藏页签收到两次登录请求后立即发出了两次导航。执行记录 `leistd-codex-test-visibility-window-a74032263477`。
- 仓库闸门执行记录 `leistd-codex-check-authorize-expiry-ccd59b215666`。`pwsh scripts/check-all.ps1` 的 52 项全部通过，含 i18n。`pwsh template-package/test-template-package.ps1` 验证两个生成场景的包内容、隔离安装、来源标记和确定性重建，均通过。
- 生成项目验证（`024402d0`，runner `leistd-codex-verify-visibility-window-6a95f52bd79b`）。本地化 Identity 和 Resource 分别执行完整 `pwsh scripts/verify.ps1`，均通过。Identity 为 351 后端单测、739 集成测试、1776 前端测试、7 条仓库 E2E。Resource 为 202 后端单测、245 集成测试、1077 前端测试。覆盖 Token JSON、UserInfo 质询与空响应体、合法授权回调的 error 和 state、两条退出过期入口，以及官方 OIDC 处理器的回调失败分类。
- 单语言 Identity 和 Resource 分别执行 `npm run lint`、`npm test -- --watch=false`、`npm run build`，均通过，前端测试分别为 1710 和 1012 条。纯 Bearer Resource 的 `pwsh scripts/verify.ps1 -Fast` 通过，含 201 后端单测。
- 纯 Bearer 的产物另检查前端目录、浏览器会话测试文件和 OIDC 浏览器回调配置均未生成。变异分别缩短 Identity 请求令牌或 Resource nonce 至 15 分钟，以及去掉依赖方回跳片段，三个对应断言均预期失败。执行记录 `leistd-codex-mutate-login-window-70df508f4c2c`。
- F2 用隔离的有头 Chromium 和真实 Identity、Resource 验证，执行记录 `leistd-codex-browse-login-window-9d08e3a7d911`。采用 Playwright `connectOverCDP` 的 `noDefaults` 保留 Chromium 原生的可见状态。真实页签隐藏时收到 `/api/v1/auth/me` 的 401，导航次数为零。恢复可见后仅发起一次挑战。Identity 会话仍有效时静默完成，无会话时完成口令及 MFA。两条分支的真实 OIDC 回调 `Location` 和最终地址均为 `/workspace/dashboard?tab=roles&source=expiry#top`，未经过友好页。 [Playwright 的 noDefaults 选项](https://playwright.dev/docs/api/class-browsertype#browser-type-connect-over-cdp-option-no-defaults)
- 有头 Chromium 通过 `npx playwright test --config=expiry-manual.config.ts` 验证 F3、F4 及兜底，执行记录 `leistd-codex-browse-login-window-8aae6b2f8305`。F3 将挑战和缓存请求拨到第 59 分钟，口令和 MFA 完成后直接回到 `/workspace/dashboard?tab=roles&source=expiry#top`，未经过友好页。F4 中依赖方 correlation Cookie 在 Chromium 自然到期，真实回调显示过期页，点一次重新登录即可建立新会话。退出确认重载和过期后提交均只有 Identity 首页按钮。连续两次授权拒绝均停在拒绝页，通用失败进入通用页。
- F4 的 Identity 会话复核。早期夹具在点击登记的 `HomeUrl` 前显式清除了 Identity Cookie，造成第二次口令和 MFA。补测保留 Cookie，仅将 OIDC 请求时钟拨到第 61 分钟，Identity 会话时钟保持实际时间。第一次口令和 MFA 完成并显示过期页后，同一浏览器的 Identity `/api/v1/auth/me` 返回 200 和原用户。点击一次后静默完成新授权，直接到依赖方首页，期间未出现 Identity 登录页、MFA 或过期页。
- 有头 Chromium 截图 18 张，中、英、中文暗色各 6 张。执行记录 `leistd-codex-browse-login-window-261fce339189`。逐板对照如下；组件间距、字体、主色与暗色来自 Spartan Empty 和模板令牌，原型地址栏和设计说明未进入页面。

| 画板 | 实际截图对照 |
| --- | --- |
| IdentityExpired | 时钟、标题、说明和两个按钮的语义与顺序一致。应用名按测试登记显示为 Demo application。英文主次按钮按 Spartan 可用宽度换行。 |
| IdentityUnknownClient | 时钟、标题、说明及唯一的 Identity 首页按钮一致。未知、缺少 ID、无 HomeUrl 的应用均只有站内链接。 |
| IdentityLogoutExpired | 退出图标与标题一致。按 R2 截取真实流程的无应用版本，说明改用「原来的应用」，只保留 Identity 首页按钮。 |
| RpExpired | 沙漏、超时文案、单个重新登录按钮一致。 |
| RpDenied | 禁止图标、拒绝文案、重新登录与返回首页的顺序一致。 |
| RpOther | 警告图标、通用文案和单个重新登录按钮一致。 |
