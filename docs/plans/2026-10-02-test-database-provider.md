# 模板移除 EF InMemory、测试改用 PostgreSQL

核查日期 2026-10-02，基线提交 `01ab01cc`。问题：模板的集成测试能否把 EF InMemory 换成 SQLite，甚至连 PostgreSQL 端到端也只用 SQLite。
用户 2026-10-02 裁决：集成测试改用 PostgreSQL（Testcontainers），开发回退与运行时冒烟取向 B，模板彻底移除 InMemory。全部验收完成后删除本文，长期规则上收到 `docs/template/quality-assurance.md` 与 `template/docs/standards/testing.md`。

## 结论

1. **不能只用 SQLite。** PostgreSQL 端到端验证的是 Npgsql 迁移在多个库与 schema 上的实际执行、`DbMigrator` 命令行、控制库与业务库的物理拆分、专属租户库的连接路由。SQLite 没有 schema，执行不了 Npgsql 迁移，也走不到 `NpgsqlConnection` 路由分支，一项都替代不了。
2. **集成测试也不建议换成 SQLite。** 原型 364 个用例过了 360 个；但其中 2 个暴露出结构性冲突：本框架大量使用独立事务（`Begin(requiresNew: true)`，模板与框架约 25 处），而 SQLite 每个库只有一个写者。外层事务已写入后再开独立事务写入，就会互相等待到超时（`database is locked`）。PostgreSQL 是行级锁，这种写法在生产上完全正常。换成 SQLite 等于让测试误拒合法代码，与 InMemory 的误放方向相反。
3. **推荐：模板集成测试改用真实 PostgreSQL**（Testcontainers：每次测试运行起一个容器，迁移一次模板库，每个测试宿主克隆一个库）。原型 364 个用例过了 358 个，6 个失败都是测试夹具对 InMemory 的假设，不是产品问题。墙钟约为 InMemory 的 1.5 倍，换来生产提供程序上的唯一约束、事务、回滚、独立事务、SQLSTATE 翻译和每次运行都实际执行的迁移。
4. **框架不变。** 组件测试已经按需使用 SQLite 验证关系语义（权限、多租户过滤、设置、通知、操作记录等）；只有审计组件用 InMemory，它只碰变更跟踪器，理由成立。

## 一、现状

| 位置 | 提供程序 | 说明 |
| --- | --- | --- |
| 框架组件测试 | SQLite（9 个测试项目）、InMemory（审计） | 唯一索引、带过滤索引、查询翻译用 SQLite 验证 |
| 模板集成测试 | EF InMemory | `ProjectWebApplicationFactory` 置空连接串，走 `UseInMemoryFallback` |
| 模板开发回退 | EF InMemory | 未配 `ConnectionStrings:Default` 时用 `Database:InMemoryName`，实现"克隆即可运行" |
| 模板运行时冒烟（矩阵） | EF InMemory | 同上 |
| PostgreSQL 端到端 | 真实 PostgreSQL | 迁移、`DbMigrator`、Shared/Dedicated 隔离、控制库拆分 |
| OIDC 端到端 | 真实 PostgreSQL | 四服务协议闭环 |

InMemory 已经在代码里留下三类代价：

- **生产代码为它让路**：`OperationRecordArchiveService` 不用 `ExecuteDelete`（注释"兼容内存库"），`UserAppService` 不用批量 `RevokeBySubjectAsync`。
- **测试刻意回避的断言**：`TenancyTests`、`AuthorizationAndAuditingTests` 注明"InMemory 不提供真实事务，不用它证明回滚"、"不强制唯一索引，多实例失败形态复现不出来"；夹具注释禁止给租户登记连接。
- **已踩过的坑**：工作单元不能混用两种 DbContext、组件存储不能用 `ExecuteDelete`（见维护记忆），都是 InMemory 与生产行为不一致造成的。

模型侧没有 SQLite 的类型障碍：实体没有 `decimal`、`DateTimeOffset` 字段（SQLite 对它们的排序与比较会直接报错）。但业务项目加金额等字段后就会碰到。

## 二、原型实测

在同一生成项目（identity-all-features，364 个集成用例）的副本上分别实现，模板源码未改。三种实现交替测量两轮，本机负载约 30。

| 实现 | 墙钟（两轮） | 结果 | 改动 |
| --- | --- | --- | --- |
| InMemory（现状） | 20.4 / 19.6 秒 | 364 / 364 | — |
| SQLite 临时文件库（WAL） | 42.6 / 43.0 秒 | 360 / 364 | 回退改 `UseSqlite`；启动时对 SQLite `EnsureCreated` |
| PostgreSQL 容器（fsync 关闭） | 30.0 / 32.2 秒 | 358 / 364 | 生成项目自己的 `DbMigrator --apply` 迁移模板库；每个宿主 `CREATE DATABASE … TEMPLATE` |

**SQLite 的 4 个失败**

- `AccessFailureCounterTests.The_count_is_committed_independently_of_the_calling_transaction`、`ReauthenticationLockoutTests.The_guard_counts_through_the_shared_counter`：各卡满 30 秒后报 `database is locked`。根因是单写者模型，见结论 2。墙钟里约 30 秒来自这两条。
- `ControlPlaneStoreSeparationTests` 两条：断言读的是 InMemory 的库名信息，需要改写。
- 去掉锁超时后，用例耗时合计 111 → 154 秒（+38%）。

**PostgreSQL 的 6 个失败**

- `ControlPlaneStoreSeparationTests` 两条：同上。
- `DefaultAdminBootstrapTests` 四条：靠同一个 `InMemoryName` 让两个宿主共享一个库，原型给每个宿主都克隆了新库。改为共享连接串即可。
- 两条独立事务用例在 PostgreSQL 上通过。
- CPU 时间约 +30%。墙钟增加主要来自容器网络往返与每宿主建库。

## 三、候选比较

| 维度 | InMemory（现状） | SQLite | PostgreSQL（Testcontainers） |
| --- | --- | --- | --- |
| 唯一索引、带过滤索引 | 不强制 | 强制 | 强制 |
| 查询翻译 | 全内存求值，不可翻译的查询静默通过 | 翻译为 SQLite，方言差异另计 | 与生产一致 |
| 事务与回滚 | 无 | 有，但单写者 | 与生产一致 |
| 独立事务（`requiresNew`） | 不真实 | **先写后开独立写入会锁死** | 与生产一致 |
| 迁移 | 不执行 | 不能执行（用 `EnsureCreated`） | 每次运行实际执行 |
| SQLSTATE 错误翻译、专属租户库路由 | 走不到 | 走不到 | 能测 |
| `ExecuteDelete` / `ExecuteUpdate` | 不支持 | 支持 | 支持 |
| 外部依赖 | 无 | 无 | 需要 Docker |
| 集成测试墙钟（本机） | 1.0× | 约 1.2–1.4×（去掉锁超时） | 约 1.5× |
| 官方立场 | 不建议用于测试 | 推荐的内存测试替身 | 推荐的真实库测试 |

官方依据：[EF Core 测试选型](https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy)（不建议 InMemory；真实库测试"几乎从来不需要靠内存库换速度"）、[针对生产数据库测试](https://learn.microsoft.com/en-us/ef/core/testing/testing-with-the-database)（一次建库与播种、按需克隆或清理、并行用多库）、[SQLite 提供程序限制](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations)（不支持 schema、迁移操作受限、`decimal`/`DateTimeOffset` 排序与比较受限）。

## 四、推荐方案与影响

**模板集成测试改用 PostgreSQL（Testcontainers）**

- 每次测试运行起一个 PostgreSQL 容器，用项目自身迁移建好模板库，每个 `ProjectWebApplicationFactory` 克隆一个库，`Dispose` 时删除。
- 夹具不再置空连接串，走生产的 `UseNpgsql` 分支；租户专属库也可以在集成测试里登记和验证。
- 需要改写的测试：`ControlPlaneStoreSeparationTests`（改为断言连接目标）、`DefaultAdminBootstrapTests`（改为共享连接串）。注释里因 InMemory 回避的回滚、唯一约束断言，可以改为真实断言。
- 单元测试不受影响，不依赖 Docker。
- CI 估算：每个场景多出容器启动与模板库迁移约 10 秒，Identity 场景的集成测试多约 10–15 秒。PR 档墙钟预计从 440 秒增至约 470–490 秒，接近 8 分钟上限，需实测确认。

**PostgreSQL 端到端保留**，但职责收窄：租户隔离等断言可以逐项移到集成测试；端到端只保留 `DbMigrator` 命令行、多库物理拆分、跨进程 Data Protection 这类必须跨进程验证的内容。逐项移动仍按变异验收。

**需要另行决定：开发回退与运行时冒烟里的 InMemory**

集成测试不再用 InMemory 后，它只剩"克隆即可运行"的开发回退和矩阵运行时冒烟。两种取向：

| 取向 | 做法 | 代价 |
| --- | --- | --- |
| A. 保留 InMemory 回退 | 开发回退与冒烟不变 | 一种无测试覆盖的运行形态；生产代码仍须避开 InMemory 不支持的写法 |
| B. 移除 InMemory | 开发用已有的 `deploy/docker-compose.dev.yml` 起 PostgreSQL；冒烟复用测试容器 | 克隆后多一条 compose 命令；纯前端开发者也需要 Docker；`Database:InMemoryName` 等配置删除 |

## 五、实施结果

| 项 | 结果 |
| --- | --- |
| 生产代码 | 删除 InMemory 回退与 `TenantConnectionResolutionOptions`；缺连接串在创建上下文时抛出并指明键名（API 在接流量前的迁移校验即触发，M3 实测）；`UserAppService` 恢复官方批量 `RevokeBySubjectAsync`；schema 校验去掉非关系型分支 |
| 开发流程 | Api 与 DbMigrator 的 `appsettings.Development.json` 指向开发 compose；DbMigrator 加 `RunWorkingDirectory`（实测前从 `backend/` 运行读不到项目内开发配置）；Resource 补全回源 Identity 的开发配置与机器客户端说明。生成项目上实走 compose → `dotnet run` 迁移 → API：就绪 200、演示管理员登录 200 |
| 集成测试 | `PostgreSqlTestDatabase`（Testcontainers，迁移一次模板库、每个工厂克隆、释放时删库）；Resource 用共享库替身与测试服务凭据；`OpenIddictLifecycleTests` 改走官方批量清理并换作用域读库；删角色回滚补上授予断言；新增数据库约束与控制面连接回落两组测试 |
| 变异 | M1 控制面接到默认连接 → 回落测试红；M2 迁移去掉检查约束 → 约束测试红；M3 清空连接串 → API 启动失败并指明键名；恢复后均绿 |
| 矩阵 | 冒烟改为每场景建库 → 该场景 `DbMigrator --apply`（Resource 用 `MigrationTarget`）→ 启动 API；共用容器按 run id 命名，收尾只删自己的 |
| 实施中修复 | `Invoke-WithEnvironment` 恢复时 `$null` 变成空字符串、残留空 `DOTNET_ENVIRONMENT`（最小实验证实）；Resource 迁移器会回源 Identity，原注释"不会回源"是错的 |
| 评审 | 独立会话评审 9 项全部处理：Resource 开发流程补全（F1）、三处配置说明统一（F2）、模板载荷去掉仓库脚本引用并改为集成测试闭环（F3）、编码规范更新（F4）、控制面测试改为验证连接回落（F5）、容器清理改用启动标志（F6）、注明需要本机 Docker 引擎（F7）、测试规范写明容器整体回收（F8/F9） |

## 六、未验证项

- CI（4 vCPU）上的实际耗时，以及每个场景起容器的成本。
- OIDC 端到端（交由 CI；本地脚本当时由其他会话维护）。
- Windows 与远端 Docker 上下文下 Testcontainers 的可用性。
