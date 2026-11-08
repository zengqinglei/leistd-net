# 待完成登录改用官方两步临时 Cookie

状态：已完成 · 风险档：高 · 关联：https://github.com/zengqinglei/leistd-net/pull/246

## 1. 需求

下游反馈（定性为建议对齐，非缺陷）：口令或外部登录这一步通过、尚待第二步时，模板把"待完成登录"表示为响应体里的随机令牌，前端放在导航状态里。现状（develop `2d3d6846`）经核对没有"第二步前签正式会话"的路径，受限会话也拿不到授权码；但有三处与官方做法不一致：

- 令牌只活在当前页面：刷新、或另一个依赖方在别的标签页发起授权时，`/connect/authorize` 看不到待完成登录，只能回登录页重新输口令。
- ASP.NET Core 的官方两段式（`SignInManager.SignInOrTwoFactorAsync` → `DoTwoFactorSignInAsync`）用独立方案 `IdentityConstants.TwoFactorUserIdScheme` 的临时 Cookie 承载中间身份，HttpOnly，脚本拿不到；模板的令牌对页面脚本可见。
- 挑战期限固定 5 分钟，与可配置的邮箱码有效期（`EmailCodeExpiryMinutes`，默认 5 分钟）无关：临近期限重发的码，邮件写"5 分钟内有效"，实际只剩几十秒；有效期调长时挑战先到期。

逐项裁决：

| # | 建议 | 结论 | 理由 |
| --- | --- | --- | --- |
| 1 | 待完成状态改用官方 `TwoFactorUserIdScheme` 临时 Cookie | 采纳（模板） | 官方组件与顺序；HttpOnly、刷新可续、授权端点可识别。服务端挑战记录仍是唯一事实来源（次数、安全版本、一次消费、凭据锁），Cookie 只承载它的不透明引用 |
| 2 | 授权端点凭待完成身份接回第二步 | 采纳（模板） | 交互请求带回第二步页、各依赖方保留自己的缓存请求；`prompt=none` 回 `login_required`（认证尚未完成）。临时身份不构造依赖方主体、不签授权码；签发前的受限判定不变 |
| 3 | 强制首绑也在临时身份下完成 | 不采纳 | 见下文 |
| 4 | 临时身份期限与邮箱码、一次重发对齐 | 采纳（模板），期限取法自定 | 见下文 |
| 5 | 清除过期、失效的临时 Cookie | 采纳（模板），作为第 1 项的回归要求 | 官方 Cookie 处理器对票据过期返回失败、不删 Cookie；各入口须显式签出 |

框架层无改动：两步临时身份是宿主应用的认证流程，`Leistd.Security.OpenIddict.*` 只负责协议与令牌，不感知应用的第二步。

**第 3 项不采纳的理由**：首绑不是"认证未完成"。没有可用第二步方式的人已经出示了他拥有的全部因子，认证是完整的；缺的是组织策略要求的合规状态，这和"已登录之后组织才打开要求"是同一件事。后者必须由正式会话上的动态判定处理（每个请求按租户开关与本人状态重算，不写进会话），模板已经这样做：`TwoFactorSetupEnforcementMiddleware` 对受限会话默认拒绝，只放行 `AllowDuringTwoFactorSetup` 标记的短名单与匿名端点（协议匿名入口由各自逻辑判定），`/connect/authorize` 对受限会话不签授权码。若首绑改走临时身份，同一状态就有两种表示，设置接口要同时接受临时身份与受限正式会话，入口反而更多。所以保留一种表示：受限正式会话 + 集中默认拒绝。下游指出现有用例靠关闭要求解除限制、没有真正走完首绑，这一缺口采纳：补一条实际登记身份验证器后授权成功的用例。

**第 4 项的期限取法**：邮件写明"N 分钟内有效"（N = `EmailCodeExpiryMinutes`），因此每封发出的码都必须在待完成登录内有满 N 分钟。

- 开启邮箱能力（`Email`）时，待完成期限 = 2 × N（默认 10 分钟），从挑战创建起算、创建时按当时策略算定，绝对、不滑动；未开启时保持 5 分钟。
- **只有外层剩余至少 N 时才真正新发**，每封新发的码都有完整 N 分钟。剩余不足 N 时拒绝新发（新错误码 `Auth:EmailCodeResendTooLate`，不计数），但**不结束登录**：已发出的码与其他方式照常可用，直到原绝对期限；前端就地说明"剩余时间不够再发新码，请用已收到的码或换一种方式，或重新登录"。
- 发送间隔内再请求沿用上一封，返回该码的真实剩余秒数，不延长任何期限。
- 保留：输错与重发都不延长、不清零次数；新码替换旧码。

不取"有效期 + 发送间隔"：那样外层很快不足 N，几乎每次重发都会被拒。也不按重发滑动延期：口令已过的中间状态应有固定上界。

验收：

| 要求 | 验收（Given / When / Then） | 覆盖 |
| --- | --- | --- |
| 第一步只写临时 Cookie | Given 已启用第二步的账号，When 口令或外部登录第一步通过，Then 响应只写临时 Cookie（HttpOnly、非持久、不滑动、绝对期限同挑战），不写会话 Cookie，响应体不含可用于完成登录的凭据 | 集成 |
| 临时身份不是正式身份 | Given 只有临时 Cookie，When 调 `/auth/me` 与其他需登录接口，Then 401；调 `/connect/userinfo`，Then OpenIddict 按协议拒绝（无访问令牌为 `invalid_request`），不返回用户资料 | 集成 |
| 刷新可续 | Given 第二步页，When 刷新，Then 凭临时 Cookie 取回方式与打码邮箱，继续第二步 | 集成（取上下文接口）；前端单测 |
| 完成后清临时、签正式 | Given 第二步通过，Then 响应删除临时 Cookie 并签发会话 Cookie；挑战一次消费，重放失败 | 集成 |
| 授权接回第二步 | Given 待完成登录，When 两个依赖方各自发起交互授权，Then 都回到第二步页且各带自己的 `request_uri`；完成一次后两者分别续跑拿到授权码 | 集成 |
| 静默授权 | Given 待完成登录，When `prompt=none`，Then `login_required`，不跳页、无授权码 | 集成 |
| 重新认证凭新临时身份续接 | Given `prompt=login`、`max_age=0`、已超过的正数 `max_age` 各一，When 回登录页重输口令→凭新临时身份回第二步→完成，Then 同一缓存请求授权成功、全程保留原证明、不循环；证明挪给另一 `request_uri` 仍失败 | 集成 |
| 正式会话与临时 Cookie 并存 | Given 有效正式会话且带一个旧临时 Cookie，When 普通授权，Then 按正式会话规则签授权码，旧临时 Cookie 被删 | 集成 |
| 重新认证不被旧临时身份抵消 | Given `prompt=login`（或 `max_age` 超过）且已有更早的待完成登录，When 授权，Then 回登录页重输口令，旧临时 Cookie 被删 | 集成 |
| 过期临时 Cookie 被删 | Given 临时 Cookie 票据已过期（或服务端挑战已失效、安全版本已变），When 经授权、口令登录、免口令登录、外部登录、退出，Then 响应删除临时 Cookie；授权回登录页，不循环 | 集成（推进时钟） |
| 新待完成覆盖旧的 | Given 已有有效待完成登录，When 再次口令登录到第二步，Then 先删旧再写新，最终浏览器持有新的；旧挑战作废 | 集成 |
| 重发有完整窗口 | Given 开启邮箱、N=5，When t=0 发码、t=4 重发，Then 新码在 t=8.9 能完成登录 | 集成（推进时钟） |
| 剩余不足不新发、不结束 | Given 同上 t=4 已重发，When t=6 再请求发码（剩余 4 < 5），Then `Auth:EmailCodeResendTooLate`、不发信；随后用 t=4 的码完成登录 | 集成 |
| 剩余恰为 N | Given N=5，When t=5 首次请求发码（剩余恰 5），Then 新发且有效 300 秒 | 集成 |
| 延迟首发与非默认 N | Given N=3（期限 6），When t=2 首发、t=3.5 重发（剩余 2.5 < 3），Then 首发有效 180 秒，重发被拒 | 集成 |
| 间隔内复用 | Given t=0 发码，When t=0.5 再请求，Then 沿用原码、返回约 270 秒，不发第二封 | 沿用现有复用用例并按新契约改写 |
| 邮箱方式刷新续接 | Given t=4 重发后，When t=6 刷新第二步页（页面自动请求发码被拒），Then 页面就地提示、仍可用 t=4 的码完成 | 前端单测；浏览器 |
| 首绑实际走完 | Given 组织要求两步验证、本人未设置，When 登录后在受限会话里登记身份验证器，Then 授权端点签发授权码 | 集成 |
| 并发完成只建一个会话 | Given 同一待完成登录（两个请求带同一个临时 Cookie），When 同时提交两种有效方式，Then 只登记一个会话 | 集成（新增，用现有夹具） |
| 取消与篡改 | Given 临时 Cookie，When 取消（重复调用）、或票据被篡改后访问上下文、提交、发码、通行密钥选项，Then 幂等成功 / 失败且删 Cookie | 集成（并入生命周期用例） |
| 真实刷新 | Given 浏览器，When 口令→独立第二步页→刷新→完成，Then 回到原目标 | 浏览器 e2e |

**非目标**：不迁入 ASP.NET Core Identity 的用户存储与 `SignInManager`；不改受限会话模型；不改通行密钥免口令登录（它一步完成）；不改依赖方侧（Resource 形态）。

## 2. 方案

- **注册**：会话认证注册处调用官方 `AddTwoFactorUserIdCookie`（共享框架 `Microsoft.AspNetCore.Identity`，无新包），再按会话 Cookie 的约定配置：部署环境 `__Host-` 前缀、Path=/、HttpOnly、Secure、SameSite=Lax（依赖方跨站顶层导航到授权端点时要带上）、`SlidingExpiration=false`；签入时 `IsPersistent=false`、`ExpiresUtc` = 挑战到期时刻。该方案不进 `Smart` 选择器与默认方案，只有第二步端点与授权端点显式认证它。
- **票据内容**：只放挑战引用（现有随机令牌）。挑战记录、次数、安全版本、凭据锁内重读与一次消费保持不变；Cookie 有效但记录不在即失败并删 Cookie。
- **端点**：`session-login` 与外部登录完成在要求第二步时签入临时 Cookie（先签出旧的），响应体返回方式、默认方式、打码邮箱，不再含令牌；新增按临时 Cookie 取第二步上下文的匿名读接口；第二步提交、发码、通行密钥选项改为从临时 Cookie 取挑战，入参去掉令牌。完成时先签出临时方案再签发会话。口令单步登录、免口令登录、退出也签出临时方案。另加取消接口（换账号、返回登录）：删挑战并签出临时方案，幂等。
- **授权端点**：无有效正式会话（或需要重新认证）时，先看临时身份：有效且未要求重新认证（或其签发晚于本请求重新认证证明的签发）→ 交互请求跳 `/auth/two-factor?returnUrl=<同一缓存请求>`，静默请求回 `login_required`；请求带了临时 Cookie 却认证失败（过期、篡改、记录失效）→ 签出临时方案后按原逻辑回登录页。正式会话路径、受限判定、重新认证证明不变。
- **期限**：见 §1 第 4 项。
- **前端**：第二步从登录页内嵌改为独立路由页 `/auth/two-factor`，从取上下文接口加载，不再依赖导航状态；登录、外部登录回调成功后带 `returnUrl` 导航过去。取上下文明确答"无待完成登录"时回登录页并保留合法 `returnUrl`；网络或服务故障就地报错可重试，不当作无待完成登录。完成后沿用 `ReturnUrlService` / `SessionContextService`：服务端授权地址整页跳转，站内目标先建立权限与设置。取消调用取消接口并保留 `returnUrl`。另一标签页已完成时，提交得到挑战到期，先刷新当前用户：已登录则直接回到 `returnUrl`。Mock 形态同步：上下文、取消、无令牌入参、期限与复用；刷新可续所需状态按 `coding-frontend.md` §7 既有 `sessionStorage` 约定保存，不模拟 HttpOnly。新增 `/api` 写端点沿用现有来源防护。
- **文档**：`template/docs/standards/auth.md` 本地账号、OIDC、外部登录各段按终局重写（第二步凭据经临时 Cookie、授权端点接回、期限规则），不保留旧说法。

## 3. 计划

1. 后端：注册与签入签出、挑战期限、端点入参与上下文接口、授权端点分支；集成测试按 §1 验收表。
2. 前端与 Mock：第二步路由页、登录与外部登录回调、API 契约；单测。
3. 文档与本规格 §4。
4. 验证：受影响形态生成后 build、lint、单测、集成测试（全功能、Identity、Standalone、关本地化、关邮箱；Resource 裁剪不受影响）；`template.json` 排除清单随增删文件核对；浏览器刷新 e2e；`scripts/check-all.ps1`。本地定向，全量矩阵交 CI。
5. 子 agent 审查后推送开 PR。

## 4. 结果

交付：PR #246（合并提交 `83fd719e`），只改模板，随下一次 develop→main 发布。按 §2 实施，框架无改动。

- 后端：`LocalSessionAuthenticationExtensions` 调官方 `AddTwoFactorUserIdCookie` 并按会话 Cookie 约定配置（部署名 `__Host-Http-CompanyName.ProjectName.TwoFactor`）；新增 Api 层 `PendingSignInCookie`（签入、读取、结束、第二步操作包装），方案名取 `IdentityConstants.TwoFactorUserIdScheme`。应用层不引用 ASP.NET Core，方案名常量因此放在 `PendingSignInCookie.Scheme`，`AuthenticationSchemeNames` 注释指向它。挑战记录加完整精度签发时刻，授权端点据此与重新认证证明比较（Cookie 票据的签发时间只到秒）。
- 端点：新增 `GET /api/v1/auth/two-factor`（上下文，出参 `TwoFactorLoginOutputDto`）与 `POST /api/v1/auth/two-factor/cancel`；删除 `TwoFactorEmailCodeInputDto`、`TwoFactorPasskeyOptionsInputDto`，`TwoFactorLoginInputDto.Token` 与 `SessionLoginOutputDto.TwoFactorToken` 去掉；新错误码 `Auth:EmailCodeResendTooLate`（400，不计数）。授权端点有效会话旁残留的临时 Cookie 一并结束；协议退出真正完成时（含没有正式会话的直接退出）也结束待完成登录，等待确认时不动它。"剩余不足 N 不新发"在发码存储的发送间隔锁内、复用判定之后才检查，间隔内沿用旧码不受它限制。
- 实施中确认的事实：异常处理中间件写错误响应前会清空响应头，第二步端点在挑战到期时的删除 Cookie 登记在 `OnStarting`；`/connect/userinfo` 无令牌时 OpenIddict 按协议答 `invalid_request`（400），验收表已按此改写。
- 前端：独立路由 `/auth/two-factor`（`TwoFactorLogin`），登录收尾抽到 `features/account/utils/login-completion.ts` 供登录页与第二步页共用；`ReturnUrlService.twoFactorLogin` 生成去向，第二步页自身不作为可接续目标。Mock 的待完成登录连同最近一封邮件挑战与它的发送间隔按 sessionStorage 约定保存，刷新后原码照样能用、间隔照样沿用。
- 测试：新增 `PendingSignInTests`（Cookie 属性、非正式身份、刷新、完成与重放、覆盖、取消、过期与篡改、并发两种方式、两个依赖方、静默、三种重新认证与证明挪用、会话与旧 Cookie 并存）、`TwoFactorMethodsTests` 期限用例、`TwoFactorRequirementTests` 首绑走完、`PasskeySignInTests` 免口令删过期 Cookie；前端第二步页、挑战组件、登录页、外部回调与 Mock 单测；e2e `two-factor-login.e2e.ts`（刷新后完成）。
- 验证：生成后 `scripts/verify.ps1` 全量通过 identity-all-features、identity、identity-tenant-management-without-history、Resource（全量）；standalone-capabilities-06 除 `distributed-events.e2e.ts` 一例事件状态超时（与本改动无关）外全部通过；standalone、identity-capabilities-08 生成后 lint、格式与类型检查通过。`scripts/check-all.ps1` 50 道闸门通过。
