# 登录与退出时的界面闪烁

状态：已完成 · 风险档：高（认证协议链、跨层前后端、会话收尾） · 关联：待开 PR

## 1. 需求

### 问题与背景

下游（分别使用 SSO 与本地登录）反馈：点"登录"后页面闪烁、像快速刷新；退出回到登录页时像刷新两次。模板 Mock 模式没有这些现象。

真实 SSO 环境（develop `ee7a7402` 生成的 Identity + 带浏览器会话的 Resource、真实 PostgreSQL 与 OpenIddict、生产构建前端）逐帧录屏确认：

| 场景 | 观察 |
| --- | --- |
| SSO 登录（冷） | 仍在 Identity 登录页时按钮已复位、"登录成功"提示弹出 → OpenIddict `form_post` 自动提交白页约 1.4 秒 → 资源应用预加载页约 2 秒 → 工作台，共约 6 秒 |
| SSO 登录（热） | 登录页上的按钮复位与提示仍出现；白页一闪未截到 |
| 资源服务 / Identity 退出 | 先清空当前页用户（问候语只剩称呼前缀、头像消失），再整页离开 |

Mock 不经 OIDC 链、接口即时返回、Mock 用户偏好与设备一致，因此看不到。问题不在框架（框架无前端 OIDC 组件），全部在模板。

### 目标

整页离开前不再改动当前界面；SSO 登录链不再渲染自动提交的中转页；退出时不先清空用户。

### 非目标与保留行为

交付时不宣称消除：整页启动单页应用的预加载页；退出仍以表单 POST 发往 Identity（`id_token_hint` 不进地址栏）的中转页；重新认证路径 `clear()` 先退回设备语言；退出时 `SignalRService.reset()` 先清空通知列表；首次访问出现过一次、未复现的浅色预加载帧；portal 登录后 SPA 跳转时的偏好切换时序（下游另议）。

### 功能要求与验收

| 要求 | 验收（Given/When/Then） | 覆盖 |
| --- | --- | --- |
| 登录挑战用 302 | Given 带浏览器会话的 Resource，When 访问 `/api/v1/auth/login`，Then 302 到签发方授权端点，带 code/PKCE 参数，不渲染表单页 | `ResourceBrowserSessionTests`、`SigningKeyRotationTests`、OIDC e2e |
| 授权码以查询串回调 | When 签发方以 302 带 code/state 回到 `/api/v1/auth/signin`，Then 建立会话并回到原 returnUrl；state/correlation/令牌错误仍 400 且不发会话 Cookie | 同上（`Production_remote_failure_rejects_invalid_protocol_messages`、`Missing_correlation…`） |
| 退出仍为表单且 state 完整 | When `POST /api/v1/auth/logout`，Then 返回自动提交表单（无 Location），含 `id_token_hint`、`client_id`、`post_logout_redirect_uri` 与官方格式的受保护 state（解封得到最终落点），禁止缓存，原票据已失效 | `Sign_out_posts_the_identity_token_hint_…` |
| 登录收尾不改界面 | Given 登录成功且目标为校验后的 `/connect/*`，Then 不读 `/me`、不建立会话上下文、不弹提示，加载态持续到整页离开；站内目标保持原顺序 | 前端 `login.spec` 等（见 §4） |
| 两阶段加载态 | 第二步验证请求中与登录收尾中均保持忙碌；任一失败恢复可操作 | 前端 `two-factor-challenge.spec`、`login.spec` |
| 退出不先清空主体 | 真实表单退出与本地身份 `logout`/`signInAgain` 请求期间 `currentUser` 保持；Mock 分支不变 | 前端 `auth-service.spec` |
| 登录页不后插按钮 | 注册摘要未返回且浏览器支持通行密钥时等高占位，返回后等高替换，不可用/失败/不支持不残留 | 前端 `login.spec` |
| 真实环境 | 同一真实 SSO 环境复录，对比实施前帧序列 | 人工验收 |

### 已确认的决策

- 2026-10-31 用户确认对比页方案，并要求由 Claude 实施（可用子 agent）、Codex 审查。
- 方案经 Codex 两轮审查通过：第一轮否决"在 `OnRedirectToIdentityProvider` 里接管并 302"（会跳过 state 封装与 PKCE 保存），改为全局官方默认 302、仅退出事件接管。

## 2. 方案

### 现状事实

- `RemoteTokenAuthenticationExtensions.cs` 设 `AuthenticationMethod = FormPost`（授权与退出都输出自动提交表单），`ResponseMode` 取 ASP.NET Core 默认 `form_post`（OpenIddict 输出无样式自动提交页）。
- `login.ts` `finishLogin` 先 `clear()`→`loadUser()`→`establish()`（应用账户外观/语言）→ toast → `ReturnUrlService.navigate`（`/connect/*` 用 `location.assign`）→ `finally` 复位加载态。
- `auth-service.ts` `logout()` 先 `clearAuthData()` 再整页离开。
- 通行密钥按钮依赖 `SecurityConfigService.config()`，返回前不显示。

### 设计

- **协议（后端）**：`ResponseMode = query`；`AuthenticationMethod` 回到官方默认 `RedirectGet`；`OnRedirectToIdentityProviderForSignOut` 接管输出：保留 `client_id`、`id_token_hint`、`post_logout_redirect_uri`，校验结束会话端点，`StateDataFormat.Protect(Properties)` 写入 state，官方 `BuildFormPost()` 生成表单，写与官方相同的 Content-Type 与禁止缓存头后 `HandleResponse()`。
- **安全取舍**：RFC 9700 §4.2.4、§4.3.1 将 form_post 列为可选降险措施。改用 query 后授权码可能进入浏览器历史与访问日志；防护依据为授权码一次性、机密客户端认证、PKCE（Identity 强制）、精确回调地址与官方 state/correlation/nonce 校验。协议 Cookie 保持官方属性（None/Secure Always），不调整 SameSite。Duende BFF 推荐同样的 code + PKCE + query 组合。
- **前端**：`ReturnUrlService.navigate` 返回明确结果（已发起整页离开 / SPA 跳转 / 默认落点）；整页目标判断复用 `resolve()` 的站内校验并受 `OpenIddictServer` 条件约束。`/me` 读取边界：`finishLogin`、外部登录正常完成、`two-factor-required.continue` 在整页目标时跳过；外部回调探测、首绑守卫、所有站内目标保留。退出只同步 `SignalRService.reset()`，不提前清空主体。通行密钥区用 Spartan Skeleton 等高占位。

### 备选与取舍

- 保留 form_post 但替换 OpenIddict 内置 `ProcessFormPostResponse` 输出带 `color-scheme` 的中转页：须复制协议库的转义与头部逻辑，只能按系统主题上色，仍多一次文档绘制，不采纳。
- 退出也改 302：`id_token_hint` 会进地址栏，违背既有决定，不采纳。

### 兼容与回滚

仅模板；已生成项目同步模板后生效。回滚即恢复 `AuthenticationMethod = FormPost`、去掉 `ResponseMode` 与退出事件接管，前端各收尾恢复原顺序。依赖方登记的回调地址不变（仍为 `/api/v1/auth/signin`、`/api/v1/auth/signout`）。

## 3. 计划

| 任务 | 产物 | 验证 |
| --- | --- | --- |
| 协议链 | `RemoteTokenAuthenticationExtensions.cs`、`ResourceAuthController.cs` 注释 | 回归先红后绿：`ResourceBrowserSessionTests`、`SigningKeyRotationTests`；退出 state 断言变异验证 |
| 跨进程 | `scripts/test-template-oidc-e2e.ps1` 挑战、授权响应、回调三段 | OIDC e2e 全场景 |
| 前端收尾/加载态/退出/占位 | `template/frontend/**`（子 agent 实施） | 各 spec 先红后绿，`identity-all-features`、`resource`、`standalone` 生成与前端全量单测、lint |
| 常驻文档 | `docs/template/browser-authentication.md`、`template/docs/standards/auth.md`、`ReturnUrlService.navigate` 注释 | 文档闸门、读取成本 |
| 真实环境验收 | 复录逐帧对比 | 人工验收 |
| 审查 | Codex 审查实施 | 明确通过 |

不做：替换 OpenIddict 中转页、改退出为 302、portal 偏好时序、浅色首帧推测性修复。

## 4. 结果

### 实际交付
- 协议：`ResponseMode = query`、登录挑战回到官方默认 302；退出事件接管输出，按官方顺序保存调用方 userstate、封装受保护 state，用 `BuildFormPost()` 输出并写禁止缓存头。
- 前端：`ReturnUrlService.navigate` 返回 `left | routed | fallback`；`finishLogin`、外部登录回调、`two-factor-required`、首绑守卫按 §2 的 `/me` 边界处理整页目标；整页离开时不复位加载态；第二步组件叠加父组件收尾态；真实退出与本地身份 `logout`/`signInAgain` 不先清空主体；通行密钥区 Skeleton 占位。
- e2e 脚本与常驻文档同步（`docs/template/browser-authentication.md`、`template/docs/standards/auth.md`）。

### 与计划的偏差
- 退出 state 断言原计划"带回 `/api/v1/auth/signout` 看落点"不可证伪（默认 `SignedOutRedirectUri` 也是 `/`），改为解封表单 state 核对 RedirectUri，并以删去 `Protect` 的变异验证。
- 实施审查补出 userstate 保留步骤（I1），新增 `Sign_out_keeps_a_caller_state_inside_the_protected_state`。

### 验收记录
- 回归先红后绿：resource 场景 `ResourceBrowserSessionTests`、`SigningKeyRotationTests` 修复前大面积失败，修复后认证相关 53/53；前端 7 个 spec 先 17 失败后 162 通过。
- 全量：identity-all-features 前端 143 文件/1620、standalone-external-login 123/1369、resource-notifications 90/831，lint 与生产构建通过；OIDC e2e 默认档 11 个场景全部通过（真实到期场景按设计跳过）。
- 真实环境复录（人工验收，Claude 执行）：SSO 登录时登录页保持加载直到离开、无提示与复位、无自动提交中转文档；资源服务与 Identity 退出时用户保持到离开。
- 审查：Codex 方案两轮（第二轮通过）、实施两轮（第二轮通过）。

### 遗留项
- 首次访问新源（或部署后样式表哈希变化）时预加载页先浅色：根因是 Angular 生产构建的关键 CSS 内联按静态 HTML 匹配，运行时加在 `<html>` 上的 `dark` 类不在其中，内联 CSS 缺 `.dark` 覆盖。`/* beasties:include */` 注释经实测会被 Angular 的 CSS 压缩去掉、`allowRules` 未开放；候选为 `optimization.styles.inlineCritical: false`，待测首次内容绘制后另行决定。
- 下游同步：下游项目的 SSO 退出回跳、整页离开与注册入口占位，由下游按回执处理。

### 已上收的常驻文档
`docs/template/browser-authentication.md`、`template/docs/standards/auth.md`、`ReturnUrlService.navigate` 与 `endSessionAndGo` 注释。
