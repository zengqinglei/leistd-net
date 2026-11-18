# 多租户分层：宿主控制面、租户工作区与平台人员

状态：已完成 · 风险档：高 · 关联：#282、#283（已合入 develop）；合并后修复 [#285](https://github.com/zengqinglei/leistd-net/pull/285) · 设计稿：[design.html](./2026-11-16-multitenancy-layering/design.html)

文中路径相对仓库根；`template/` 下的路径以模板源码为准，后端源码前缀 `template/backend/src/CompanyName.ProjectName.` 简写为 `<P>.`。

## 1. 需求

### 问题与背景

开启多租户的生成项目，常在同一套代码上同时交付 SaaS（多个客户共用部署）与私有化（一家客户独占部署）。核对 develop `9b0dac26` 后的问题：

| 编号 | 问题 | 性质 | 现状证据 |
| --- | --- | --- | --- |
| Q1 | 没写明宿主只做控制面；业务权限该选哪个侧别没有规则；私有化该选哪种形态没有指引 | 文档缺口 | `template/docs/standards/auth.md` 侧别表只按数据形态分 |
| Q2 | 模拟登录写死租户里用户名为 `admin` 的用户，改名或删除后失败 | 设计缺陷 | `<P>.Application/Tenants/AppServices/TenantImpersonationAppService.cs:84-89` |
| Q3 | 平台运营人员要长期在多个租户里工作：只能模拟成租户 admin（全权、非本人），一个会话只能在一个租户，换租户要整页跳转 | 场景缺口 | `frontend-navigation.md` §1；`isolation-and-authorization-scenarios.md` §1 已知边界 |
| Q4 | 多租户产品私有化只能关闭多租户，与 SaaS 走两套路径 | 场景缺口 | `IncludeMultiTenancy` 只有开和关 |

Q1、Q2 所有多租户项目都受影响；Q3、Q4 只出现在"平台方有运营团队常驻多个租户"的产品里。用户只属于一个租户、宿主人员用模拟登录排障，仍是多数通用项目的合理默认。

### 目标

- G1：写明三层划分与侧别规则，补私有化形态指引（全部项目）。
- G2：模拟登录不再依赖固定用户名（默认形态）。
- G3：新增可选形态"工作区"：平台人员用本人账号登录，经授予在每个租户有一条没有凭据的关联账号；会话只认本人，租户按请求选定；切换不重新登录、不整页刷新，多标签页各停各的租户；覆盖 Standalone、Identity 与 Resource（浏览器会话）及跨服务调用。
- G4：私有化可配置一个默认租户，首次启动即可用。

### 非目标

- 不做多服务授权编排（由 Identity 代各资源服务写入角色）。模板没有服务目录；资源服务按 [Identity 内置管理员资格](./2026-11-18-resource-admin-auto-bootstrap.md)在首次建立成员时引导 Admin。后续授权由资源服务本地管理。
- 不做客服代入（以租户里某个现有用户的身份操作）。
- 不改框架包：所需调整都是宿主组合与模板代码。
- 不改关闭多租户的形态。

### 术语

| 术语 | 定义 |
| --- | --- |
| 本人账号 | 宿主里的用户（`TenantId` 为空），平台人员用它登录 |
| 关联账号 | 平台人员在某个租户里的 `User` 行，`HomeUserId` 指向本人账号；不能持有任何凭据 |
| 访问索引 | 控制库里的 `UserTenantAccess(HomeUserId, TenantId, CreationTime)`，回答"本人能进哪些租户" |
| 工作区 | 一个请求作用的租户，由 `X-Workspace: <租户名>` 选定 |
| 工作区形态 | 生成参数 `TenantStaffAccess=Workspace` 生成的项目；默认形态是 `Impersonation` |

### 功能要求与验收标准

越权类集中在工作区认证方案一个入口，用真实 HTTP 与真实授权策略做矩阵；"一个入口"指一个测试类，类内参数化覆盖各条路径。

| 编号 | 验收标准（Given / When / Then） | 覆盖 |
| --- | --- | --- |
| AC1 | Given 关联账号；When 用口令、外部登录、通行密钥登录，或发起找回密码；Then 全部失败，与"没有该凭据"的普通账号同形；租户管理员为它重置口令、设邮箱、绑定外部登录或通行密钥时被领域拒绝 | 集成：一个测试类参数化各登录入口与管理动作，含普通账号对照 |
| AC2 | Given 宿主管理员获 acme 访问、关联账号角色 Admin；When 带 `X-Workspace: acme` 调普通权限端点、组件自用端点（`App.CurrentUser` 策略）与 `permissions/current`；Then 端点看到的主体是关联账号、租户是 acme，返回 acme 的数据与权限；共享库与独立库租户各一例 | 集成：入口矩阵 |
| AC3 | Given 未获授予的宿主用户、关联账号已停用、租户已停用，或带别家租户名的租户用户；When 带 `X-Workspace`；Then 403：租户用户越界为 `Workspace:Forbidden`，宿主用户的访问不可用（未授予、停用、租户停用）为 `Workspace:Unavailable`；都不回退为本人继续执行 | 集成：入口矩阵 |
| AC4 | Given 任意已登录用户；When 不带头；Then 行为与现在一致 | 集成：入口矩阵 |
| AC5 | Given 宿主管理员在工作区内；When 调本人账户、认证与协议端点；或所在工作区的租户被停用后再请求；Then 前者忽略头、作用于本人；后者只拒绝该工作区请求，本人会话不被注销，不带头的请求照常 | 集成：入口矩阵 |
| AC6 | Given 租户管理员（工作区形态下也在 `/w/<自己的租户>/...`）；When 看用户列表、改关联账号角色、停用它；Then 带「平台」标记；Standalone/Identity 下停用后平台人员下一个请求 403 | 集成 + 组件 |
| AC7 | Given 宿主有 `App.Tenants.StaffAccess`；When 授予、改角色、移除、再次授予；Then 关联账号与索引同步；移除后 Standalone/Identity 下一个请求 403、Resource 最迟 5 分钟；再次授予恢复原关联账号（同一 Id） | 集成 |
| AC8 | Given 平台人员在 acme 改了角色；When 看 acme 操作记录；Then 记录在 acme，操作人是关联账号，显示名同本人 | 集成 |
| AC9 | Given Identity + Resource（浏览器会话）组合；When 宿主管理员在 Resource 前端登录、拉工作区列表、进入 acme、触发一次现有的下游服务调用；Then Resource 首次访问 JIT 建成员，下游服务收到的主体是关联账号、租户是 acme | OIDC 端到端一个场景；令牌缓存到期与交换拒绝规则另由集成测试覆盖 |
| AC10 | Given 能进 acme 与 globex；When 同一浏览器两个标签页，一个从 `/w/acme/platform/users` 切到 globex，另一个停在 acme；冷启动直接打开 `/w/acme/...`；前进后退；Then 地址换段、组件重建、不整页刷新、不重新登录；两个标签页数据互不影响；路由守卫判定前权限已属于目标工作区 | 组件 + Playwright |
| AC11 | Given 关联账号在某工作区没有任何入口权限；When 进入或切到该工作区；Then 保留外壳，说明原因并给出去处 | 组件 + Playwright |
| AC12 | Given 空库、配置了 `MultiTenancy:DefaultTenant`；When API 首次启动、再次启动；Then 只建一次默认租户；工作区形态下宿主管理员得到 Admin 关联账号与索引，从宿主登录后直接落到默认工作区；已有租户时跳过 | 集成 + Playwright 一条落点路径 |
| AC13 | Given 默认形态；When 租户里内置 admin 被改名；Then 模拟登录仍选中该租户持有内置 Admin 角色的用户进入 | 集成 |
| AC14 | Given 工作区形态；When 调用原模拟登录端点；Then 404，界面无模拟入口，历史模拟记录照常显示 | 集成 + 组件 |
| AC15 | Given 工作区形态且开了实时或通知；When 平台人员进入工作区，再切换工作区；Then 本人连接常驻（不带 `workspace`，宿主通知照常到达）；工作区连接以关联账号握手，切换后旧连接停止、带新工作区重建，订阅状态不残留；租户用户只有一条连接 | 集成（握手）+ `signalr-service.spec.ts`（连接管理） |
| AC16 | Given 默认形态或关闭多租户；When 生成项目；Then 不出现工作区方案、`/w` 路由与平台人员页面，构建通过 | 模板生成检查 + 选择器场景构建 |
| AC17 | Given 工作区形态且目标工作区返回 `Workspace:Forbidden` 或 `Workspace:Unavailable`；When 冷启动或深链进入；Then 显示区分原因的「无法访问该工作区」说明页，地址保留原目标，按钮按上次 → 第一个 → `/platform` 前往可用工作区；应用内切换被拒仍恢复原处并提示，Unavailable 刷新目录后退出不可用工作区 | `workspace-guard.spec.ts`、`workspace-denied.spec.ts`、`startup-service.spec.ts`、`workspace-context-service.spec.ts`、Mock 拒绝回归 |
| AC18 | Given Resource 同一浏览器已以 A 登录并缓存工作区令牌；When 以 B 重新完成 OIDC 登录，再请求同一工作区；Then 本人和工作区主体均属于 B，不复用 A 的工作区令牌 | `ResourceBrowserSessionTests.Oidc_login_as_another_user_does_not_reuse_the_previous_workspace_subject`；缺少 subject 或票据键时跳过缓存另有同类参数化回归 |
| AC19 | Given Identity 工作区形态的已登录会话；When 直接打开或整页刷新 `/platform`、`/w/default-e2e/platform`；Then 仪表盘及区域切换器显示，导航完成并保留目标地址 | `workspaces.e2e.ts`；`identity-workspace` 登记 `Verify` 后纳入完整矩阵 |
| AC20 | Given 匿名用户从根地址进入；When 在公共首页点击登录并完成认证；Then 进入默认工作区，整条链只有首次打开首页的一次 Document 请求；直接访问根地址不执行受保护路由守卫 | `workspaces.e2e.ts`、`app.routes.spec.ts` |

### 非功能要求

- 只在工作区认证方案这一个入口校验工作区；端点与应用服务不逐个判断租户。
- 不为极少数并发窗口加复杂度：同键并发交换、授予与移除的中间态、撤销后的缓存窗口都接受，不加锁、补偿事件或清缓存事件。
- 撤销时效：Standalone/Identity 下一个请求生效；Resource 最迟一个工作区令牌有效期（5 分钟）。

### 已确认的决策

| 编号 | 决策 | 确认 |
| --- | --- | --- |
| D1 | 私有化按产品形态分开：开多租户的产品私有化为"单一默认租户"；只服务一家客户的产品关闭多租户 | 用户，2026-11-16 |
| D2 | 本人账号 + 关联账号（同 Entra B2B 访客账号），不改成全局用户 + 成员关系 | 用户，2026-11-16 |
| D3 | 平台人员进入租户做成生成参数 `TenantStaffAccess`：默认 `Impersonation`（保留模拟登录并修 Q2），可选 `Workspace`（替代模拟登录）；不做客服代入 | 用户，2026-11-16（取代当日早先"撤掉模拟登录"的决定） |
| D4 | 工作区形态一次做完，含 Resource 与跨服务调用，一个 PR 交付 | 用户，2026-11-16 |
| D5 | 界面采用设计稿方案 A：侧栏头部合一切换器 + 租户行「平台人员」抽屉 | 用户，2026-11-16 |
| D6 | 与下游已有实现不一致处按终局做法实现：路由统一 `/w/:workspace`、平台人员两条实时连接、区分 `Forbidden` 与 `Unavailable` 取下游做法；缓存键用服务端票据键与本人 subject（业务三仓已一致，无需对齐）、进入工作区令牌寿命取较小值、访问令牌不加 `sid`、节点缓存用 `IMemoryCache` 取本方案。下游同步模板时自行对齐，确需保留的差异由下游列出并说明理由 | 用户，2026-11-16 |

## 2. 方案

### 现状事实

- 租户解析由主体定案：已认证主体没有租户声明即为宿主，请求头改写不了已登录用户的租户（`framework/docs/components/multi-tenancy.md`"解析与校验"）。
- 权限按当前侧别判定，没有租户上下文即为 Host（`framework/components/authorization/Leistd.Authorization.Core/Checking/DefaultPermissionChecker.cs:33`），所以关闭多租户时 `Tenant` 侧权限永远不可用。
- 默认授权策略与 `App.CurrentUser` 显式指定认证方案（`<P>.Api/DependencyInjection.cs:63-107`），授权阶段会按指定方案重新认证并覆盖 `HttpContext.User`（`auth.md`"认证"一节）。Standalone 用 `SessionCookie`，Identity 与 Resource 浏览器会话用 `Smart` 策略方案分流 Bearer 与 Cookie。
- `AuthPrincipalFactory.CreateAsync` 调用 `User.GetAccessStatus`，不为 `Allowed` 返回 null；该判定同时用于已有会话与 Resource 成员状态，不是登录专用（`<P>.Application/Auth/SignIn/AuthPrincipalFactory.cs:74-85`）。
- `sid` 只进 ID 令牌（`AuthPrincipalFactory.cs:230-232`）；Resource 会话票据里有服务端票据键 `DistributedTicketStore.TicketKeyProperty`（`ResourceSessionRefresher.cs:46`）。
- 现有令牌交换（`<P>.Api/Controllers/ConnectController.cs:250-276`）是严格单跳：输入不能带 `act`，输出加 `act`，寿命不超过输入令牌；`CreateFromTokenAsync` 按令牌的租户与 `sub` 查会话（`AuthPrincipalFactory.cs:43-65`）。
- 宿主默认管理员由 API 启动时的 `SystemInitializer` 在初始化锁内创建（`<P>.Application/Initialization/SystemInitializer.cs`），`DbMigrator` 不建账号。
- 控制库 `IdentityControlDbContext` 承载租户注册表等宿主全局数据，固定连接。
- 前端只有 `/platform`（侧栏）与 `/workspace`（顶栏）两区，侧栏头部是区域切换器；没有 `canDeactivate` 路由守卫。

### 三层划分

| 层 | 承载 | 不承载 | 权限侧别 |
| --- | --- | --- | --- |
| 宿主（控制面） | 租户开通与连接、平台人员与授予、开放应用、进程级设置、跨库运维 | 业务数据、业务菜单 | `Host`；`Both` 的宿主部分 |
| 租户（工作区） | 全部业务数据与功能；本租户成员、角色、设置、操作记录 | 其他租户的数据 | 业务 `Tenant`；成员、角色等 `Both` |
| 平台人员（桥梁） | 一个本人账号，经授予进入多个租户 | 不代入租户里的现有用户 | — |

- 开多租户时业务权限默认 `Tenant`；关闭多租户时只有宿主作用域，业务权限用 `Both`。
- 两侧都有的页面是同一组件，数据范围由请求作用的工作区决定。

### 生成参数

新增选择参数 `TenantStaffAccess`，只在 `IncludeMultiTenancy=true` 时有效；一个系统里的 Identity 与各 Resource 取同一个值。

| 应用场景 | 生成参数 | 宿主人员怎么进租户 |
| --- | --- | --- |
| 单客户系统、内部系统 | `--IncludeMultiTenancy false` | 没有租户 |
| 多租户 SaaS，平台只管租户、偶尔排障 | 默认（`TenantStaffAccess=Impersonation`） | 模拟登录 |
| SaaS 与私有化同一套代码，平台团队常驻多个租户 | `--TenantStaffAccess Workspace` | 本人登录，按请求进入多个租户 |
| 上述任一的私有化部署 | 不加参数，部署配置 `MultiTenancy:DefaultTenant` | 首次启动建默认租户 |

计算符号（命名按 `template.json` 现有约定，实施时定稿）：`Impersonation` 增加条件 `TenantStaffAccess == "Impersonation"`；新增工作区符号 = `IncludeMultiTenancy && TenantStaffAccess == "Workspace" && (LocalIdentity || ResourceBrowserSession)`。纯 API 的 Resource 不需要该符号：调用方直接持有对应租户的令牌。模板矩阵为工作区形态登记 Standalone、Identity、Resource 浏览器会话三个代表场景。

### 默认形态：修模拟登录

模拟目标改为"该租户中持有内置 Admin 角色、已启用的用户，按创建时间取第一个"，不再按用户名查找；找不到时报现有错误码。

### 工作区形态：身份

- 关联账号是租户内的普通 `User` 行，多一个可空 `HomeUserId`；用户名 `platform.<本人用户名>`，邮箱用空串（现有唯一索引已排除空串）。与租户里已有用户名冲突时授予失败并提示。
- **凭据约束放在领域**：`User` 聚合对关联账号拒绝设置口令、邮箱、外部登录与通行密钥。这样关联账号没有任何可登录的凭据，各登录入口与找回密码天然失败，不必修改 `GetAccessStatus`；`GetAccessStatus` 仍只表示"能否作为成员操作"，关联账号按 `IsActive` 正常判定，主体工厂可以直接复用。
- **访问索引**放在控制库（与租户注册表同库、固定连接），宿主全局，不实现 `IMultiTenant`，不映射进租户上下文。
- 授予：先在租户侧建或恢复关联账号并设角色，再写索引。移除：先删索引，再软删除关联账号。再次授予时恢复已软删除的关联账号（同一 Id，历史记录连续）。中间失败留下的孤儿行不影响正确性：入口同时要求索引存在、关联账号存在且启用。
- 认证、两步验证、确认身份与会话策略都按本人账号所在的宿主。

### 工作区形态：认证入口

- 新增 `Workspace` 认证方案，作为默认方案，并替换默认授权策略、`App.CurrentUser` 及 Hub 所用自然人策略中的方案；它内部先认证原来的方案（Identity/Resource 为 `Smart`，Standalone 为 `SessionCookie`）。机器策略与协议端点显式指定的方案保持不变。
- 处理规则：

| 请求 | 结果 |
| --- | --- |
| 不带头；或账户、认证、协议端点（`/api/v1/auth/**`、`/api/v1/external-auth/**`、`/connect/**`） | 本人主体 |
| 租户用户带自己的租户名 | 本人主体 |
| 租户用户带别家租户名 | 403 `Workspace:Forbidden` |
| 宿主用户带头，索引存在、租户启用、关联账号启用 | 本请求主体换成关联账号 |
| 宿主用户带头，任一条件不满足 | 403 `Workspace:Unavailable`（前端据此刷新工作区列表并退到下一个可用工作区） |

- 403 由方案自己的禁止响应给出 ProblemDetails，不走 401，也不回退为本人。
- 关联主体的声明：`sub` = 关联账号，`tenant_id` = 目标租户，`home_sub` = 本人；沿用本人的 `auth_time`、`amr`。
- 每个带头的请求，方案都先把租户名解析成启用的租户 Id（本地身份经 `ITenantStore`，Resource 经工作区专用的 Identity 查询端口），未启用直接 403（Resource 也一样，不因令牌已缓存而跳过）。这样框架的租户会话自恢复（`UseTenantSessionRecovery`：主体所属租户失效时注销会话并下发 `X-Tenant-Invalid`）不会因工作区请求注销本人会话，框架不改。两步验证"受限会话"按本人账号判定。
- SignalR 浏览器连接带不了自定义头，协商与连接用查询参数 `workspace`，由同一方案读取。平台人员在工作区内有两条连接：本人连接常驻（不带参数，宿主通知与未读数照常），工作区连接随切换重建（工作区实时订阅与关联账号的通知）；租户用户与在控制面时只有一条。
- 工作区列表 `GET /api/v1/auth/me/workspaces`：只按本人返回，忽略工作区。Identity/Standalone 读访问索引；Resource 经应用层端口与 ServiceClient 适配回源 Identity 的同一端点：沿用账户偏好客户端的接法，用本人令牌做现有令牌交换取得 Identity 受众的令牌再请求（不经 `Workspace` 方案，避免循环），保持本人范围。

### 工作区形态：主体建立与令牌交换

一处判定，三处复用：在 `AuthPrincipalFactory` 增加"按本人进入工作区建主体"：校验本人账号可用、索引存在、租户启用、关联账号启用，然后在目标租户上下文与新工作单元内用现有字段组装逻辑建关联主体并带 `home_sub`；`email_verified` 等字段按关联账号自己的状态投影，不复制本人的。

会话语义沿用现有边界，不新增会话标识：进程内路径由内层 Cookie 方案校验本人的具体会话；令牌交换路径只校验已验证的输入令牌、本人账号、访问索引、租户与关联账号，不按 `home_sub` 校验具体会话；会话绑定客户端的授权码与刷新令牌仍由现有链路按 `sid` 校验，工作区与下游令牌受输入令牌到期上限约束。所以本人会话结束后，已签发未过期的访问令牌仍可交换到其到期为止，会话结束阻止的是后续刷新。

| 用途 | 输入 | 输出 |
| --- | --- | --- |
| Standalone/Identity 进程内 | 本人 Cookie 主体 + 目标租户 | 本请求的关联主体 |
| 进入工作区交换（新分支，Resource 用） | 本人访问令牌 + 目标租户（`/connect/token` 的令牌交换，附加参数 `workspace`） | 访问令牌：`sub` = 关联账号、`tenant_id`、`home_sub`，受众同输入令牌，不加 `act`，不带刷新令牌，寿命取 5 分钟与输入令牌剩余寿命的较小值；拒绝时 `invalid_target` |
| 现有下游交换遇到工作区令牌 | 带 `home_sub` 的令牌 | 按上面的判定重建关联主体（本人账号按 `home_sub` 在宿主校验），其余规则不变：照常加 `act`、单跳、受众与资源归属检查 |

"进入工作区"是本人选择范围，不消耗单跳；之后 Resource 调下游仍有一跳可用。

### 工作区形态：Resource 浏览器会话

ASP.NET Core 的 Cookie 处理器在已带有效 Cookie 的新登录中沿用服务端票据键；同请求签出再签入也不清空内部键，这是 [dotnet/aspnetcore#47503](https://github.com/dotnet/aspnetcore/issues/47503) 的已知行为。部署环境的会话 Cookie 使用 `__Host-Http-` 前缀，浏览器只接受同源 HTTPS 响应设置，第三方不能通过 Domain 或脚本植入该 Cookie，会话固定的利用前提不成立，因此不另做键轮换或处理器扩展；消费票据键的身份缓存必须自行区分主体。

- `Workspace` 方案对宿主用户带头的请求，按（Workspace, 服务端票据键, 本人主体 sub, 租户）在节点内缓存工作区令牌（`IMemoryCache`，到期时间按交换结果在条目内设置；同键并发偶尔重复交换可以接受）；缓存期限取 4 分钟与令牌实际剩余寿命的较小值，命中时不返回已过期令牌。缺失时调用进入工作区交换；缺少票据键或 sub 时不读写缓存。
- 验证工作区令牌复用本服务现有的访问令牌验证配置，结果票据保存 `access_token` 与到期时间，以便现有令牌访问器与 Hub 到期行为照常工作。
- 跨服务调用的令牌访问器改为读 `Workspace` 方案：有工作区时是工作区令牌，否则是本人令牌。
- 关联账号首次访问 Resource 时建立租户成员，同时按 Identity 内置 Admin 角色资格引导 Admin。其余成员没有本地角色时进入零权限外壳。

### 工作区形态：前端

- 路由：工作区形态下 `/platform/**` 只表示宿主控制面，所有租户内页面（含租户用户自己的）都在 `/w/:workspace/**` 下，复用现有子路由（`/w/acme/platform/users`、`/w/acme/workspace/dashboard`）。租户用户进入的只能是自己的工作区，打开 `/platform/**` 时转到 `/w/<自己的租户>/...`。地址的含义因此只有一种，链接在租户管理员与平台人员之间可以互相分享。默认形态与关闭多租户的路由不变。
- 请求范围用 `HttpContextToken`：默认取当前路由的工作区；`/platform` 与本人账户请求标成本人。
- 状态：目标工作区在导航开始时确立，路由守卫判定前加载该工作区的权限与设置；切换完成时提交，旧工作区的迟到响应丢弃（复用 `SessionContextService` 与 SignalR 的代次）。`AuthService.currentUser` 只表示本人；业务订阅、本人行判断与租户资源键取工作区主体。
- 切换只做路由跳转；`/w/:workspace` 父组件按工作区键重建子路由出口，各页走首屏骨架。不新增离页确认。
- 登录后落点：上次的工作区（`localStorage` 只存"上次"，不作为当前状态），失效取第一个，没有时 `/platform`。
- 菜单按路由所在区选择，只按权限裁剪；零权限时保留外壳并说明原因。
- 冷启动或深链的工作区 403 不进入全局启动失败卡片：以 `hlmEmpty` 显示「无法访问该工作区」，Forbidden 说明账号属于其他工作区，Unavailable 说明工作区或访问资格不可用；保留原目标地址，主按钮「前往我的工作区」共用登录后落点规则。应用内切换被拒恢复原范围并提示，Unavailable 刷新列表后退到下一个可用工作区。
- 实时连接：平台人员在工作区内保持本人与工作区两条连接，通知中心按当前范围显示；其他情况一条。

### 工作区形态：授予管理

- 新权限 `App.Tenants.StaffAccess`，`Host` 侧，挂在 `App.Tenants` 下。
- 端点：列出某租户的平台人员、授予（本人账号 + 租户角色）、改角色、移除；可选角色从目标租户读取。
- 租户侧用户列表列出关联账号并带「平台」标记，租户管理员可改角色、停用；重置口令、改邮箱、删除等由领域拒绝，界面同时隐藏。
- 界面方案见设计稿第 3 节。

### 私有化：默认租户

- 只在拥有租户控制库与本地身份的 Standalone/Identity 执行；Resource 照常回源。
- 配置 `MultiTenancy:DefaultTenant`（`Name`、`DisplayName`）。API 启动时 `SystemInitializer` 在初始化锁内、默认管理员就绪之后执行：库中没有任何租户时建立默认租户（沿用现有租户创建与种子）；工作区形态下再给默认宿主管理员建 Admin 关联账号与索引，种子不另建带口令的本地 admin。播种接口按形态适配：默认形态沿用现有初始管理员凭据输入；工作区形态只复用角色与权限播种再建关联账号，不先建带口令的 admin 再清掉口令。已有租户时跳过。
- 登录页不预选默认租户：认证账号所在的租户与登录后落入的工作区分开。宿主本人登录后落到默认工作区；默认租户的普通用户照现有方式选租户或按域名解析登录。
- 不加部署形态开关。

### 备选与取舍

| 备选 | 不选的原因 |
| --- | --- |
| 工作区形态设为默认 | 多数通用项目不需要；会让每个多租户项目承担工作区与跨服务交换的复杂度 |
| 全局用户 + 成员关系 | 模板用户按租户隔离，独立库租户的用户在自己的库里，要重做隔离 |
| 会话里记"当前租户"，切换时换 Cookie | 一个会话只能停在一个租户，多标签页互相影响 |
| 只有平台人员走 `/w/:workspace`、租户用户沿用原路径 | 同一地址 `/platform/users` 对宿主与租户含义不同，链接不能互相分享，还要两套挂载；工作区形态本就是新生成形态，统一走 `/w` 没有存量深链负担 |
| 平台人员在工作区内只保持一条实时连接 | 进入工作区期间收不到宿主通知 |
| 进入工作区令牌寿命固定 5 分钟 | 派生令牌会比本人令牌活得久；取两者较小值 |
| 访问令牌加入 `sid` 以区分 Resource 会话 | 改变现有令牌契约；Resource 用自己的服务端票据键与本人 subject 区分本地会话和用户 |
| 在各登录入口判定"关联账号不可登录" | 入口多、易漏，还会与复用主体工厂冲突；领域禁止关联账号持有凭据，一处即可 |
| 默认租户放在 `DbMigrator` | `DbMigrator` 没有账号初始化链；放在已持有初始化锁、默认管理员已就绪的 `SystemInitializer` |
| 分两期交付 Resource | 用户决定一次做完；跨服务语义与单服务共用一处主体判定，一起设计更一致 |

### 兼容与回滚

- 默认形态：只改模拟目标的选择方式，无数据变更。
- 工作区形态：新增迁移（`User.HomeUserId`、控制库 `UserTenantAccess`）；不含模拟登录。提交脚注写明新参数与形态差异。
- 回滚：回退 PR；新列与新表没有被其他数据依赖。

## 3. 计划

分两个 PR 依次交付（用户 2026-11-16 决定）：

| PR | 范围 | 合入条件 |
| --- | --- | --- |
| PR1 必做层 | 分层与侧别规则、私有化形态指引（只写已成立的事实，不提尚未实现的工作区形态）；WP2 模拟登录修复；本规格与设计稿 | 端到端闭环后合入 develop |
| PR2 工作区形态 | WP1 中参数、矩阵与工作区相关文档，WP3–WP7 | 在 PR1 合入后的 develop 上一次实施并闭环 |

每个 PR 内工作包在同一分支按依赖顺序实施，每个工作包只跑 L0（受影响项目构建 + 目标测试类或 spec），完成后提交一次；全部完成后集中跑一轮，审查修复后只重跑受影响项。

| 工作包 | 产物 | L0 |
| --- | --- | --- |
| WP1 规范与参数 | `isolation-and-authorization-scenarios.md` §1；`auth.md` 侧别规则与工作区一节；`frontend-navigation.md`、`frontend-routing.md`；部署文档私有化指引；`template.json` 参数与计算符号；矩阵代表场景 | 文档闸门；选择器场景 `-GenerateOnly` |
| WP2 默认形态修复 | 模拟目标按内置 Admin 角色选择 | 目标集成测试（AC13） |
| WP3 身份模型与授予 | `HomeUserId`、领域凭据约束、控制库索引、迁移；授予管理服务、端点、权限；租户侧限制 | 目标集成测试（AC1、AC6、AC7） |
| WP4 认证入口 | `Workspace` 方案与策略替换；主体判定；SignalR 参数；工作区列表 | 入口矩阵测试类（AC2–AC5、AC8、AC15 握手） |
| WP5 令牌交换与 Resource | 进入工作区交换分支；下游交换识别 `home_sub`；Resource 方案、缓存、令牌访问器、列表回源；Resource 增加工作区专用租户查询回源端口，沿用 Identity 客户端配置，不注册框架 `ITenantStore` | 目标集成测试（缓存期限、拒绝规则、列表回源；会话绑定与非绑定客户端各一次进入与下游交换，用同一用户两个会话的现有夹具核对会话语义） |
| WP6 前端 | `/w/:workspace`（含租户用户）、请求范围、状态与守卫顺序、切换器、授予界面、「平台」标记、零权限外壳、本人与工作区两条实时连接；Mock 覆盖；按 [frontend-ui.md](./2026-11-16-multitenancy-layering/frontend-ui.md) 实施 | 目标 spec + lint；Mock 站点走查 |
| WP7 默认租户 | `SystemInitializer` 默认租户与关联账号 | 目标集成测试（AC12） |
| 集中验证 | 见下表 | 结果写进 §4 |

集中验证（重负载在 worker-01）：

| 生成项目 | 跑什么 | 证明 |
| --- | --- | --- |
| Standalone + `Workspace` | 后端单元与集成测试项目全量、前端全量 spec、Playwright（切换与双标签页、授予、零权限、默认租户落点） | AC1–AC8、AC10–AC12、AC14、AC15 |
| Identity + `Workspace` | 构建与目标集成测试（交换分支、工作区列表） | AC9 的 Identity 侧 |
| Resource 浏览器会话 + `Workspace` | 构建与目标集成测试；与上一项组成 OIDC 端到端一个场景，含一次真实下游调用 | AC9 |
| 默认形态（Standalone、Identity） | 现有模拟登录测试 + AC13 | AC13 |
| 关闭多租户及其他条件形态 | 选择器场景生成与构建；其余由 PR CI `core` 档与合入后 `full` 档承担 | AC16 |

不做的事：见 §1 非目标。

## 4. 结果

PR1 交付范围：三层职责、业务权限侧别与私有化形态选择指引；模拟登录按本租户内置 Admin 角色选择最早创建的已启用成员，支持管理员改名；同步 Mock、错误默认文案、注释与恢复指引。AC13 由 `ImpersonationTests.Host_can_impersonate_a_renamed_tenant_administrator` 回归覆盖，角色、启用状态与创建时间选择由同类目标测试覆盖。

PR1 定向验证与 Claude 审查通过，已通过 #282 合入 develop。Standalone 生成项目的后端构建、目标集成回归、Mock spec、前端 lint 与后端风格检查通过；选择器给出的代表场景生成、条件裁剪与文档检查通过。

PR2 已实现 WP1、WP3–WP7：新增有效的工作区形态与场景；宿主本人身份、无凭据关联成员和控制面授予；统一 Workspace 认证入口；Identity 交换、Resource 浏览器会话与专用目录回源；方案 A 前端、Mock 和默认租户初始化。框架包未改动。Claude 第 1 轮 UI1–UI6、F1–F9、R1–R4、B1 及 R2 补充清单已修复，审查已随 #283 收口并合入 develop。

集中验证结果（生成项目均使用候选框架包，真实数据库为 PostgreSQL）：

| 范围 | 结果 |
| --- | --- |
| Standalone 工作区后端 | 构建 0 警告；单测 297/297、全量集成 722/722。审查修改后只补受影响的 24 项集成与风格检查，全部通过 |
| Standalone 工作区前端 | 全量执行 159 个 spec、1802 项用例；两组夹具问题修正后仅重跑受影响 2 个 spec，80/80 通过。审查目标 9 个 spec、161/161，生产构建与 lint 通过；Playwright 3/3，覆盖切换、双标签页、授予、零权限和默认租户落点 |
| Identity 与 Resource 工作区 | 两种后端构建与风格检查通过；Identity 目标 15/15，Resource 44/44。真实 OIDC 场景通过，覆盖浏览器 Cookie、工作区列表与主体、JIT 成员写入、票据隔离、短输入令牌寿命上限及一次真实下游调用 |
| 默认形态 | Standalone 10/10、Identity 12/12，含既有模拟登录、管理员改名和默认租户凭据；默认 Standalone 前端构建与 lint 通过 |
| 条件形态 | 6 个场景 GenerateOnly 及完整源码预检通过（含空文件断言）；最小工作区的前后端构建与 lint、关闭多租户的 Resource 构建通过 |

首次集中验证发现的失败均定向修复和复验，未重复已通过的全量项目。后端风格的冗余限定名、条件裁剪后的前端排版及测试夹具问题均已收尾。OpenIddict 的访问令牌寿命通过原生 AccessTokenLifetime 约束，已用 90 秒输入的集成测试与真实 OIDC 复验。

最终快照的 Mock 覆盖、全仓 i18n 和模板包验证通过；模板包内容、隔离安装、来源标记及重建确定性均通过。30 个登记场景覆盖全部可达条件行和有效能力两两组合的静态检查通过，该检查未执行完整场景矩阵。部署结构自测 43 项通过。

常驻事实同步到 `docs/architecture/isolation-and-authorization-scenarios.md`、`template/docs/standards/auth.md`、`template/docs/deploy/README.md` 与 `template/backend/README.md`。方案 A 的五组 1440/390 截图已就绪，F9 对应用户列表截图已更新。

worker-01 不可达，按个人执行偏好回退到受管 local；未运行本地 core/full 档矩阵，后续 PR CI 与合入后矩阵由 Claude 负责。执行证据与逐项提交记录保存在实施台账。PR2 已通过 #283 合入 develop；合并后修复见下节。

### 合并后修复

PR #283 已合入 develop `108341f7`。[修复 PR #285](https://github.com/zengqinglei/leistd-net/pull/285) 收口三项：Mock 条件成员合并导入；Resource 工作区令牌缓存按票据键、本人 subject 与租户隔离，防止同浏览器重新 OIDC 登录后命中前一用户的工作区令牌；工作区深链拒绝使用专用说明页。Identity 与 Resource 的签出尝试及相关夹具调整均已撤回，不改变现有会话键语义。规格与常驻认证、路由文档同步。

D6 下游对齐清单：业务三仓工作区令牌缓存键已包含 subject，与修复后的模板一致，无需对齐。不发送通知。

本 PR 的验证结论：三种实际生成形态（Identity、Resource、关闭本地化的 Standalone 工作区）的前端 lint、生产构建与受影响 spec 通过；专用拒绝说明页的两种原因、原地址与返回落点经有头 Mock 浏览器验证，桌面与手机排版通过。Resource 后端构建、受影响浏览器会话回归与风格检查通过；去掉缓存键中的 subject 时换号回归失败，恢复后通过，缺少或空票据键/sub 时不缓存的参数化回归通过。测试签发器的 A/B 邮箱冲突修正后，只补验失败用例，没有重跑无关全量矩阵。

票据键消费方排查：`ResourceSessionRefresher` 用键串行化并重读受 Cookie 引用版本校验的当前票据，重试缓存只保存时间；`ExternalAuthController` 用键锁定并一次消费外部票据，另校验意图、发起人和租户。两者都没有跨用户主体缓存，保持原实现。常驻事实同步到 `template/docs/standards/auth.md` 与 `template/docs/standards/frontend-routing.md`。

追加修复：CI run `43827458347` 的五个失败场景均在 Prettier 检查处失败。`startup-service.spec.ts` 的工作区条件块裁掉后留下连续空行，调整空行在条件块内的位置，使两侧生成结果都符合格式要求。`identity-workspace` 登记 `Verify`，补上此前没有随矩阵执行的 Workspace E2E，以及宿主和工作区平台的冷启动、刷新覆盖。

Chrome 157 在 nip.io 演示地址按「根地址 → 登录」复现白屏，窗口可见且 rAF 正常。平台的空路径父路由误匹配公共首页：首次进入被守卫送到 `returnUrl=/`，登录后返回根地址时落在没有组件的路由分支。该复现的导航已经结束，没有挂起的 Promise；直接打开受保护平台地址的对照正常。父路由改为消费 `platform`，子路由用空路径保留原布局、懒加载与权限守卫；公共首页不再被平台守卫截获。独立生成项目的根地址登录回归在修复前失败，修复后验证公共首页与默认工作区落点。

nip.io 的主机解析为宿主，登录后本人接口正常，未观察到 Cookie 或签发方异常。代理日志显示先登录成功，随后根地址被刷新，接着同一秒出现四次登录页的 Document GET，全部为 200，没有服务端重定向循环。结合用户确认的反复刷新操作，按手动刷新处理：根地址的错误匹配会把刷新后的页面再次送到登录页。修复后的根地址登录链由 E2E 断言只出现首次首页 Document 请求；未复现应用自行连续整页重载，不另加生产跳转逻辑。
