# 资源服务按 Identity 内置管理员自动引导 Admin

状态：已完成 · 风险档：高 · 关联：[PR #294](https://github.com/zengqinglei/leistd-net/pull/294)

## 1. 需求

### 问题

Identity 在迁移时建出宿主管理员，开通租户时建出租户管理员。Resource 服务的迁移只建 Admin 角色，不建人：用户首次访问时自动建出的本地成员不带角色，进去只看到零权限外壳。要让管理员在 Resource 里可用，部署方必须在每个 Resource 上执行 `DbMigrator --grant-admin <sub> [--tenant <id>] --apply`，涉及以下几处：

- 每个环境初始化时：宿主管理员。
- 每开通一个租户时：该租户的管理员。
- 私有化部署的默认租户：其管理员。

开通租户是例行业务操作，不应依赖运维逐个服务执行命令。

### 目标

- Identity 认定的内置管理员第一次进入某个 Resource 时，自动成为该 Resource 同一作用域的 Admin。
- 集群初始化和开通租户都不再需要 `--grant-admin`。
- `--grant-admin` 只保留两种用途：
  - 指定一个不是 Identity 管理员的人当首位管理员；
  - `--restore` 恢复。

### 非目标

- **后续变化不同步。**
  - Identity 撤掉某人的管理员身份，不连带撤掉他在 Resource 的 Admin；
  - 某人在 Resource 已有成员行之后才成为 Identity 管理员，也不补。
  - 之后由 Resource 本地管理，这与 Grafana `skip_org_role_sync` 的语义一致。
- **不回填。** 已经存在的零权限成员不处理，仍走界面分配或 `--grant-admin`。
- **只映射内置 Admin。** 其他角色不映射：Identity 和各 Resource 的角色是两套目录。
- **不改令牌内容。** 不用令牌或 userinfo 里的角色做判断。
- **不做推送或事件式开通。**

### 已确认的决策

- **用户，2026-11-18：** 保留部署命令对停用成员的既有行为和已有成员的资料投影。仅对数据库唯一约束冲突立即重试一次。
- **用户，2026-11-18：** 减少运维介入。宿主管理员和租户管理员都按 Identity 内置 Admin 自动引导一次。
- **用户，2026-11-18：** 建成员时向 Identity 查询，不读令牌声明；只引导一次，之后由 Resource 本地管理。
- **用户，2026-11-18：** 新增一个机器只读接口，写法沿用 `InternalAccountPreferenceController`，不扩展现有接口。

### 验收标准

| 编号 | Given / When / Then | 覆盖 |
| --- | --- | --- |
| AC1 宿主 | Given 某人是 Identity 宿主内置超管或宿主内置 Admin 成员，在 Resource 没有成员行；When 他第一次以宿主身份访问 Resource；Then 建出本地成员并加入宿主 Admin。Admin 角色不存在时先建并播种宿主权限。留下成功操作记录，授权依据为 `IdentityAdministrator` | Resource 集成测试 |
| AC2 租户 | Given 某人是租户 T 的内置 Admin 成员；When 他第一次在 T 访问 Resource；Then 成为 T 的 Resource Admin | Resource 集成测试（多租户） |
| AC3 普通用户 | Given 某人在 Identity 不是内置管理员；When 首次访问；Then 建成员、不带角色，与现状一致 | Resource 集成测试 |
| AC4 交换令牌首次接触 | Given 管理员第一次接触本服务用的是交换令牌（不带角色声明）；When 投影建成员；Then 同样引导为 Admin | Resource 集成测试 |
| AC5 只引导一次 | Given 成员行已存在：被移出 Admin、被停用、被删除，或者已是 Admin；When 再次访问；Then 不调用 Identity，保留本地授权、启停与删除状态。已有可见成员的资料照常按令牌刷新 | Resource 集成测试 |
| AC6 Identity 暂时不可用 | Given Identity 超时或返回 5xx；When 首次访问；Then 不建成员行，本次请求按现有逻辑拒绝（403 `User:LocalMemberMissing`）；Identity 恢复后，下次请求完成建成员和引导 | Resource 集成测试 |
| AC7 未授予 scope | Given Identity 以 401 或 403 拒绝查询（升级时漏配 scope）；When 首次访问；Then 记 Error 日志，按现状建成员、不带角色，不影响用户访问 | Resource 集成测试 |
| AC8 并发首次访问 | Given 同一主体并行发出两个首次请求；When 投影；Then 只有一条成员行和一条 Admin 成员关系，用户请求不报错 | Resource 集成测试 |
| AC9 Identity 接口 | 查询端点只接受带新 scope 的机器主体，用户令牌被拒。判定结果：<br>- 宿主超管、宿主内置 Admin 成员为 true，其他宿主用户为 false；<br>- 租户内置 Admin 成员为 true，其他租户用户为 false；<br>- 已停用或已删除的用户为 false，不存在的用户为 false；<br>- 不存在或已停用的租户返回 404 | Identity 集成测试 |
| AC10 工作区关联账号 | Given 工作区形态下，平台人员获准进入租户 T 并选了 Admin 角色；When 他第一次进入 T 的 Resource；Then 关联账号成为 T 的 Resource Admin | Resource 集成测试（工作区场景） |
| AC11 原命令不变 | `--grant-admin`、`--grant-admin --restore` 的行为和输出不变 | 现有 `ResourceAdminBootstrapTests` |
| AC12 真实环境 | 在 Identity 新建一个租户，其管理员登录后直接进入 Resource，即有 Admin 权限，全程不执行命令；宿主管理员同样 | 人工验收（worker-01 工作区演示站） |

## 2. 方案

### 现状事实

- **Identity 自动建管理员：**
  - 宿主由 `SystemInitializer` 按 `DefaultAdmin:Password` 建出（`template/backend/src/CompanyName.ProjectName.Application/Initialization/SystemInitializer.cs`，`#if (LocalIdentity)` 分支）；
  - 租户由 `TenantSeeder` 在开通时建出；
  - 内置 Admin 角色名固定为 `AdminConstant.RoleName = "Admin"`，`IsStatic`，不能改名、不能删除（`Domain/Users/Entities/Role.cs`）。
- **Resource 迁移只建角色，不建人：** 非 `LocalIdentity` 分支只播种 Admin 权限。首位管理员由 `DbMigrator/Runners/ResourceAdminBootstrapRunner.cs` 引导，普通引导的规则如下：
  - 本地没有成员时先建只含主体标识的最小行；
  - 已在 Admin 中时不做任何事；
  - 曾被移出 Admin 时不加回，软删除用户拒绝操作并提示恢复。停用用户仍可获授 Admin，保留停用状态。
- **Resource 首次访问建成员：** 由 `Api/Middlewares/ResourceUserProvisioningMiddleware.cs` 调用 `UserAppService.EnsureCurrentUserProjectedAsync` 完成，不带角色。投影失败时记 Warning 并放行，没有成员行的请求由授权拒绝。
- **Resource 判权只看本地角色：** `Application/Permissions/Provider/PermissionSubjectProvider.cs` 只读本地库里的角色关系，不读令牌声明。
- **令牌和 userinfo 都不能作为判断依据：**
  - 令牌交换会去掉 `role`（`Api/Controllers/ConnectController.cs` 令牌交换分支）；
  - userinfo 需要用户本人带 `openid`、`roles` 的令牌，返回的角色就取自令牌；
  - 下游三仓已经从访问令牌中去掉 `role`。
- **可以参照的机器接口：**
  - 端点 `api/v1/internal/account-preferences/{tenant}/{userId}`（`Api/Controllers/InternalAccountPreferenceController.cs`），scope 和策略分别在 `Application/Settings/Constants/AccountPreferenceScopes.cs`、`AccountPreferencePolicies.cs`；
  - Resource 侧客户端为 `Infrastructure/AccountPreferences/IIdentityAccountPreferencesReadApi.cs`；
  - 机器 scope 在 `Application/Auth/OAuth/OAuthScopes.cs` 登记；
  - Resource 请求的 scope 默认值在 `Api/appsettings.json` 的 `Leistd:ServiceClients:Identity:Scope`。

### 设计

**Identity：新增内置管理员查询接口**

- 端点 `GET api/v1/internal/administrators/{tenant}/{userId}`：
  - `tenant` 为 `host` 或租户 Id，返回 `{ "isAdministrator": bool }`；
  - 策略只认机器主体和新 scope `administrators.read`（`MachineOnly`）。
- 判定口径：
  - 用户须存在、未删除、访问状态为 `Allowed`；
  - 宿主侧：超管，或宿主内置 Admin 角色成员；
  - 租户侧：该租户内置 Admin 角色成员。
  - 不存在的用户返回 false；租户不存在或不可用返回 404。
- 条件：`OpenIddictServer`，与账户偏好接口一致。

**Resource：建成员时引导**

- 把 `ResourceAdminBootstrapRunner` 的普通引导抽成应用层服务，供 DbMigrator 和成员投影共用：
  - 包括初始化 Admin 角色、建最小成员行、加入 Admin、写操作记录；
  - `--restore` 仍留在 Runner 里；
  - 部署命令沿用 `DeploymentBootstrap` 依据，自动引导用新依据 `IdentityAdministrator`。
  - 操作记录的动作码沿用现有的 Admin 授予动作码，操作者取当前主体；具体以现有记录器约定为准，在 PR 中说明。
- 投影**仅在本地没有任何成员行时**（含软删除的行）调用 Identity 接口，再按结果在同一个事务里建成员并引导。只有新建成员时才调用，稳态请求没有额外开销。
- 失败处理：
  - 超时、5xx 等瞬态错误：抛出，不建成员，沿用投影现有的失败放行逻辑，下次请求重试；
  - 401、403：记 Error 并按现状建成员（不带角色），避免升级漏配 scope 时所有新用户被挡在门外。
- 并发：投影只在数据库唯一约束冲突时重试一次。重试时读到已有成员行，不再调用 Identity，也不再引导。
- 默认 scope 在 `Leistd:ServiceClients:Identity:Scope` 中追加 `administrators.read`，部署 compose 中 Resource API 的 scope 同步更新。DbMigrator 不需要这个 scope。
- 条件：`RemoteTokenAuth`；多租户下作用域取当前租户。

### 备选与取舍

| 备选 | 结论 |
| --- | --- |
| 读令牌里的 `role` 声明 | 不采用。交换令牌不带角色，经转调首次接触会漏掉；下游已从令牌中去掉 `role`；令牌承载授权信息与"令牌只证明身份"的原则冲突 |
| OIDC userinfo | 不采用。要用户本人带 `openid`、`roles` 的令牌，角色取自令牌，与上一行同样的问题 |
| 扩展账户偏好接口 | 不采用。只该读偏好的调用方会读到授权信息；ETag 缓存语义也不同 |
| 每次登录按 Identity 同步 | 不采用。需要给成员关系记录来源，Resource 也无法在本地管住 Admin |
| 开通时推送或事件通知 | 不采用。要求启用消息设施，覆盖不到既有租户、后加的服务和宿主侧 |

### 兼容与升级

- 部署顺序：先升级 Identity（新接口），并在 Identity 的开放应用中给 Resource 的机器客户端加上 `administrators.read` 权限，再升级 Resource。Resource 默认会请求这个 scope，Identity 没有授权时，OpenIddict 会拒绝整个机器令牌请求，部署文档要写明这个顺序。每个环境只做一次。
- 已有成员行的人不受影响；`--grant-admin` 和 `--restore` 不变。
- 回滚：回退 Resource 版本即恢复现状，Identity 的新接口和 scope 可以保留。

## 3. 计划

| 任务 | 产物 | 验证 |
| --- | --- | --- |
| Identity 查询接口 | scope 和策略常量、`OAuthScopes` 登记、`InternalAdministratorController`、判定服务 | Identity 集成测试覆盖 AC9 |
| 引导服务抽取 | 普通引导移到应用层服务，Runner 改为调用该服务 | 现有 `ResourceAdminBootstrapTests` 全部通过（AC11） |
| 投影接入 | Identity 客户端方法，`EnsureCurrentUserProjectedAsync` 新建分支调用查询和引导 | Resource 集成测试覆盖 AC1–AC8、AC10 |
| 配置与部署 | `appsettings.json` 默认 scope、`template/deploy/docker-compose.yml` | 生成矩阵；Resource 与 Identity 场景 build、lint |
| 文档 | 后端 README"资源管理员首次授予"改为自动引导为主、命令为辅；部署文档写初始化与升级顺序；`docs/specs/2026-11-16-multitenancy-layering.md` 中与 `--grant-admin` 引导相关的表述同步 | 文档检查、受限公开词扫描 |
| 真实环境验收 | worker-01 工作区演示站：新租户的管理员和宿主管理员直接进入 Resource | AC12 截图 |

### 不做的事

见 §1 非目标。另外不新增配置开关：不想让某人保留 Admin 的产品，在 Resource 本地把他移出即可，之后不会再被加回。

## 4. 结果

Identity 查询端点按机器主体与 `administrators.read` 授权。Resource 在首次投影时调用该端点，建成员、授予 Admin 与数据库操作记录在同一工作单元提交。授权依据为 `IdentityAdministrator`，记录器以请求主体作为操作者。DbMigrator 沿用命令的输出、操作人和 `DeploymentBootstrap` 依据。

部署配置同步了 Resource API 的 scope，升级顺序已写入部署配置文档。后端 README、服务间调用标准和多租户分层规格已同步。

worker-01 的验收覆盖 AC1–AC11。四个生成场景共通过 998 项单测和 85 项定向集成测试，包含迁移快照、控制器授权约定和原有 `ResourceAdminBootstrapTests`。PR 档生成与文档检查以及交叉形态的构建和 lint 均已通过。

依赖审计发现图标包经 schematics 将 picomatch 纳入生产依赖闭包。图标包现使用 36.1.0。picomatch 公告暂无修复版本，由生产依赖审计输出警告。详情见[前端依赖维护](../template/quality-assurance.md#前端依赖维护)。

AC12 留待本 PR 合并、develop 发布 beta 后的演示站人工验收。
