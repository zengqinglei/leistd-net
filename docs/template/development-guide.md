# 模板开发规范

## 1. 定位

`template/` 是可发布的 `dotnet new` 项目模板，也是 Leistd.* NuGet 包的参考消费端。模板维护需要同时保证源模板正确、条件裁剪正确以及生成结果可构建运行。

## 2. 依赖方向

- `Domain` 保存业务模型和内层抽象，不依赖 Application、Infrastructure 或 Api。
- `Application` 编排用例并依赖 Domain，不依赖 Infrastructure。
- `Infrastructure` 实现持久化和外部适配器并依赖 Domain；需要实现 Application 抽象时可依赖单独的 Contracts 项目，当前模板未拆该项目。
- `Api` 是组合根，可同时引用 Application 和 Infrastructure，并负责服务注册与端点映射。
- 模板只通过 `PackageReference` 消费框架包，本地联调也使用 `.tmp/local-feed`。

## 3. 条件生成

修改条件化功能前先读 `template/.template.config/template.json`。条件代码、文件排除、项目引用、DI、前端路由、Mock 和文档必须作为一个场景整体调整，生成后不得残留 `<!--#if`、`<!--#endif` 或模板占位符。

### 3.1 加开关的两条规则

**规则一：数据类型只问一个问题——同一产物能否同时要这两者？**

| 答案 | 类型 |
| --- | --- |
| 不能，且互斥取值 ≥3 个 | 枚举 |
| 其余一律 | **布尔 `IncludeXxx`** |

服务形态恰好是三个互斥取值，因此是唯一的枚举 `ServiceRole`：

| 取值 | 含义 |
| --- | --- |
| `Identity`（默认） | 自己签发令牌，持有本地身份与租户控制面 |
| `Standalone` | 有本地身份与租户控制面，但**不带授权服务器**（Cookie 会话形态） |
| `Resource` | 不持有身份，校验远端签发的令牌 |

其余能力一律布尔，因为它们都能表达成"有/无某能力"：`IncludeNotifications`、`IncludeExternalLogin`、`IncludeLocalization`。

**规则二：能力按"用户会不会单独取舍"划粒度，相关的一次给全。**

身份能力一次带上用户、角色、权限管理、登录、密码、邮箱验证与租户控制面，不拆 `IncludeRoles`（旧模板拆过，是过度细分）。授权服务器不作为独立布尔，而是 `ServiceRole` 的一个取值——因为"带不带 `/connect/*` 端点"和"是不是身份服务"在部署上从来不是两个独立选择。

**computed 只做枚举到能力的映射**，让条件代码写能力名而不是写 `ServiceRole == "..."`：

```
LocalIdentity             = (ServiceRole != "Resource")
OpenIddictServer          = (ServiceRole == "Identity")
RemoteTokenAuth           = (ServiceRole == "Resource")
ServiceUserContextEnabled = (ServiceRole != "Standalone")
```

条件代码只引用这四个能力名。这样新增一个 `ServiceRole` 取值时改的是这四行，而不是散在几百个文件里的比较表达式。

### 3.2 前置依赖靠枚举消解，不要用 `isEnabled`

`isEnabled` 不能引用 computed 符号，而本模板的前置条件均为能力名。

将互斥关系编码进枚举取值：`ServiceRole=Resource` 已表示没有本地身份，无需额外布尔开关。`template.json` 不使用 `isEnabled`。

其他布尔参数在前置条件不成立时可以显示，但不得生成无效内容；例如外部登录代码整体受 `LocalIdentity` 保护。

### 3.3 模板引擎限制

| 限制 | 失败形态 |
| --- | --- |
| **不支持 `#error` 指令** | 处理该文件时抛 `Object reference not set`，**所有场景生成失败** |
| **`isEnabled` / computed `value` 引用已删除的符号** | 该符号单独用正常，**一进复合条件就 NRE**；报错只有文件名 |
| **注释里写条件指令的字面形式** | 引擎支持 `//#if`，于是把注释当成真实指令，配对错位、**整段代码被静默吞掉**，产物少几百行却"生成成功" |
| **删条件块后留下恒真嵌套**（`#if (X)` 套在 `#if (X)` 里） | 内层永真、`#else` 分支永不可达；也可能触发上一条 |
| **只查 `.cs`/`.ts` 会漏 `.csproj`** | 包引用没剪掉，产物仍带着该依赖 |
| **`isEnabled` 引用 computed 符号** | `String 'X' was not recognized as a valid Boolean`；只接受 parameter |

修改参数结构后先运行 `pwsh scripts/check-all.ps1`，再运行场景矩阵。与模板直接相关的检查如下：

| 脚本 | 拦什么 |
| --- | --- |
| `scripts/check-template-symbols.ps1` | 悬空符号（`isEnabled`、computed `value`、modifier `condition`、代码 `#if` 四处）、注释里的指令字面形式、恒真嵌套、与 `#if` 同义的 `#elif` |
| `scripts/check-using-guards.py` | 五项：C# using 守卫（双向）、前端 TS import 守卫、`InternalsVisibleTo` 无条件、csproj XML 良构、无仅含空行的条件块。详见该文件头 |
| `scripts/check-async-boundaries.py` | 动态连接路径（UoW、多租户、`Leistd.Data`、模板 `TenantConnections`）里的 `.Result` / `.Wait()` / `GetAwaiter().GetResult()` |

`check-using-guards.py` 验证全部符号取值组合；场景矩阵只编译 `$Scenarios` 中列出的组合。

### 3.4 前端条件块：`//#if` 紧贴上一单元，空行留在块内首行

**独立语法单元**（函数、interface、方法、`it(...)` 用例）被条件包裹时：`//#if` 紧跟上一个单元、之间不留空行；块内第一行留空；`//#endif` 紧跟块内最后一行，之后再空一行接下一个单元。

```ts
}
//#if (Symbol)

function conditionalThing() {
  // ...
}
//#endif

export const next = 1;
```

开启时得到「上一单元 + 空行 + 本单元 + 空行 + 下一单元」，关闭时整块消失后剩「上一单元 + 空行 + 下一单元」——两种形态各恰好一个空行。

把空行放在标记外侧（`}` 空行 `//#if`）则关闭时会剩下双空行，prettier 判 lint 失败；而只跑其中一种场景发现不了。**条件块的格式必须按开、关两个场景分别验证。**

这条只管独立语法单元。**数组元素、对象成员、`import` 具名列表**里的条件项本来就不带空行，直接紧贴前后项即可；而当两个场景对同一处的期望本身不同（例如列表变短后 prettier 要求折成单行），就不要在结构内部放 `//#if`，改成两个分支各写一份完整的语法单元。

### 3.5 标准场景矩阵

| 场景 | 参数重点 | 目的 |
| --- | --- | --- |
| `identity` | 默认 | OIDC Server、租户控制面、Identity 业务库与完整前端 |
| `resource` | `ServiceRole=Resource` | 远端令牌校验、本服务授权、动态租户连接与完整前端 |
| `standalone` | `ServiceRole=Standalone` | Cookie 会话形态：有本地身份与租户控制面，**不带授权服务器** |
| `identity-notifications` | `IncludeNotifications=true` | Identity 可选通知切片 |
| `resource-notifications` | Resource + 同上 | Resource 可选通知切片 |
| `identity-external-login` | `IncludeExternalLogin=true` | Identity 外部登录适配 |
| `identity-localization` | `IncludeLocalization=true` | Identity 本地化切片 |
| `resource-localization` | Resource + 同上 | Resource 本地化切片 |

`standalone` 专门检验条件独立性：`identity` 与 `resource` 里 `LocalIdentity` 和 `OpenIddictServer` 恰好同真同假，只有 `standalone` 把两者分开。新增能力时不得只验证 `identity` 和 `resource`。

场景断言分两类，缺一不可：`Present`/`Absent` 与 `RequiredTokens` 证明"留下的是对的那一份"，`ForbiddenTokens` 证明"不该留的没留下"。只查缺失会漏掉"两份都在"的情形。

`ForbiddenTokens` **不得为了让场景通过而放宽**——它抓到的每一次都是真残留。若某个词在注释里合法出现，改注释措辞，不改断言。

### 3.6 跨 `ServiceRole` 的 HTTP 契约由框架统一

租户连接的机器端点（`runtime/{tenantId}`、`migration`）与资源服务回源用的远端存储都由多租户组件提供：
Identity 形态用 `MapTenantConnections` 映射端点，Resource 形态用 `Leistd.MultiTenancy.ServiceClient` 的 `AddRemoteTenantConnectionStore` 回源，
两边共用 `Leistd.MultiTenancy.Core` 里的线上 DTO。模板只决定前缀：Identity 在 `ComponentEndpoints` 里的路由组前缀与 Resource 的
`Leistd:ServiceClients:Identity:RoutePrefix`（默认 `/api/v1/tenant-connections`）必须一致。改前缀时两处一起改，并在 Identity 与 Resource 的 HTTP 组合验证中确认。
模板不再手写这组端点的控制器、Refit 接口或 `IMyProjectClient` 方法。

> 这些是 leistd-net 的维护事实，**不要写进 `template/` 源码注释**（见 `developing-leistd-template` skill 的「对外分发边界」）。模板注释只写生成项目自身运行与持续开发需要的知识。

### 3.7 错误码不随本地化裁剪

`BusinessException` 在构造时必填错误码，因此模板不再用 `#if (IncludeLocalization)` 裁掉业务码。多语言形态用它查词条，非多语言形态仍用它做客户端分支和日志聚合；两种形态的机器契约完全一致。

错误码改名按破坏性变更处理，对前端分支和 API 状态映射都要有针对性测试。
业务错误码按所属模块和最低实际使用层放置：Domain 规则码归对应 Domain 模块，纯用例码归 Application 模块，`Domain/Shared` 只保留真正跨模块的契约。组件错误码始终引用组件常量。API 各模块只登记非默认 HTTP 状态，组合根显式汇总模块和组件适配层的默认映射；宿主覆盖优先且与调用顺序无关，默认 400 不建第二张清单。
错误码采用 `模块:语义名`，前缀由一个模块独占、后缀与常量成员名一致；`*ErrorCodes` 检查同时验证唯一性、格式和资源键。未命中映射的 `BusinessException` 回落 400；422 仅在客户端需要区分“内容可解析但无法处理”时显式映射。

## 4. Skill 与规范

- `template/.agents/skills/leistd-project-workflow/` 是跨工具项目协作入口，不依赖 `CLAUDE.md`、`AGENTS.md` 或其他工具专属文件。
- 单一 `SKILL.md` 根据用户最终意图路由规划、实现、审查、测试、协调和部署，并持有对应完成责任；场景细节按需从一层 `references/` 加载。
- `template/docs/README.md` 是生成项目唯一文档索引，`docs/standards/` 只保存工程事实，不重复 Skill 流程。
- Skill 安装后即使项目没有文档，也必须从源码、配置、测试和 CI 继续低风险任务；只有产生长期可复用信息时才按需创建最小权威文档。
- 不携带固定需求、规范或报告模板，不预建按需目录。
- 修改任何 Skill 时使用官方 `skill-creator` 并运行 `scripts/validate-skills.ps1`。
- 前端 UI 走 Spartan UI：选型依据与主题/能力取舍见 [`docs/architecture/frontend-ui-library.md`](../architecture/frontend-ui-library.md)，组件用法规范见 [`template/docs/standards/coding-frontend.md`](../../template/docs/standards/coding-frontend.md)；改前端时按「`spartan` skill（`.agents/skills/spartan/`，含 `rules/`）→ 本地 `libs/ui` 源码 → 官方文档」确认组件 API，不臆造 Helm/Brain API。`@spartan-ng/mcp` 是仓库维护者的可选工具（根 `.mcp.json`），模板不内置。

## 5. 本地框架联调

先在仓库根目录打包，再由模板矩阵通过一次性 NuGet 配置消费：

```powershell
pwsh framework/build/pack-local-feed.ps1
pwsh scripts/test-template-matrix.ps1 -SkipPack
```

需要人工观看前端测试运行时，改用有头 Chrome（CI 仍默认 `ChromeHeadless`）：

```powershell
pwsh scripts/test-template-matrix.ps1 -SkipPack -FrontendBrowser Chrome
```

不在仓库 `NuGet.Config` 或生成项目中固化本地源。

## 6. 数据库初始化

- 模板携带可审查的 EF Core 基线迁移；API 启动不执行 `MigrateAsync` 或 `EnsureCreatedAsync`。
- 每个服务的 `DbMigrator` 是一次性部署进程，先于 API 运行，使用 DDL 身份；API 只使用 DML 身份。
- 每个服务在所有物理数据库中使用自己的固定 schema 和迁移历史表。Identity 的租户/OIDC Control DbContext 固定连宿主 Control DB，不跟随租户路由。
- `ConnectionStrings:MigrationTarget` 只用于首次预迁移一个尚未登记的 Dedicated 物理目标；常规发布仍从 Identity 枚举已登记目标并去重迁移。
- 修改迁移策略时必须同步 DbMigrator、基线 migration、生成项目 README、部署说明与真实 PostgreSQL 闭环断言。

## 7. 事务边界

工作单元**按需引入**，不是每个写方法的必需装饰。默认不开启，写业务代码可以先不理解它。

- **只有跨多次提交边界的方法才标 `[UnitOfWork]`。** 单次 `SaveChanges` 本身已在数据库隐式事务里；
  写入由管理器以一次 `SaveChanges` 完成的（权限授予、租户连接配置）也已自证原子；
  不走本框架仓储的第三方存储（OpenIddict 自带 store）标了也管不到。判据见
  [工作单元组件文档](../../framework/docs/components/unit-of-work.md#何时不需要)。
- **写方法一律用仓储返回值构造输出，不回查数据库。** `InsertAsync` / `UpdateAsync` 返回实体，
  且 `Id`（Guid v7 领域生成）与创建审计、租户值都在实体进入跟踪时就已落定，返回时即完整。
  回查有两处害处：多一次往返；且在工作单元内那些行还没落库，回查得到空结果——
  "创建后返回创建结果"会变成 404 或少掉全部关联。
- 需要把刚写入的关联数据回显时，让写方法**回传**它写了什么（如 `AssignDefaultRolesToUserAsync`
  返回角色名），而不是让调用方按 id 再查一遍。
- 已有反向决定的地方不要覆盖：`RoleAppService.DeleteAsync` 刻意不做成一个事务并写明了失败形态选择。

## 8. 宿主与租户侧别

新增平台能力时先回答一个问题：**这条权限背后的数据带不带 `TenantId`？**

| 数据形态 | 侧别 | 模板中的例子 |
| --- | --- | --- |
| 实体实现 `IMultiTenant`，受全局租户过滤器分区 | `Both` | 用户、角色、设置、权限目录 |
| 宿主全局，无 `TenantId`，过滤器不生效 | `Host` | 租户注册表、OpenIddict 开放应用 |
| 只在租户内成立 | `Tenant` | 模板当前没有 |

**省略 `side` 等于选择 `Both`**，而 `Both` 的含义是"租户管理员也拿得到"。对宿主全局资源来说这就是跨租户越权：`TenantSeeder` 按 `Side.HasFlag(MultiTenancySides.Tenant)` 播种，`Both` 会命中，于是每个租户的 Admin 角色都被授予该权限；宿主全局的表又不受租户过滤器约束，接口返回的就是全系统的数据。**这条只在真的建了租户之后才暴露**，单租户本地开发和单场景测试永远是绿的。

**侧别为 `Host` 时，权限检查本身就是边界，不要在服务里再拦一次。**
`DefaultPermissionChecker` 按当前侧别判定，且**与是否授予无关**——即便有人手工往库里写一条授予记录，
租户上下文下仍然不通过。在应用服务入口重复一遍同一个不变量，只是把它写两处、且没有任何东西保证两处同步。

**只有一种情形需要服务内再校验：权限必须保持 `Both`，而它管辖的内容里有一部分是宿主专属的。**
`App.Settings` 就是这一种——两侧都要能改自己的设置，所以侧别不能收成 `Host`；
但 `SettingScopes.Host` 那几项（日志级别这类进程级配置）只有宿主能写，
这一层租户过滤器和权限侧别都表达不了，由设置组件的设置页用例在读取时隐藏、在写入时拒绝（`Setting:HostOnly`）。

判断口径：**先问侧别能不能表达。能，就只写侧别；不能，才在服务里补。**

### 实体的租户维度

同一条判据对持久化对象同样适用，问法换成：**这个实体会不会被独立查询？**

| 情形 | 要求 |
| --- | --- |
| 会被独立查询（有自己的仓储 / `DbSet`，或被 `Where` 直接命中） | **必须 `IMultiTenant`** |
| 只经聚合根访问，没有任何独立查询入口 | 不需要；但必须**真的**没有入口 |

**没有第三种状态。**"当前所有调用路径恰好都先经过了受过滤的表"不是一种设计——它不被任何机制保证，新增一条直查路径就破防；而 `MultiTenantFilterGuard` 只扫 `IMultiTenant` 实体，对这种实体一个字都不会说。

宿主全局的数据（控制面表、OpenIddict 这类第三方表）走另一条路：**不映射进租户上下文**，能力边界由权限侧别把守。

**侧别是 `AddPermission` 的必填参数**，漏声明连编译都过不去——编译器本身就是那张"已决定"清单，不需要另立一张表来对账。（早先的 `PermissionContractTests.ExpectedSides` 是侧别还可省略时的补偿闸门；根因消除后它已随之删除。）

**收紧侧别不会撤销已经授出去的记录。** `SeedAdminRolePermissionsAsync` 只在授权版本为 0 时播种，既不自动补齐也不自动撤销——因此把某条权限从 `Both` 改成 `Host` 时，必须同时给既有部署一条撤销 SQL，否则已建租户仍持有该权限。

## 9. 集成测试夹具只用内存库（为什么不做双库夹具）

模板的 `ProjectWebApplicationFactory` 只有一个内存库，租户专属库的路由不在它的覆盖范围内。
这是有意的划分，被反复提出过，结论记在这里，不写进模板载荷：

1. **覆盖已经在了，而且更真。** `scripts/test-template-postgresql-e2e.ps1` 在真实 PostgreSQL 上
   逐条断言共享租户的数据落在默认库、专属租户落在自己的库、两边互不泄漏，外加连接登记、
   事后分库被拒 409、连接串静态加密。再加一份内存版是重复，不是补缺。
2. **做了就得在生产组合根里开一个只服务于测试的口子。** `Infrastructure/DependencyInjection.cs`
   按"有没有连接串"二选一：有就 `UseNpgsql`，没有就内存库——全项目只有 Npgsql 一个真实提供程序。
   要让测试选别的，得往那里加分支，或引入一层提供程序选择抽象，而那层抽象唯一的消费者是测试夹具。
3. **绿灯会给假信心。** 模板声明了默认 schema，迁移历史表是 schema 限定的；SQLite 没有 schema，
   夹具只能走 `EnsureCreated`，物理形态与生产不同，而租户路由的正确性恰恰依赖这些。

下游仓库各自维护多连接测试宿主这件事，**解法不在模板**：模板是 `dotnet new` 的一次性脚手架，
已生成的项目不跟随模板更新，往这里加夹具只对将来新建的项目有效。若那份重复确实成立，
载体应是框架侧的测试支撑包（随版本升级下发）——当前 `framework/tests` 全部 `IsPackable=false`，
那会是一个新的交付面，动手前先看各处宿主真正共用的是什么，不要先建包再找用途。

## 10. 验证

```powershell
pwsh scripts/check-all.ps1                                 # 全部静态闸门（~40s，-List 看清单）
pwsh scripts/test-template-matrix.ps1
pwsh scripts/test-template-postgresql-e2e.ps1 -SkipPack
```

第三条在真实 PostgreSQL 上验证本地 Framework NuGet 包→Identity/Resource 生成→DbMigrator→API→Shared/Dedicated 隔离的整条链路；它要求本机已安装 Docker、`psql` 和 PowerShell。

每次运行使用独立的 run 目录 `.tmp/runs/<run-id>/`（`<run-id>` = PID+时间戳），其下含 `generated-template/`、`local-feed/`、`template-hive/`、`nuget-cache/` 与一次性 NuGet 配置——生成物、包源和 `globalPackagesFolder` 都不跨 run 写入，因此**多个 AI/终端可并行执行**。不得共享解包目录后再“定点清理 Leistd.*”：本地包会在版本号不变时重新 pack，清理会在另一个并发 build 期间抽走 DLL。NuGet 自身的 HTTP 缓存仍会避免重复下载。启动时只清理超过 2 小时未活动且非当前 run 的旧目录（据 `.run.lock` 判活），绝不删正在运行的 run。CI 发布目录仍使用 `framework/artifacts`。

重复调试同一份已 pack 的本地包时可用 `-SkipPack`（读取共享的 `.tmp/local-feed`）；Framework 包内容变化后必须重新 pack，不得让旧的同版本包掩盖源码改动。
