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

### 3.6 跨 `ServiceRole` 的 HTTP 契约没有编译期保护

租户连接端点的路径在模板源码里有**三份副本**，分属不同 `ServiceRole` 的产物，任何一次生成都只包含其中一部分，因此彼此**无法编译期互检**：

| 位置 | 出现在哪种形态 |
| --- | --- |
| `Api/Controllers/TenantConnectionController.cs`（`[Route("api/v1/tenant-connections")]`） | `LocalIdentity` |
| `Client/IMyProjectClient.cs` 的租户连接方法 | `LocalIdentity`（Resource 生成物里被裁掉） |
| `Infrastructure/TenantConnections/IdentityTenantConnectionClient.cs` | `!LocalIdentity` |

现有场景按角色分别生成，无法编译期互检这些副本。改动任一处路由时必须同步另外两处，并在 Identity 与 Resource 的 HTTP 组合验证中确认路径一致。

> 这些是 leistd-net 的维护事实，**不要写进 `template/` 源码注释**（见 `developing-leistd-template` skill 的「对外分发边界」）：生成项目里既没有另外两份副本，也没有模板矩阵与仓库 E2E，业务开发者无从按此核对。模板注释只写生成项目自身运行与持续开发需要的知识。

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

## 8. 验证

```powershell
pwsh scripts/check-all.ps1                                 # 全部静态闸门（~40s，-List 看清单）
pwsh scripts/test-template-matrix.ps1
pwsh scripts/test-template-postgresql-e2e.ps1 -SkipPack
```

第三条在真实 PostgreSQL 上验证本地 Framework NuGet 包→Identity/Resource 生成→DbMigrator→API→Shared/Dedicated 隔离的整条链路；它要求本机已安装 Docker、`psql` 和 PowerShell。

每次运行使用独立的 run 目录 `.tmp/runs/<run-id>/`（`<run-id>` = PID+时间戳），其下含 `generated-template/`、`local-feed/`、`template-hive/`、`nuget-cache/` 与一次性 NuGet 配置——生成物、包源和 `globalPackagesFolder` 都不跨 run 写入，因此**多个 AI/终端可并行执行**。不得共享解包目录后再“定点清理 Leistd.*”：本地包会在版本号不变时重新 pack，清理会在另一个并发 build 期间抽走 DLL。NuGet 自身的 HTTP 缓存仍会避免重复下载。启动时只清理超过 2 小时未活动且非当前 run 的旧目录（据 `.run.lock` 判活），绝不删正在运行的 run。CI 发布目录仍使用 `framework/artifacts`。

重复调试同一份已 pack 的本地包时可用 `-SkipPack`（读取共享的 `.tmp/local-feed`）；Framework 包内容变化后必须重新 pack，不得让旧的同版本包掩盖源码改动。
