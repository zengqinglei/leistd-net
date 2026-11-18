# Identity 机器接口改用服务级 scope

状态：已完成 · 风险档：高 · 关联：—

## 1. 需求

### 问题

Identity 给其他服务调用的机器接口，每个都配一个专用 scope：`tenant-routing.read`、`tenant-migration.read`、`account-preferences.read`、`system-directory.read`、`administrators.read`。每新增一个内部接口，都要做以下几件事：

- 改 scope 目录；
- 改调用方的 scope 配置；
- 在每个环境的开放应用里补授权；
- 写清升级顺序。

2026-11-18 的管理员自动引导就是这样：升级要先给 Resource 的机器客户端授予 `administrators.read`，本机走查时又发现"服务端机密客户端"模板下加不进 scope。

### 目标

- scope 只表达"能调哪个服务"，不再按用途拆分。业界做法相同：Entra ID 的服务间调用申请 `api://<资源>/.default`，Auth0 和 Okta 的服务间令牌按目标 API 授予。
- Identity 的全部机器接口都改为认 Identity 自己的服务级 scope（`OAuth:Resource` 同名 scope），并要求调用方是机器主体。
- 以后新增内部接口，不再需要新的 scope，也不需要逐个环境补授权。

### 非目标

- 不按调用方做白名单。能拿到 Identity 服务级 scope 的，只有管理员在 Identity 登记的内部服务。按"不过度考虑安全"的原则，不另加收紧。
- 不改用户令牌、令牌交换的规则，也不改 Resource 自己的 API scope。
- 不改框架组件的行为。

### 已确认的决策

- **用户，2026-11-18：** 不再新增 `administrators.read`，机器接口改用服务级 scope 授权。按用途拆分的 5 个 scope 一并删除，与下游 identity 仓库的做法一致（`docs/project/auth.md`："scope 只表达能调哪个服务，不按用途拆分"）。

### 验收标准

| 编号 | Given / When / Then | 覆盖 |
| --- | --- | --- |
| AC1 机器访问 | Given 机器客户端持有 Identity 服务级 scope；When 调用租户路由（运行时、迁移）、账户偏好、系统目录、内置管理员查询；Then 都返回 200 | Identity 集成测试 |
| AC2 拒绝 | 以下几种情况都被拒绝：不带 Identity 服务级 scope 的机器令牌；带该 scope 的用户令牌，包括令牌交换得到的；会话 Cookie；超管声明 | 单元测试（`MachineScopePolicyTests` 改写）与集成测试 |
| AC3 系统目录 | 自然人照常可以读取系统目录；机器主体须带 Identity 服务级 scope | 单元测试与集成测试 |
| AC4 scope 目录 | 5 个按用途拆分的 scope 不再登记；开放应用的 scope 选项和 Mock 数据中都不再出现 | 集成测试与前端单测 |
| AC5 Resource 与 DbMigrator | Resource API 和 DbMigrator 的机器身份只申请 Identity 服务级 scope，原有功能不变：租户路由、账户偏好、系统目录、首次访问引导 Admin、迁移时枚举租户并补齐 Admin | Resource 集成测试；OIDC 跨服务端到端 |
| AC6 部署配置 | compose 中 Resource API 和 DbMigrator 的 `Leistd__ServiceClients__Identity__Scope` 都取 `${IDENTITY_API_RESOURCE}`，不新增部署变量 | 生成检查 |
| AC7 跨服务端到端 | OIDC 跨服务端到端的默认模式和 `-Workspace` 模式都通过 | 本机串行运行 |
| AC8 开放应用 | 用"服务端机密客户端"模板新建 Resource 的机器客户端，授予 Identity 服务级 scope 后，能完成 AC5 的全部调用。如果模板仍然不能授予该 scope，就修复模板 | 前端单测，或在 PR 中写明界面核对的结论 |

## 2. 方案

### 现状事实

- **scope 目录**：`template/backend/src/CompanyName.ProjectName.Application/Auth/OAuth/OAuthScopes.cs`。服务级 scope 是 `new(options.Resource, "API", [options.Resource], MachineOnly: false)`，另有 5 个 `MachineOnly: true` 的按用途拆分的 scope。常量分别在：
  - `Settings/Constants/AccountPreferenceScopes.cs`
  - `Systems/Constants/SystemDirectoryScopes.cs`
  - `TenantConnections/Constants/TenantConnectionScopes.cs`
  - `Users/Constants/AdministratorScopes.cs`
- **授权策略**：`Api/DependencyInjection.cs` 中，`AddMachineScopePolicy` 逐个为上述 scope 登记策略。它已经检查了 Bearer、机器主体和 scope，正好可以复用。系统目录另有一条"自然人，或带 scope 的机器"策略。
- **调用方配置**：
  - Resource API 在 `Api/appsettings.json` 的 `Leistd:ServiceClients:Identity:Scope` 中列出按用途拆分的 scope；
  - DbMigrator 在 `DbMigrator/appsettings.Development.json`；
  - `template/deploy/docker-compose.yml` 第 24、72、74 行写的是固定的 scope 名。
  - 令牌交换已经使用 `${IDENTITY_API_RESOURCE}`（第 77 行），也就是 Identity 的服务级 scope。
- **其他引用**：
  - 测试：`scripts/test-template-oidc-e2e.ps1`、`scripts/test-template-generation.py`，以及多个集成测试；
  - 文档：`template/docs/deploy/configuration.md`、`template/docs/standards/service-invocation.md`、后端 README；
  - Mock：`template/frontend/_mock/data/open-applications.ts`；
  - 框架里只有示例字符串：`framework/docs/components/service-client.md` 的配置示例，以及两个测试中作为任意 scope 名使用的字符串。
- **下游 identity 的做法**：`MachineApiPolicy`（策略 `Identity.Machine`）要求 Bearer、机器主体和目标服务的 scope，所有内部端点共用；按用途拆分的旧 scope 已不再登记。

### 设计

**Identity**
- 新增一条机器策略，例如 `MachinePolicies.Identity`，名称按模板现有约定定。用 `AddMachineScopePolicy` 登记，要求的 scope 为 `OAuth:Resource`，从 Options 读取。
- 租户路由（运行时、迁移）、账户偏好、内置管理员查询的端点都改用这条策略。
- 系统目录策略改为：自然人，或带 Identity 服务级 scope 的机器。
- 从 `OAuthScopes` 和常量中删除 5 个按用途拆分的 scope。对应的 `*Policies` 常量如果只是为这些 scope 存在，就一起删掉，不留空壳。

**Resource 和 DbMigrator**
- `Leistd:ServiceClients:Identity:Scope` 的默认值留空，开发配置和文档写明：填 Identity 的服务级 scope，与 `TokenExchange:Scope` 取同一个值。
- compose 中两处都取 `${IDENTITY_API_RESOURCE}`。
- 客户端代码不变，只是申请的 scope 变了。

**测试与文档**
- 测试中原有的 scope 断言改为服务级 scope。
- 文档删除"授予 `tenant-routing.read`……"之类的逐项说明，改写为一句话：机器客户端授予 Identity 服务级 scope 即可调用全部机器接口。
- 删除管理员自动引导规格及部署文档中，关于 `administrators.read` 升级顺序的说明。
- 框架文档里的示例字符串改为服务级 scope 的写法。框架测试里作为任意 scope 名使用的字符串不改。

### 备选与取舍

| 备选 | 结论 |
| --- | --- |
| 保留按用途拆分的 scope | 不采用。每个内部接口都要配 scope 和授权，运维成本随接口数量增长，而且已经卡住过一次升级 |
| 服务级 scope 加调用方白名单 | 不采用。能拿到这个 scope 的客户端本来就由管理员登记，白名单只多一处配置 |
| 同时接受旧 scope 一个版本 | 不采用。模板仍处于 beta，下游已经切换；双轨会让策略和测试都翻倍 |

### 兼容与升级

- 已有环境：把 Resource API 和 DbMigrator 的 `Leistd__ServiceClients__Identity__Scope` 改为 Identity 的服务级 scope，与 `TokenExchange:Scope` 相同。
- Resource 的客户端为了做令牌交换，本来就有这个 scope 的授权，所以通常不需要改开放应用。
- 机器客户端如果和交换客户端不是同一个，就在开放应用里给它授予 Identity 服务级 scope，并允许 client_credentials。
- 开放应用里残留的旧 scope 授权不影响运行，可以在编辑时清除。如果编辑保存时校验会拒绝未登记的 scope，就在 PR 中写明处理方式。
- 回滚：回退两侧版本，并恢复原来的 scope 配置。

## 3. 计划

| 任务 | 产物 | 验证 |
| --- | --- | --- |
| Identity 策略与 scope 目录 | 机器策略、端点改用新策略、删除 5 个 scope 及其常量 | 单测（改写 `MachineScopePolicyTests`）；Identity 集成测试 AC1–AC4 |
| 调用方配置 | Resource 和 DbMigrator 的 appsettings、compose | 生成检查 AC6；Resource 集成测试 AC5 |
| 开放应用 | 核对"服务端机密客户端"模板，必要时修复；更新 Mock | 前端单测，或界面核对 AC8 |
| 测试脚本与文档 | OIDC 跨服务端到端、生成检查、部署和服务调用文档、README、相关规格、框架文档示例 | 文档检查、受限公开词扫描 |
| 端到端 | 本机串行运行 OIDC 跨服务端到端的默认和 `-Workspace` 模式 | AC7 |

### 不做的事

见 §1 非目标。

## 4. 结果

Identity 的租户路由、迁移、工作区租户查询、账户偏好与内置管理员查询使用 `App.IdentityMachine`。策略从 `OAuthResourceOptions.Resource` 读取 scope，并要求 Bearer 机器主体。系统目录对自然人及持有该 scope 的机器主体开放。

Resource API 与 DbMigrator 在 Compose 中都取 `IDENTITY_API_RESOURCE`。开放应用的「服务端机密客户端」模板可以授予 Identity 服务级 scope。编辑存量应用时，可以取消勾选仍被选中的退役 scope，再保存。

现行配置与调用约定已同步到模板部署配置、服务间调用规范、后端 README 和框架的服务客户端示例。

Identity 与 Resource 的定向测试、条件形态构建和开放应用浏览器单测通过。默认与工作区两种 OIDC 端到端模式通过，覆盖机器接口、租户路由、账户偏好、系统目录、首次管理员引导与迁移补齐。
