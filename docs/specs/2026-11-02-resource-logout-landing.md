# 资源服务退出后回到登录

状态：已修订 · 风险档：高（认证退出链）

## 1. 需求

带浏览器会话的 Resource 退出后回到 `/`，会启动 SPA，再由未登录处理发起挑战；受保护首页因此多启动一次前端，并覆盖登录端点的默认入口。采纳模板层修复：退出回调直接回到登录挑战端点，与 LocalIdentity 退出回登录表单的体验一致。默认登录入口仍由 `Login` 的默认 `returnUrl` 决定，不保留退出前页面。

2026-11-02 用户确认按任务书完成策略审查、实施、自验、PR 与 Claude 审查后合并。

| 要求 | 验收（Given / When / Then） | 覆盖 |
| --- | --- | --- |
| 直接发起登录 | Given 带浏览器会话的 Resource，When 退出，Then 表单保护 state 的落点为同源 `/api/v1/auth/login`，回调 302 到该地址，无查询参数 | `ResourceBrowserSessionTests` |
| 保留退出协议与撤销 | When 退出，Then `post_logout_redirect_uri` 仍是 `/api/v1/auth/signout`，hint 留在表单，原票据失效，调用方 state 保留 | 同上 |
| 手动入口不自动登录 | Given `/auth/login`，When 手动访问或 Mock 退出，Then 保留继续登录按钮，由用户发起登录 | `resource-login.spec.ts` 与代码审查 |
| 选项实际生效 | Given 已修复实现，When 删除 `SignedOutRedirectUri` 设置，Then 精确落点断言失败 | 变异验证 |

## 2. 方案

在 `RemoteTokenAuthenticationExtensions` 的 `ResourceBrowserSession` 条件内，紧邻 `SignedOutCallbackPath` 设置官方 `SignedOutRedirectUri`；控制器仅选择 OIDC scheme，不传覆盖选项的属性。保留先退出本地会话且不带回跳地址的顺序和原因。

[ASP.NET Core 10 官方实现](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Security/Authentication/OpenIdConnect/src/OpenIdConnectHandler.cs) 在退出事件之前填入该选项，并将相对路径转换为绝对地址。地址与现有协议回调同样依赖受信转发头，部署依赖不变。Identity 在结束会话后才回调（sid 匹配免确认，否则经确认页），紧接的授权因此进入登录表单。

`ResourceLogin` 只用于手动入口与 Mock，不在守卫、401、真实退出自动链中。Mock 无签发方，自动跳转会立即重新登录，保留手动按钮并补原因注释。

不改变回调登记、退出表单、state 封装、前端守卫、401、Mock 与 Landing。回滚恢复原控制器落点并删除选项即可；不涉及框架公共 API 与包内容。

## 3. 计划

| 任务 | 产物 | 验证 |
| --- | --- | --- |
| 退出落点 | 官方选项、控制器与回归断言 | resource-notifications 生成项目先红后绿；全部 ResourceBrowserSessionTests 与 ControllerAuthorizationConventionTests；变异和还原 |
| 手动入口与契约 | ResourceLogin 注释、认证常驻文档 | 定向 spec、lint、读取成本与静态闸门 |
| 交付 | 单个 PR | CI 全绿；Claude 审查覆盖最终 head 后 merge |

## 4. 结果

已实现 ResourceBrowserSession 的官方退出落点选项，移除控制器覆盖值，保留本地 Cookie 注销顺序与退出协议；补充精确 state 与回调 Location 回归断言。ResourceLogin 的手动/Mock 定位已写入注释。

resource-notifications 实际生成结果通过定向后端、前端与 lint 验证；回归先红后绿、删除选项的变异被拒绝，还原后重新构建通过。静态与读取成本闸门通过，未抬高读取上限。验证明细随 PR 提供。

常驻文档已同步 `template/docs/standards/auth.md` 与 `docs/template/browser-authentication.md`。既有认证专题索引覆盖本次内容，无新增专题。

为遵守不出框架 beta 的边界，未修改会被当前发布规则归入未建模输入的 OIDC e2e 脚本。该脚本可读取退出表单 state 字段，但尚未跟随资源退出回调；本轮由集成测试携带原表单 state 验证生产处理器的 302 落点，未新增跨进程端到端证据。前端运行逻辑、Mock 与回调登记保持原契约。

## 5. 修订：2026-11-02 回到官方默认落点

用户决定模板采用官方默认退出策略：回到应用根 `/`。模板根为可匿名展示的公开首页，本身没有受保护首页的登录中转缺陷；退出直接回登录属于项目诉求，留给项目选择。此决定替代 §1–§4 中直接回登录的默认策略。

部分回退 PR #219：删除 `SignedOutRedirectUri` 及其注释；控制器保持仅指定 OIDC scheme，由官方默认决定落点；state 与回调 Location 断言改为绝对地址 `https://localhost/`，保留跟随原保护 state 验证 302 的回归用例。

ResourceLogin 的说明改为手动入口与 Mock 退出落点，真实守卫与 401 直接挑战签发方；首帧不自动跳转，以免 Mock 退出后立即登回。两份认证常驻文档同步匿名根前提及受保护根的项目适配选择。前端运行逻辑、回调登记与退出协议保持原契约。

验收：Given 模板默认 Resource 浏览器会话，When 退出并处理原表单 state 的 signout 回调，Then state 和 302 Location 均为同源应用根；When 临时加回直接登录选项，Then 精确落点断言失败，还原后重新构建通过。覆盖仍由 ResourceBrowserSessionTests、ControllerAuthorizationConventionTests、resource-login.spec.ts 及静态、lint、读取成本闸门承担。

修订验证已通过：实际生成的资源浏览器会话与控制器授权定向测试、前端定向测试和 lint、静态及读取成本闸门全部通过；加回登录选项的变异被精确落点断言拒绝，还原后重新构建通过。本轮仅模板与仓库文档，不改 scripts/，不触发框架 beta。
