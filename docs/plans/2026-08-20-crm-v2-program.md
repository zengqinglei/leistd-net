# CRM V2：Leistd 模板落地指南

## 1. 本文的定位与边界

**服务拓扑不在本文决定。** 服务数量、逻辑边界、CRM 十模块、数据 Owner、第三方归属的唯一权威是 CRM V2 工作区的 `docs/01-服务与模块划分/README.md`。本文只回答一件事：**在那份已定的拓扑上，怎么用 Leistd 框架与模板把它建起来。**

具体回答四个问题：

1. 五个新服务各自用什么模板参数生成，目录长什么样。
2. 哪些能力是框架现成的、哪些是生成后调整、哪些是业务新增。
3. **开工前必须先补的模板能力**（§5）——这是本文最重要的结论。
4. 测试策略与验收标准。

以下内容**以对方文档为准，本文不重复也不竞争**：

| 主题 | 权威位置 |
| --- | --- |
| 服务划分与 CRM 十模块 | `01-服务与模块划分/README.md` |
| 存储与 KYC 资料平台（SPI、Provider 路由、扫描隔离、presigned 语义） | `01-服务与模块划分/存储与KYC资料平台.md` |
| 通信渠道与 Provider 路由（发送状态机、幂等标识、租户隔离） | `01-服务与模块划分/通信渠道与Provider路由.md` |
| 多租户与 License | `03-多租户与License`（**本文尚未对账**，见 §9） |

框架的能力边界与选型依据见[隔离与授权场景](../architecture/isolation-and-authorization-scenarios.md)。

## 2. 五个新服务的模板落地

既有系统 `spec-sales`、`spec-ib` 不在本文范围——它们是 CRM 互调权威 API 的对象，不是模板生成物。

| 服务 | 角色 | `IncludeIdentity` | `IncludeRoles` | `IncludeTenancy` | OpenIddict | 说明 |
| --- | --- | --- | --- | --- | --- | --- |
| `spec-id` | 授权服务器 + Access + SaaS Control Plane | ✓ | ✓ | ✓ | **Server** | 唯一颁发令牌的服务；租户注册表与登录页归它 |
| `crm-v2` | 模块化单体，10 模块 | ✓ | ✓ | ✓ | **Validation** | 资源服务器；模块内聚不拆服务 |
| `spec-foundation` | 存储/KYC/通信渠道等共享能力 | ✓ | ✓ | ✓ | **Validation** | 内部划分见对方文档 |
| `matrixhub` | 事件契约中枢 | 待定 | 待定 | 待定 | **Validation** | 判据见下 |
| `tradehub` | 交易域 Owner | 待定 | 待定 | 待定 | **Validation** | 判据见下 |

`matrixhub` / `tradehub` 的参数取决于两个问题，答案未知前不预设：**它有面向人的管理界面吗**（决定 `IncludeIdentity` / `IncludeRoles`）、**它持有按租户分区的业务数据吗**（决定 `IncludeTenancy`）。纯服务间设施若两问皆否，用 `minimal` 形态并只保留令牌校验。

> **OpenIddict 一列的三个取值不是现有模板参数。** 现状只有布尔的 `IncludeOpenIddict`，且 `false` 分支是纯 Cookie 认证。见 §5.1——这是开工前的阻塞项。

`IncludeNotifications`：`crm-v2` 建议开（站内通知与实时推送是 CRM 常规需求），其余按需。`IncludeLocalization`、`IncludeExternalLogin` 按产品要求定，`spec-id` 若接第三方登录则开后者。

## 3. 目录结构

根目录 `/Users/quincy/orca/workspaces/crm-refactor-v2-claude/方案细化/`，与既有 `docs/` 规划材料并列。

**每个服务是一份完整的模板产出**，即 `dotnet new` 原样生成的全部内容——因此任何一个服务都可以随时 `git init` 独立成仓，不需要先做结构搬迁：

```text
方案细化/
├── docs/                          # 既有规划文档，不动
├── README.md                      # 既有
│
├── spec-id/                       # 一份完整 template 产出 = 一个未来的独立仓
│   ├── .agents/skills/            # [T] 项目 Skill
│   ├── backend/
│   │   ├── src/
│   │   │   ├── SpecId.Api/                   # [T] 保留授权服务器端点
│   │   │   ├── SpecId.Application/
│   │   │   │   ├── Tenants/                  # [T] 模板自带租户 CRUD 与种子
│   │   │   │   └── Branding/                 # [B] 租户品牌：登录页与邮件页眉页脚的唯一来源
│   │   │   ├── SpecId.Domain/
│   │   │   ├── SpecId.Infrastructure/
│   │   │   ├── SpecId.Client/                # [B] SDK：品牌、用户档案、凭据
│   │   │   └── SpecId.DbMigrator/            # [T] 见 §5.2
│   │   └── tests/SpecId.IntegrationTests/    # [T]+[B]
│   ├── frontend/                  # [T] 登录页按品牌渲染
│   ├── deploy/                    # [T] 本服务自己的 Compose / K8s
│   ├── docs/                      # [T] 含本服务的迁移与发布 runbook
│   ├── Dockerfile
│   ├── README.md
│   ├── VERSION
│   └── .gitignore · .gitattributes · .dockerignore
│
├── crm-v2/                        # 同上完整结构
│   ├── backend/src/
│   │   ├── Crm.Api/
│   │   ├── Crm.Application/
│   │   │   ├── Modules/                      # [B] 10 个 DDD 模块（清单见 §9 待对账）
│   │   │   ├── DataScopes/                   # [B] IDataScopeProvider<T> + IDataScopeAssignmentProvider
│   │   │   └── Permissions/Provider/         # [T] 本服务 App.*；宿主侧能力标 Host 侧别
│   │   ├── Crm.Domain/Modules/               # [B] 模块实体与领域服务；租户化实体实现 IMultiTenant
│   │   ├── Crm.Infrastructure/
│   │   ├── Crm.Client/                       # [B] SDK
│   │   └── Crm.DbMigrator/                   # [T]
│   ├── frontend/                  # [T] crm-web；crm-site 若为独立站点则另起一份完整产出
│   └── ...（deploy / docs / Dockerfile / VERSION 同 spec-id）
│
├── spec-foundation/               # 同上；内部模块划分见对方专题文档
├── matrixhub/                     # 同上
├── tradehub/                      # 同上
│
└── local-orchestration/           # [B] 仅本地联调用的 Compose 聚合
```

标记：`[F]` 框架现成 · `[T]` 模板生成后调整 · `[B]` 业务新增

### 3.1 两条结构约束

**不设跨服务共享源码工程。** 跨服务只共享**形状**不共享**类型**：枚举值以字符串走线，各服务在自己的 SDK DTO 里定义本地枚举。共享一个源码工程会让五个仓库进入版本锁步，与"每个服务可独立成仓"直接冲突。

**`local-orchestration/` 不是服务，任何服务都不得依赖它。** 每个服务自带 `deploy/`，独立成仓时不需要回头找根目录。

## 4. 框架适配 vs 业务调整

### 4.1 框架侧：保持默认，不为本项目新增组件

| 需求 | 用现成的什么 | 判定 |
| --- | --- | --- |
| 租户隔离 | `Leistd.MultiTenancy.*` | 实体实现 `IMultiTenant` 即可 |
| 子域名品牌化 | `MultiTenancyOptions.DomainFormat` | 配置项 |
| 每服务独立角色与权限 | `Leistd.Authorization.*` | 授予已按 `(TenantId, ProviderName, ProviderKey)` 分区 |
| 集合可见性 | `Leistd.Authorization.DataScope.Core` | 业务实现 Provider，组件不动 |
| 单实例授权（"这一份文档给张三"） | `Leistd.Authorization.Resource.*` | 组件不动 |
| 跨服务身份与租户传递 | `Leistd.ServiceClient.*` | 出站自动注入用户上下文与 `X-Tenant-Id` |
| 领域分层、仓储、工作单元、事件 | `framework/ddd-struct`、`Leistd.EventBus` | 不动 |

**通用组件不新增。** 两处门槛需要说明：

- **没有 Settings / Features 家族。** 租户可配置项在 20 以内时，业务侧一张配置表 + 强类型配置类足够；差异项持续增长或租户数上到几十才立项 `Leistd.Settings`。
- **对象存储没有抽象组件。** 只有 `spec-foundation` 碰厂商 SDK，一个消费者的抽象不进通用组件（门槛是三个以上真实消费者）。

**明确不做**：不把品牌等业务字段塞进 `TenantConfiguration`。租户注册表是访问控制状态（已为此去掉缓存），不能和高频读的展示数据混住。

### 4.2 业务项目侧：生成后必须调整的清单

| 项 | 调整内容 | 不做的后果 |
| --- | --- | --- |
| `Program.cs` | 配 `DomainFormat`；配 `ForwardedHeaders:KnownNetworks` 为网关网段 | 不配网段则网关后转发头全部失效；清空则任意客户端可改写 Host 并绕过子域名权威 |
| 权限定义 Provider | 加本服务的 `App.*`，宿主侧能力标 `MultiTenancySides.Host` | 租户超管会看到宿主功能，点进去必然 403 |
| `DbContext.ConfigureModel` | 挂业务实体配置（`OnModelCreating` 已封闭，不要改写） | 全局过滤器覆盖不到该实体，隔离静默失效 |
| 实体唯一索引 | 租户化实体的唯一索引拆成宿主行 / 租户行成对过滤索引 | 可空 `TenantId` 进唯一索引时 NULL 互不相等，宿主行失去唯一性兜底 |
| 前端父路由白名单 | 新模块权限加入 `/platform` 的 `data.permissions` 与 `canAccessPlatform` | 只有该模块权限的用户进不了控制台（后端通、前端 403） |
| 前端菜单分组 | 按关注点分组（数据范围属访问控制，不属业务） | 归类错会误导使用者 |
| 列表页形态 | 复用 `hlmTable` + TanStack + `app-table-paginator` | 与既有页面风格断裂、无分页 |
| `*.Client` | 补对外方法与 DTO | 调用方手写 HttpClient，租户与用户上下文传递失效 |

## 5. 开工前必须先补的模板能力

### 5.1 资源服务器模式（阻塞项）

**现状**：模板的 OpenIddict 是一条链 `AddCore().AddServer(...).AddValidation(o => o.UseLocalServer())`。`UseLocalServer()` 表示"只信任本进程内的颁发者"。而 `IncludeOpenIddict=false` 分支是**纯 Cookie 认证，没有任何 Bearer 校验**；服务间用户上下文恢复（`AddServiceUserContext`）也整段在 OpenIddict 分支内。

**因此模板今天只支持一种形态：自己既是授权服务器又是资源服务器。** 本项目"身份集中在 `spec-id`、其余服务校验它签发的令牌"这个形态模板不支持——两个取值都不对：

- `true`：四个业务服务各自成为授权服务器，对外暴露 `/connect/token` 等端点。这是实打实的攻击面扩张，不是"多余代码"。
- `false`：收不了 Bearer，也没有服务间用户上下文，身份集中直接不成立。

**要补的内容**：把"授权服务器"与"资源服务器"拆成两种可选形态。

| 形态 | 装配 | 适用 |
| --- | --- | --- |
| Server | `AddCore` + `AddServer` + `AddValidation(UseLocalServer)` | `spec-id` |
| Validation | 仅 `AddValidation`，配远端 `Issuer` + `UseSystemNetHttp`，校验 audience 与 scope | 其余四个服务 |
| None | 现有 Cookie 分支 | 单体自用 |

配套：`AddServiceUserContext` 必须移出 `IncludeOpenIddict` 门禁（Validation 形态同样需要）；新增 issuer / audience 配置键；矩阵补 Validation 场景；跨服务集成测试覆盖"远端签发的令牌被正确校验"与"错误 audience 被拒"。

**这一项不补，五个服务之间无法建立可信的身份与租户传递链。**

### 5.2 迁移门禁与迁移器（强烈建议同批）

**现状**：模板在 `app.Run()` 之前无条件执行——有迁移走 `MigrateAsync`、无迁移走 `EnsureCreatedAsync`，**没有环境判断**。生产每个 Pod 启动都尝试改 schema，多副本同时启动即并发迁移。

要补三项：

**1. 门禁** `Database:AutoMigrate`（可空布尔，默认 = 是否 Development）。决策抽成纯函数以便单测真值表：

| 环境 | AutoMigrate | 有迁移 | 有待执行 | 决策 |
| --- | --- | --- | --- | --- |
| Dev | 默认(true) | 是 | 是 | 应用 |
| Dev | 默认 | 是 | 否 | 跳过 |
| Dev | 默认 | 否 | — | `EnsureCreated` |
| Dev | 显式 false | 是 | 是 | 启动失败（本地演练发布流程） |
| 非 Dev | 默认(false) | 是 | 是 | **启动失败**并打印待执行清单 |
| 非 Dev | 默认 | 是 | 否 | 跳过 |
| 非 Dev | 默认 | 否 | — | **启动失败**（生产要求已生成迁移） |
| 非 Dev | 显式 true | 是 | 是 | 应用 + 警告日志 |
| 非 Dev | 显式 true | 否 | — | **仍然失败**——绝不在迁移历史之外建表 |

**2. `*.DbMigrator` 控制台工程**，两个模式：默认只读预演（打印待执行清单与 SQL），`--apply` 执行。对方文档已把 `DbMigrator` 列入"服务内工作负载"，形态一致。运行镜像因此不需要 EF 工具链。

**3. 生产应用账号不给 DDL 权限**——这条把"应用不改 schema"从约定变成机制。只靠代码里的环境判断，早晚有人加配置绕过。

不做的三件事及理由：不给迁移加分布式锁（多副本竞争只出现在明确劝退的路径上，正解是用 Job）；不把种子搬进迁移器（种子幂等、与 schema 无关）；不加第三种 CLI 模式（预演 + 执行够用）。

零停机走 expand/contract：加可空列 → 回填 → 部署兼容新旧的代码 → 切换 → 下个版本删列。**任何"一步重命名"都会在滚动发布期间打断正在运行的旧副本。** runbook 沿用既定的生产变更流程（只读预演 → 人工确认 → 单事务 + 前置校验 + 提交前自检 → 备好回滚脚本）。

## 6. 数据库分离

**两个互相独立的轴，不要混谈：**

| 轴 | 选择 |
| --- | --- |
| 按服务分库 | **做**——五个服务五个库。共享数据库的"微服务"是同一个部署单元穿了五件衣服 |
| 按租户分库 | **不做**——共库共表 + `TenantId` + 全局过滤器 |

框架只支持共库共表（`TenantConfiguration` 是封闭六字段，**刻意没有连接串**）。按租户分库的代价：迁移从一次变成 ×租户数且部分失败留下混合 schema、连接池翻倍、**宿主侧跨租户聚合报表做不了**（而 SaaS Control Plane 恰恰需要）、建租户从一条 INSERT 变成一次基础设施操作。

**现在要留的三处零成本接缝：**

1. DbContext 创建集中在 `Infrastructure/DependencyInjection` 一处，将来插连接串解析器只改一个文件。
2. **`IMultiTenant` 全局过滤器永远不撤**，即使将来分库——一条路由错的连接会泄漏整个库。"分库了就不用过滤器"是灾难性优化。
3. 跨租户查询必须走显式 `IDataFilter.Disable<IMultiTenant>()` 口子，那些点就是将来分库要重写的清单。

**触发条件**：数据驻留合规 / 合同约定的物理隔离 / 单租户体量迫使独立伸缩。届时改造约一个组件的量（`TenantConfiguration` 增连接串 → 按 `ICurrentTenant` 解析 → 选项按连接串缓存 → 迁移器遍历租户），ABP 是这个形状，属已知设计。

**否决共库分 schema**：EF 支持别扭、备份粒度没变好，承担了复杂度却没拿到隔离。

## 7. 测试策略

| 层 | 覆盖什么 | 硬性要求 |
| --- | --- | --- |
| 单元 | 领域规则、迁移门禁真值表、数据范围 Provider 组合语义 | 范围 Provider **必须在关系型 Provider 上测**——InMemory 全内存求值，不可翻译的谓词会静默通过 |
| 集成（每服务） | 三路越权矩阵（列表/详情/更新）、同名用户跨租户不可见、停用租户处理、批量操作整体拒绝 | 断言以"看不见"为主；跨租户按 Id 取数必须 404 而非 403 |
| 跨服务 | A 在租户 T 内调 B，B 的 `ICurrentTenant.Id == T` 且查询落 T 分区；匿名伪造租户头被拒；远端 issuer 令牌校验通过、错误 audience 被拒 | §5.1 补完后才可测 |
| 浏览器闭环 | 只测跨进程、跨时序、跨浏览器语义的接缝 | 见下 |
| 矩阵 | 各服务的参数组合可生成、可构建、零残留标记 | 每次动条件代码都跑 |

**每个修复都要反向验证**：把修复退回去，确认对应用例变红。多租户开发中反复出现"用例根本没走到目标分支"，只有反向验证能发现。

浏览器闭环必测：

1. 各 broker 子域名的品牌正确且不串。
2. 租户超管菜单不含宿主侧功能，直接敲 URL 同样 403。
3. 只有单模块权限的用户能进控制台并只看到该模块。
4. 配置角色数据范围后换人登录，列表集合随之变化。
5. 租户被停用后在途会话被注销、本地租户状态清空、回登录页不死锁。

第 4、5 项需在 **CORS 分离部署下重跑**——跨域响应头要 `WithExposedHeaders` 才能被前端读到，而后端集成测试直读响应头、前端单测手工构造 `HttpHeaders`，两侧都绕过浏览器的 CORS 过滤。

## 8. 验收标准

**隔离与安全（任一不过即不可上线）**

- [ ] 跨租户按 Id 取数返回 404 而非 403（不泄漏资源存在性）。
- [ ] 已认证会话携带伪造租户头无法改写自身租户。
- [ ] 子域名部署下匿名请求无法用请求头改写租户；受管域内无租户段的主机定案为宿主；受管域外仍可用请求头（服务间调用依赖它）。
- [ ] 每个租户化实体都在"完整性锁"断言清单内；不实现 `IMultiTenant` 的关联表单独断言。
- [ ] 资源服务器拒绝非 `spec-id` 签发、audience 不匹配或已过期的令牌。

**功能性**

- [ ] 各 broker 子域名进入后品牌、注册流程、邮件页眉页脚正确且互不影响。
- [ ] 新增同类 broker **只需数据变更**，不改代码、不发版。
- [ ] 同一用户在各服务拥有不同角色，权限判定互不影响。
- [ ] 数据范围在列表、总数、导出三个入口给出一致集合；读范围不蕴含写范围；批量操作含越权项时整体拒绝。

**工程性**

- [ ] 生成项目零编译警告（含 CS0105、CS1573 这类）。
- [ ] 每服务集成测试全绿；模板参数矩阵全绿。
- [ ] 配置错误（非法域名格式、非法代理网段、待执行迁移）导致宿主**启动失败**并在错误中带出配置键与值，而不是记日志后以退出码 0 结束。
- [ ] 每个服务目录可独立 `git init` 成仓并单独构建，不依赖根目录下的任何共享工程。

## 9. 待对账清单

本文成稿时以下内容尚未对账，需在完整审核时补齐：

| 项 | 影响 |
| --- | --- |
| `03-多租户与License` | License 概念在本框架中不存在。它与租户是什么关系（License 是租户属性、还是独立于租户的授权维度）会影响 §6 的分库结论与权限模型 |
| CRM 十模块清单 | §3 的 `Modules/` 需按实际模块展开，并标注哪些模块持有租户化实体 |
| `matrixhub` / `tradehub` 职责 | §2 的模板参数待定项 |
| `spec-foundation` 内部划分 | 以对方两份专题文档为准，本文只负责它的模板落地形态 |
| `crm-site` 与 `crm-web` 是否两份独立前端产出 | §3 的 `crm-v2/frontend/` 形态 |
