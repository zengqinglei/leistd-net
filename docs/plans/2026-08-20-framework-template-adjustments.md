# Framework / Template 待调整内容：文件级任务分解

本文只回答**改哪些文件、每处职责是什么**。取舍依据见 对应的落地方案 的 §5、§6，不在此重复；少数项（§2 的时机取舍、§3.3 的缺陷表现）依据只在本文，随条目就地给出。

优先级：`P0` 阻塞 下游项目开工 · `P1` 生产上线前必须 · `P2` 可捎带 · `P3` 待决策

> 状态基线：`252bbac` + 一批未提交的实现（按租户分库、ServiceRole、DbMigrator）。
> §2 与 §3.5 是**已核实无待调整项**的记录；§1.1 与 §3.1 是**已实现但形态与原计划不同**，正文保留作对照、遗留任务在小节开头；其余为待做项。

---

## 一、Framework：通用组件

### 1.1 按租户分库支持（**已实现，形态与本节原计划不同**）

> **现状**：能力已落地，但实现形态优于本节原计划，原文保留作对照。
>
> | 项 | 本节原计划 | 实际实现 | 评价 |
> | --- | --- | --- | --- |
> | 连接配置存放 | `TenantRecord` 增 `ConnectionString` 列（明文） | 独立 `TenantConnectionRecord` + **Secret 引用**（`RuntimeSecretReference` / `MigrationSecretReference`） | **实现更好**：控制库不持有凭据；DDL 最小权限进了数据模型；热路径表不被敏感列污染 |
> | 注册表钉宿主连接 | 改 `EfCoreTenantStore` 绑宿主上下文 | 独立 `IdentityControlDbContext`，解析器直接注入它（不经 `IDbContextProvider`）以避免递归 | 达成同一约束 |
> | 不变量 | 列长度约束 | 追加 `CK_TenantConnectionRecord_ModeSecrets` **数据库检查约束**（Shared 不许有 Secret / Dedicated 必须两个都有） | **超出原计划**，ABP 也没有 |
> | UoW 多库 | 原计划未覆盖 | `UnitOfWorkConnectionBinding`：UoW 内切换租户或解析到不同物理目标立即失败 | **超出原计划**，跨库部分提交成为结构性不可达 |
>
> **遗留任务**（源自代码审查，按优先级）：
>
> | 序 | 项 |
> | --- | --- |
> | **P0** | 连接串解析抽象**移出 `MultiTenancy`**：`Leistd.UnitOfWork.EfCore` 现在硬依赖 `MultiTenancy.Core`，不需要租户的服务也被拖进来。抽象改名 `IConnectionStringResolver` / `[ConnectionStringName]` 放 `UnitOfWork.Core`，`MultiTenancy` 只提供实现。ABP 佐证：`IConnectionStringResolver` 与 `ConnectionStringNameAttribute` 在与租户无关的 `Volo.Abp.Data` |
> | **P2** | `TenantConnectionRecord` 加修改审计 + 收紧 setter。它是全系统最敏感的一行（改它就改了租户数据落在哪个库），却只有 `Version` 没有"谁在何时改的"；而同目录 `TenantRecord` 是全审计软删 + 私有 setter |
> | **P2** | 检查约束改用 EF 元数据取真实列名，不用 `nameof` 拼、不硬编码 PostgreSQL 双引号——消费方启用命名约定（PG 常用 snake_case）会引用不存在的列 |
> | **P2** | `_transactionApis` 退回单字段：`UnitOfWorkConnectionBinding` 保证一个 UoW 只有一个物理目标 → 只可能一个事务 key，字典/循环/重复检查以及 `ITransactionApiContainer` 的公开破坏性变更全部服务于不变量禁止的场景 |
> | **P3** | `"Default"` 提为常量（现硬编码在 `DbContextProvider.cs:22`）；回落链写成 `IConnectionStringResolver` 的显式契约并注明"具名条目暂不支持" |
> | **P3** | `TenantConnectionStringNameAttribute` XML 注释改中文（同目录其余 5 个文件均为中文）；新增异常消息语言与框架既有统一 |
> | **P3** | `ResolveConnectionStringAsync` 双入口收成一处；去掉 `Join` 里冗余的 `!IsDeleted`（全局过滤器已生效） |
> | 测试 | 补"Shared 与 Dedicated 两种模式下建租户+播种都成立"——同一代码路径在两种模式下行为不同（Shared 同物理目标可同事务，Dedicated 会触发 `Bind` 抛异常） |
> | 注释 | 「解析器直接注入 `IdentityControlDbContext`、不经 `IDbContextProvider`」这个防递归不变量要写进类注释——后人改成走 Provider 会栈溢出 |

以下为原计划正文（对照用）。租户可独立配置连接串，所有服务共用该租户的库，各服务仍在自己的 schema。**最关键的约束是租户注册表必须钉在宿主连接上**——解析租户要先读注册表，注册表若随业务数据搬进租户库就形成鸡生蛋。

```text
framework/components/multi-tenancy/
├── Leistd.MultiTenancy.Core/
│   ├── Store/
│   │   ├── TenantConfiguration.cs                    # 改：增 ConnectionString（可空，空=用默认库）
│   │   ├── ITenantManager.cs                         # 改：Create/Update 支持连接串维护
│   │   └── ITenantConnectionStringResolver.cs        # 新增：按 ICurrentTenant 解析连接串；
│   │                                                 #      抽象放 Core（不依赖 EF）
│   └── DependencyInjection.cs                        # 改：注册解析器默认实现
│
└── Leistd.MultiTenancy.EntityFrameworkCore/
    ├── Entities/TenantRecord.cs                      # 改：增连接串列
    ├── EntityConfigurations/TenantRecordConfiguration.cs  # 改：列长度约束；连接串不进索引
    ├── Stores/EfCoreTenantStore.cs                   # 改（核心）：不再复用业务 DbContext，
    │                                                 #   改为绑定"宿主连接"上下文，否则鸡生蛋
    ├── Managers/EfCoreTenantManager.cs               # 改：写入连接串；同宿主连接
    └── DependencyInjection.cs                        # 改：AddMultiTenancyEfCore 区分
                                                      #   宿主注册表上下文与业务上下文
```

配套测试与文档：

```text
framework/tests/Leistd.MultiTenancy.Tests/
├── TenantConnectionRoutingTests.cs                   # 新增：按租户路由到不同连接；
│                                                     #   空连接串回落默认库；
│                                                     #   **误指到别的租户库时过滤器仍然生效**
└── TenantStoreHostPinningTests.cs                    # 新增：注册表读取始终走宿主连接，
                                                      #   不受当前租户影响（鸡生蛋回归锁）

framework/docs/components/multi-tenancy.md            # 改：两种隔离模式、宿主连接约束、跨租户聚合的显式出口
docs/framework/versioning.md                          # 改：破坏性变更条目（Store 注册形态变化）
```

### 1.2 弃用的 AutoMapper 组件去留（P3，待决策）

现状是**已文档化的保留决定**（`framework/Directory.Packages.props` 注释：13.0.1 有高危 DoS，官方仅在商业授权版修复，新项目用 Mapster，NU1903 告警属预期）。按"不考虑兼容性"原则应当删除整个家族；保留则下游仍会拿到有漏洞的依赖。

```text
framework/components/object-mapping/Leistd.ObjectMapping.AutoMapper/   # 删除整个包（若决定删）
framework/Directory.Packages.props                                     # 移除 AutoMapper 版本项
framework/docs/components/object-mapping.md                            # 改：移除该实现的章节
framework/Leistd.Framework.slnx                                        # 移除工程引用
```

### 1.3 判定不做（记录门槛，避免反复讨论）

| 项 | 门槛 |
| --- | --- |
| `Leistd.Settings` / Features 家族 | 租户可配置项 <20 时业务侧一张表足够；差异项持续增长或租户数上到几十再立项 |
| 对象存储抽象组件 | 只有 `foundation` 碰厂商 SDK；单一消费者不进通用组件（门槛：三个以上真实消费者） |
| `TenantConfiguration` 承载业务字段 | 租户注册表是访问控制状态，不与高频读的展示数据混住 |
| **具名连接串**（ABP 解析链第 4 级，每服务独立库） | 管道已通（`ResolveAsync(name)` 与 `[...Name]` attribute 都在），缺的只是存储从 1:1 变 1:N —— 6 个点全在连接配置这一条竖切面，UoW 层一行不动。**门槛**：出现第一个"不能与其它服务共用租户库"的真实需求（某服务负载需独立实例，或合规要求跨区域） |
| **ABP 的 `Databases` 分组映射**（解析链第 5 级） | **永不做**。那层间接的存在理由是 ABP 不知道消费方装哪些模块；我们知道自己的组合，"多服务共用一个租户库"用 schema 分开即可。届时保持三级扁平链：`(租户,名)` → `(租户,Default)` → 宿主配置 |
| **缺配置时回落到宿主库**（ABP 的行为） | **不采纳，保持我们更严的失败关闭**。ABP 在租户没配连接串时静默用宿主库；若该租户本该独立，那就是跨租户数据混住——比宕机更糟。`TenantAppService.CreateAsync` 已保证创建时写入连接记录（`SetAsync` 是 upsert） |

> **建模提示（留给将来）**：`TenantDatabaseMode` 现在挂在**租户**上是正确的，但正确性依赖 1:1 前提。将来支持具名时，模式要下移到具名行——一个租户可以大部分服务共库、只有某个服务独立。

## 二、Framework：DDD 领域分层

**无待调整项。** 新增实体的环境值落点已定案并落地（`00ae4e7`、`252bbac`），当前职责边界：

```text
framework/ddd-struct/Leistd.Ddd.Infrastructure/Persistence/
└── BaseDbContext.cs          职责一：新增实体的环境值落点
                                （ChangeTracker.Tracked + StateChanged）
                                → TenantId + 创建审计（CreationTime / CreatorId）
                                → 三层护栏：FromQuery 跳过、状态须为 Added、值已有则不动
                              职责二：两个命名全局查询过滤器（软删除 AND 租户）
                              职责三：封闭 OnModelCreating，保证过滤器在派生配置之后套用

framework/components/auditing/Leistd.Auditing.EntityFrameworkCore/
└── AuditSaveChangesInterceptor.cs
                              职责：只管 Modified / Deleted（含软删除转换）
                                → Added 已移出，不再是它的职责

framework/components/multi-tenancy/Leistd.MultiTenancy.EntityFrameworkCore/
                              职责：只做租户注册表的存储与管理
                                → 落值组件已删除（MultiTenantSaveChangesInterceptor）
```

四条依据均在多租户开发中验证过：

| 依据 | 验证方式 |
| --- | --- |
| 环境值落在"进入跟踪时"是正确时机 | 与 ABP 的 `AbpDbContext.ChangeTracker_Tracked` → `SetCreationAuditProperties` 一致；四条回退验证按预期变红 |
| 提早落值不会覆盖查询出来的数据 | 三层护栏各有用例；反向验证确认 `FromQuery` 是冗余护栏（查询物化结果是 `Unchanged`，永不为 `Added`），保留以把意图写进代码 |
| `BaseDbContext.ConfigureModel` 封闭式钩子已正确——过滤器在派生配置之后套用 | 有回归用例（只经 `ApplyConfiguration` 入模型的实体同样被过滤） |
| 框架组件表名**未硬编码 schema**，派生上下文一行 `HasDefaultSchema` 即可覆盖全部实体 | 已 grep 确认无 `HasDefaultSchema`、组件 `ToTable` 不带 schema |
| 仓储与 `IQueryableAsyncExecuter` 抽象够用 | 验证项目里数据范围特性做完，Application 层未引用 EF Core |

> 与 Volo.ABP 的唯一残留差异是 `TenantId`：ABP 落在 `Entity` 基类构造函数（更早一步，靠反射写私有 setter）。本框架选择保持领域实体基类零环境依赖，代价是"作用域内 `new`、作用域外 `Add`"这种跨作用域持有实体的写法拿不到租户值——那本身是应当避免的写法。取舍已记录，不再重开。

## 三、Template：业务项目模板

### 3.1 资源服务器模式（**已实现，形态与本节原计划不同**）

> **现状**：能力已落地。原计划是把 `IncludeOpenIddict` 布尔改成三态；实际实现引入了 `ServiceRole` 选择项（`Identity` / `Resource`），参数面从 7 个 bool（128 组合）降到 1 个 choice + 3 个 bool（16 组合），矩阵场景 13 → 7。
>
> **`ServiceRole` 这个轴我认同——它直接对应真实部署形态。但同一次改动里捆进了三项能力删除，需要按下面的粒度分开处理。**
>
> | 序 | 项 | 成本 | 状态 |
> | --- | --- | --- | --- |
> | **5a** | 恢复"无 OIDC"模式：加第三个 `ServiceRole` 值 | **小**。Cookie scheme 还完整在（`AddCookie("MyProjectCookie")` 仍用于 Identity 角色的浏览器会话），缺的只是"完全不装 OpenIddict"这个组合。需要：一个新 choice + Program.cs 一个分支 + 一个矩阵场景。不碰租户、不碰授权 | 待做 |
> | **5b** | `MultiTenancy` 从恒真 computed 恢复为参数 | **中，比看起来贵**。72 处 `#if (MultiTenancy)` 标记还在，但本次新增的租户关键件是按**服务角色**设门的（`IdentityControlDbContext`、`LocalTenantConnectionStringResolver`、`TenantConnectionController`、`IdentityControlDbContextFactory` 都是 `#if (IdentityService)`；`DatabaseMigrationRunner` **无保护**）。今天置假会引用被剪掉的类型，**编译不过**。须先把这些门禁改成 `IdentityService && MultiTenancy`（或引入 computed `TenantControlPlane`），再用真实的 `no-tenancy` 矩阵场景把关——旧标记的覆盖完整性已不可信 | 待做 |
> | **5c** | `LocalAuthorization` | **建议反向处理**：确认永久，删掉 264 处标记 / 72 个文件。理由：恢复成本最高；"有租户隔离但没有权限检查"的服务是个很怪的产物。**代价**：删后若要恢复需重新给 72 个文件打标记，比 5a/5b 更难回退——若对"将来可能要无授权服务"有预期则改为保留参数 | **待用户表态** |
>
> 顺序：`5a`（独立、最小）→ `5b`（需新矩阵场景把关）→ `5c`（大机械改动，单独一个提交，用矩阵全绿把关——264 处机械删除最易夹带手误）。
>
> 走完这三项后，不再有任何恒真 computed 符号（当前 `LocalAuthorization = true`、`MultiTenancy = true` 让 `#if` 永不为假，是两头不占的死脚手架）。

以下为原计划正文（对照用）。模板当前只支持"自己既是授权服务器又是资源服务器"（`AddCore().AddServer().AddValidation(UseLocalServer)` 一条链，`false` 分支是纯 Cookie 且 `AddServiceUserContext` 也在该门禁内）。

```text
template/
├── .template.config/template.json                    # 改：IncludeOpenIddict 布尔 →
│                                                     #   三态（Server / Validation / None）；
│                                                     #   同步 modifier 的排除清单
└── backend/src/CompanyName.ProjectName.Api/
    ├── Program.cs                                    # 改：三分支装配
    │                                                 #   Server: Core+Server+Validation(UseLocalServer)
    │                                                 #   Validation: 仅 Validation + 远端 Issuer
    │                                                 #                + UseSystemNetHttp + audience/scope
    │                                                 #   None: 现有 Cookie 分支
    │                                                 # 改：AddServiceUserContext 移出 OpenIddict 门禁
    │                                                 #   （Validation 形态同样需要服务间用户上下文）
    └── appsettings.json                              # 改：增 OAuth:Issuer / OAuth:Audience
```

```text
scripts/test-template-matrix.ps1                      # 改：新增 resource-server 场景；
                                                       #   既有场景断言 Validation 形态零残留
template/backend/tests/.../ServiceInvocationTests.cs   # 改：远端签发令牌被正确校验、
                                                       #   错误 audience 被拒
```

### 3.2 迁移形态（P1）

两种方式都由**条件剪裁**提供，非法组合在生成期拒绝——剪裁让能力根本不在产物里。

#### 边界条件（已查证，不要凭直觉重推）

| 事实 | 出处 |
| --- | --- |
| **EF Core 9+ 的 `Migrate()`/`MigrateAsync()` 自动获取库级锁**，贯穿迁移与数据播种，CLI、bundle、运行时方法一律适用 | EF Core 官方文档「Migration locking」。provider 实现：SQL Server `sp_getapplock`、PostgreSQL advisory lock、SQLite 建 `__EFMigrationsLock` 表 |
| 在**显式事务**里调 `Migrate()` **会抛异常**——显式事务会阻止获取该锁 | EF Core 9 破坏性变更。**约束：启动前移分支绝不能包在 UoW/事务里** |
| K8s 滚动更新事实上是串行的（`maxSurge` 默认 25%，新 pod 要等 readiness，而启动迁移未完成 readiness 不通过） | — |

因此**"多副本会竞争迁移导致损坏"不是有效的反对理由**。真正成立的是另外两条，且都与副本数无关：

| 反对理由 | 强度 |
| --- | --- |
| **DDL 权限**：启动前移要求 API 进程持有有 DDL 的凭据，直接作废 Runtime/Migration 双 Secret 的最小权限模型 | 最强 |
| **独立库租户的 N 次迁移**：每个独立库租户一次迁移，启动路径的工作量随租户数**无上界**增长 | 硬伤 |
| 启动阻塞：同时起 N 个 pod 时（初次部署、0→N 扩容、HPA 扩容、节点故障恢复——这些都不走滚动）1 个迁移、N-1 个在锁上等；迁移久于 `startupProbe` 超时会被杀成 crashloop | 次要，调 `failureThreshold` 可缓解 |

#### 由此得出的组合规则

| 组合 | 结论 |
| --- | --- |
| 共享库模式 + **任意副本数** | **允许启动前移**。文档提示把 `startupProbe.failureThreshold` 调到覆盖最长迁移 |
| 存在**独立库租户** | **禁止启动前移**，只能 DbMigrator |

判定发生在**生成期**（由 `ServiceRole` 与是否启用独立库能力决定），不需要运行时检测。

#### 文件级任务

```text
template/
├── .template.config/template.json                    # 改：新增 IncludeDbMigrator；
│                                                     #   拒绝"独立库能力 + 启动前移"的组合
├── backend/
│   ├── CompanyName.ProjectName.sln                    # 改：DbMigrator 工程改为**条件包含**
│   │                                                  #   （现状是无条件包含）
│   └── src/
│       ├── CompanyName.ProjectName.Api/Program.cs     # 改：恢复启动前移分支（共享库模式下）；
│       │                                              #   **不得包在 UoW/事务里**（见上表第 2 条）
│       │                                              #   IncludeDbMigrator=true 时启动
│       │                                              #   **只校验不施加**，有待执行迁移即失败并打印清单
│       └── CompanyName.ProjectName.DbMigrator/
│           ├── Program.cs                             # 改（P0）：**补只读预演**——
│           │                                          #   无参=清单+SQL，--apply=施加。
│           │                                          #   现状不解析 args，RunAsync() 直接施加，
│           │                                          #   撞全局规则「先只读预演，再执行写入」
│           │                                          # 改：catch 补失败的迁移名
│           │                                          #   （现状只打印 exception.GetType().Name）
│           └── DatabaseMigrationRunner.cs             # 改：补 #if 门禁（现状无保护）
└── docs/deploy/README.md                              # 改：迁移与发布 runbook；
                                                        #   生产应用账号不给 DDL 权限；expand/contract；
                                                        #   两种方式各自的适用域与 startupProbe 提示
```

```text
template/backend/tests/.../DbMigratorTests.cs          # 改：补"无参只预演、不写库"与
                                                        #   "--apply 才施加"两条断言
template/backend/tests/.../DbMigratorContractTests.cs  # 新增：启动前移模式下有待执行迁移时
                                                        #   启动失败且错误含待执行清单
scripts/test-template-matrix.ps1                       # 改：新增 db-migrator 场景
docs/template/development-guide.md                     # 改：新场景进标准矩阵表
```

### 3.3 前端平台入口权限集合一（P1）

两份硬编码清单在**租户场景下不等价**——这不是可维护性问题，是能落到真实角色上的缺陷：

| 位置 | 项数 | 含 `tenants.default` |
| --- | --- | --- |
| `app.routes.ts:61-71`（`/platform` 的 `data.permissions`） | 5 | **有**（`#if TenancyEnabled`） |
| `authorization-service.ts:36-44`（`canAccessPlatform`） | 4 | **无** |

一个只有租户管理权限的平台运营账号：

| 位置 | 表现 |
| --- | --- |
| `user-menu.ts:205` | 菜单里看不到平台入口 |
| `login.ts:212` | 登录后被重定向到非平台页 |
| `external-auth-callback.ts:99` | 外部登录同上 |
| 直接敲 `/platform/tenants` | **能进**（路由守卫放行） |

即"后端通、前端不通"，且是租户场景专属。

```text
template/frontend/src/app/
├── shared/models/permission.ts                       # 改：紧邻 PERMISSIONS 导出
│                                                     #   PLATFORM_ENTRY_PERMISSIONS ——
│                                                     #   平台入口权限集的**单一来源**（含 #if 条件段）
├── core/services/authorization-service.ts            # 改：canAccessPlatform 改引用该集合
│                                                     #   （删掉自己那份 4 项硬编码）
├── app.routes.ts                                     # 改：/platform 的 data.permissions 引用同一集合
└── core/services/authorization-service.spec.ts       # 改：断言两处**同源**（集合相等）；
                                                      #   补"仅 tenants 权限可进平台"的用例
```

> 同源断言是这一项的关键交付物。只改成引用同一常量，下一个人仍可能在路由里手写补一项——断言让分叉在 CI 里立刻失败。

### 3.4 多服务共库的 schema 指引（P2）

`HasDefaultSchema` 与 `MigrationsHistoryTable` **必须配对**——只做前者会让多个服务争用同一张 `__EFMigrationsHistory`，这是最隐蔽的坑。两者都是生成后调整，不改模板代码，只补文档与检查单。

```text
template/README.md                                    # 改：多服务共库时的两行配对调整
template/docs/standards/coding-backend.md             # 改：schema 与迁移历史表的约定
```

### 3.5 无需调整（已核实，记录以免重复排查）

| 面 | 核实结论 |
| --- | --- |
| 模板 Controller | `TenantController` 8 个端点全带 `[Authorize(Policy=...)]`；`tenants.*` 声明 `MultiTenancySides.Host`，侧别边界经 `[Authorize]` 免费获得（检查器先于授予读取拒绝）；无 Controller/DTO 接受客户端传入的 `tenantId`（`AuthController.cs:90` 是从用户实体写 claim，不是入参）；`BaseController` 只是 `ControllerBase` 薄壳 |
| 模板前端租户链路 | `tenant-interceptor`（附 `X-Tenant-Id`）、`tenant-context-service`、`http-error-interceptor` 的 `X-Tenant-Invalid` 恢复、租户管理页与登录页租户选择，均完整且有 spec |
| 框架前端 | **不存在交付面**（`framework/` 下无 `package.json`，前端只在 `template/frontend`） |
| 框架 Controller | 只有 `Response.AspNetCore/Extensions/ControllerExtensions.cs` 与 `Exception.AspNetCore` 的异常映射两个触点，与租户无关 |

### 3.6 Resource 宿主的控制面端点（**待用户决策**）

`AuthenticatedTenantContextMiddleware` 要求每个非匿名请求带**恰好一个** `tenant_id` claim。于是一个端点只有两种状态：

| 状态 | 达成方式 |
| --- | --- |
| 必须有租户 | 默认 |
| 完全公开 | `[AllowAnonymous]` |

**缺了中间一档：「已认证、但没有租户」。** 后果：

| 场景 | 现状 |
| --- | --- |
| 带租户的用户请求 | ✓ |
| 服务间调用带租户 | ✓（`UseServiceUserContext` 先把受信 `X-Tenant-Id` 恢复成 claim） |
| Resource 服务的运维诊断端点（指标、缓存清理、权限定义导出） | ✗ 要么标 `[AllowAnonymous]` 变公开，要么进不去 |
| 宿主上下文的后台任务调 Resource 服务 | ✗ 401 |
| 跨租户数据聚合 | ✗ 401 —— **这条本就不该开**，应靠"遍历租户、每租户一次调用、每次一个新 UoW" |

真正缺落点的只有中间两条（控制面端点），不是数据聚合。

**建议**：保留"默认必须有租户"的失败关闭语义，加一个窄的显式豁免标记：

```text
默认              → 已认证 + 恰好一个 tenant_id claim（不变）
[AllowAnonymous]  → 公开（不变）
新增标记           → 已认证 + 通过授权，但不要求租户 claim
```

四条要点：① 默认仍失败关闭，忘标不会漏；② 豁免的是租户要求，不是认证与授权，所以不是后门；③ **只给控制面端点用**，带标记的端点不得查询实现 `IMultiTenant` 的实体——这样"一个请求看不到两个租户的数据"的保证依然成立，这条要写进标记文档；④ 实现量小：中间件多一次 endpoint metadata 判断 + 一条"带标记仍拒绝未认证请求"的测试。

不做的代价：Resource 服务的运维端点要么公开，要么搬到 Identity 服务——后者让 Identity 承担不属于它的职责。

现有测试 `Missing_tenant_claim_is_rejected` 已把当前行为锁成设计，改动需同步该用例的语义。

## 四、执行顺序

§1.1 与 §3.1 的能力已落地，剩下的是审查产出的改进项。按"不需决策 / 需决策 / 收尾"分三批。

### 第一批：不需决策，可立即

| 序 | 项 | 出处 | 理由 |
| --- | --- | --- | --- |
| 1 | DbMigrator 补只读预演 | §3.2 | 撞全局规则「先只读预演，输出确认，再执行写入」 |
| 2 | 连接串解析抽象移出 `MultiTenancy` | §1.1 | 越晚越贵——每多一个服务接进来，破坏性变更面就更大 |
| 3 | `TenantConnectionRecord` 加审计 + 收紧 setter | §1.1 | 最敏感的一行没有修改者记录 |
| 4 | 检查约束不硬编码列名/引号 | §1.1 | 命名约定下会引用不存在的列 |
| 5 | `_transactionApis` 退回单字段 | §1.1 | 顺带撤掉一个不必要的公开破坏性变更 |

### 第二批：需先决策

| 项 | 需要定什么 | 建议 |
| --- | --- | --- |
| §3.2 两种迁移方式 | 是否恢复启动前移为可选 | **恢复**（用户已确认）。约束见 §3.2 组合规则 |
| §3.6 Resource 控制面端点 | 加窄豁免标记，还是不加、搬到 Identity | **加窄标记** |
| §3.1 的 5a / 5b | — | 直接做，顺序 5a → 5b |
| §3.1 的 5c | 将来是否可能要"无授权服务" | 若否则删 264 处标记；若有预期则保留参数 |

### 第三批：收尾

| 序 | 项 |
| --- | --- |
| 6 | §3.3 前端权限集合一（现存缺陷，小改） |
| 7 | §3.4 schema 指引（纯文档） |
| 8 | §1.1 的 P3 组：`"Default"` 提常量、回落链写成契约、注释与异常消息语言统一、双入口收敛、去冗余过滤 |
| 9 | §1.1 的测试与注释：两种模式建租户+播种、防递归不变量写进类注释 |
| 10 | §1.2 AutoMapper 去留（待决策） |

### 依赖关系

- 第一批第 2 项会改 `AddMultiTenancyEfCore` 与 `UnitOfWork.EfCore` 的注册形态，模板 `Infrastructure/DependencyInjection.cs` 需同步——该文件在 `252bbac` 刚因删除落值拦截器改过。
- §3.1 的 5b 与第一批第 2 项都动连接配置这条竖切面，建议 5b 排在第 2 项之后，避免两次改同一批门禁。
- 其余各项互不依赖。
