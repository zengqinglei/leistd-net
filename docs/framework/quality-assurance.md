# 质量检查与验证分工

本文定义 leistd-net 的质量检查、删除验收和 CI 调度原则，不随 NuGet 分发。框架用例的目录、注册契约与覆盖取舍见[开发规范 §7](./development-guide.md#7-测试)，模板层级与 lint 缓存见[模板质量验证](../template/quality-assurance.md)。

## 分层执行与时间预算

按改动选最小充分集合，不在每次改动后跑全量。完整集成测试与全部模板场景交给 CI 和合入后执行，与微软"单元测试每次推送前跑、完整集成测试放构建服务器"的分层一致。

| 档位 | 时机 | 预算 | 内容 |
| --- | --- | --- | --- |
| L0 编辑循环 | 每次小改动 | ≤ 1 分钟 | 受影响项目构建 + `dotnet test <测试项目> --filter "FullyQualifiedName~<类>"`；改了某道闸门就单跑它 |
| L1 阶段完成 / 提交前 | 交评审前 | 框架 ≤ 5 分钟，模板 ≤ 10 分钟 | `check-all.ps1` + 下表按路径选的入口 |
| L2 PR CI | 每次推送 PR | 参考约 8–9 分钟，队列与外部准备波动另计 | 框架全量测试、包消费、PostgreSQL/OIDC 端到端、模板 `pr` 档场景 |
| L3 全集 | develop 推送、工作日夜间、发布（main 推送仅在框架或 VERSION 变化时） | 不设严格预算 | 同 L2，模板跑 `full` 档全部场景；发布等待同 SHA 的 L3 结果 |

L1 按改动路径选择入口：

| 改动 | 必跑 | 视情况加跑 |
| --- | --- | --- |
| 只改仓库根 `docs/`、`.agents/` 或 README 文案 | `check-all.ps1` | — |
| 随包 `framework/docs/`、模板 `template/docs/` 或项目 Skill | `check-all.ps1` + 打包内容或代表性生成检查 | 影响运行契约时加相应隔离消费或生成场景验证 |
| 框架某组件家族的实现 | 框架全量测试 + `check-all.ps1` | 公共 API、注册、包依赖变化时加打包与 `-PackageIds` 隔离消费；模板消费方式变化时按下一行验证 |
| 模板某个特性（通知、外部登录、本地化）或某种 `ServiceRole` | 含该特性/形态的一个场景 + `identity-all-features` + 关闭侧场景（通常是 `identity`） | 前端交互变化做浏览器验证 |
| 模板参数、条件块、公共生成逻辑、共享依赖或构建配置 | `test-template-matrix.ps1 -Tier pr` | — |
| 数据库映射、迁移、租户路由 | 受影响场景 + `test-template-postgresql-e2e.ps1` | — |
| 认证、令牌、外部登录协议 | 受影响场景 + `test-template-oidc-e2e.ps1` | 浏览器链路加 `-IncludeBrowserScenarios` |
| Dockerfile、部署资产 | `-Scenarios standalone -ContainerSmokeScenarios standalone` | — |
| CI、矩阵或闸门脚本 | `check-all.ps1` + 被改脚本的自检与夹具 | 范围判定变化运行 `python scripts/test-workflow-change-scope.py`；影响调度时用远端 CI 验收 |

模板矩阵与生成项目的集成测试需要 Docker：集成测试用 Testcontainers 起 PostgreSQL，运行时冒烟先迁移再启动 API。`full` 档在本地只在需要复现合入后失败时执行。未执行的档位与入口须在交付说明里列出。

框架 L0 可收窄到目标类或家族；L1 跑 `dotnet test framework/Leistd.Framework.slnx -c Release`，覆盖其他家族的反向依赖，且保留必要的还原与构建。只使用 `--no-build` 时必须先构建本次源码，不能拿旧程序集验证新改动。

模板维护的 L0 包含生成准备，不能承诺重新生成、还原、构建和测试都在一分钟内。可以在一个已生成的项目中用 `--filter` 或 `--include` 探索行为；最终改动写回模板源，并重新生成受影响场景验证替换、条件和裁剪。生成目录的临时修改不能直接复制回模板。生成后业务项目使用自身测试规范，不执行仓库生成矩阵。

## 编译器、分析器与静态闸门

编译器和官方分析器能完整表达的规则优先在构建中执行，通过 `.editorconfig` 与 Release 的 `TreatWarningsAsErrors` 确保诊断会失败。例如公共 XML 缺失由 CS1591、命名空间由 IDE0130、无用 using 由 IDE0005 承担，不再建同义脚本。

自写闸门只承担它们不能表达的仓库语义：包/家族边界、条件模板的全集求值、随包文档与源码关系、词条/动作码、替换点或精确豁免。判断是否等价须同时核对输入范围、正反例、豁免、退出码和未执行分支；同名诊断或一次构建通过不算等价。官方机制放行而仓库禁止的语义须保留窄规则，不能为了减少闸门而放宽契约。

闸门清单唯一入口是 `scripts/check-all.ps1`，数量不在其他文档或 workflow 另行维护。检查器的正反例自检与生产输入检查保护不同对象，不能互相替代。优化可合并同一输入的重复遍历或索引构建，但仍须执行原自检与完整扫描；不建立没有实测收益的跨 run 索引缓存。 API 漂移入口在同一进程中先执行全部正反例，再以本次源码索引扫描正文；规则失效立即失败，不提供只自检后提前退出的模式。

## 删除与替换的验收

每处删除或替换检查、断言或测试都必须说明：它原来能抓什么缺陷，最终由哪个入口、规则或用例接替。没有接替者就保留。纯规则边界、注册生命周期与幂等、关系型约束和失败路径不能仅凭宿主启动或正常 HTTP 请求成功而删除。

接替关系必须用变异验证证明：在隔离输入中逐项注回旧链能发现的缺陷，记录旧入口和新入口的非零退出码与实际诊断；合法对照与撤销注入后的入口须绿。缓存或调度替换还须验证新增文件、配置/依赖变化、缺失/取消的检查不会产生假绿。仅自检全绿、覆盖率或推理不足以验收删除。最终入口直接使用接替者，不留新旧检查并行的过渡模式或迁移开关。

运行时或测试接替的变异须可编译，先确认构建成功，再确认指定断言失败。`if (false)` / `if (true)` 引发的 CS0162 或其他编译失败不能证明测试接替有效；只有接替者本身是编译器/分析器时，对应编译诊断才是验收证据。共享工作树中的验证可能依赖其他会话未提交的修复，提交前还须验证实际候选快照，不能用工作树全绿代替候选全绿。

## CI 划分与分片

先测作业的运行、队列和依赖，按 DAG 关键路径决定优化顺序，不把所有作业节省的秒数相加当作墙钟收益。独立的包消费、框架契约与真实服务闭环可在同一候选 SHA 上并行，但质量结果须包含它们；拆成独立作业不能变成可选检查。

模板场景分 `pr`、`full` 两档。`full` 档完整分配所有已登记场景；`pr` 档是子集，由 `check-template-scenario-coverage.py` 逐行求值，证明每个条件行都由某个 `pr` 档场景生成，且 24 种参数组合可达的行都有登记场景生成。这是行覆盖：同一产物里几处条件分支一起编译、lint 的组合交互不在 `pr` 档保证之内，由 `full` 档兜底。两档的入选场景都执行原有完整阶段；汇总按本次档位核对场景集合与阶段，漏片、错档、取消和失败都不能通过质量聚合。调整场景档位属于调度，以覆盖闸门通过、`full` 档仍在合入后执行为验收，不逐场景注入缺陷；删除或替换检查本身仍按下节执行变异验收。按实测场景耗时均衡负载，不自动为未来规模引入动态调度器；各作业使用独立 feed/hive/缓存/端口和清理边界。是否增加分片同时评估队列、准备重复与 runner 总时间。发布继续等待同 SHA 的完整质量结果，不解除串行发布锁。

分片按"验证什么"命名并登记说明（`$MatrixSlices`），不用编号；场景定义、档位与分片归属只维护在 `scripts/template-matrix-scenarios.ps1`，`template-slice-plan` 作业从中读出本档分片生成 matrix，不在 workflow 重抄。当前 `pr` 档 3 片、`full` 档 2 片（按服务形态）：`pr` 档有人等待，按墙钟取片数；`full` 档无人等待，按 runner 时间取片数。PR 事件跑 `pr` 档；`release.yml` 复用与手动触发默认跑 `full` 档。`framework-pack` 打包一次并上传当前候选的不可变包 artifact；各分片 `template-slices` 和独立 `package-consumption` 下载到各自私有目录。默认全量消费先核对源码项目与包集完整性，漏包失败；人工 `-PackageIds` 才可缩小消费范围。消费校验与矩阵同时开始，矩阵不等待消费；模板分片、包消费、PostgreSQL 与 OIDC 作业都只读复用同一候选包，不共写包源；后两者的 hive、解包缓存、数据库与清理仍各自隔离，人工独立入口仍可自行打包。全部包的消费者各保留一个独立项目，通过临时 slnx 一次 restore/build 调度；包集合与 DLL/XML/文档检查仍先执行，任何项目失败都使入口失败，不把空消费项目当作实际 API 的依赖完整性证明。原 `template-matrix` 必过检查名作为汇总入口，使用 `always()` 核对必要作业成功与本档每片恰好一份收据的场景集合、阶段和容器责任；缺片、跳过、取消、重复、错档或未登记的片名均失败。结果只在完整执行后写出，文件仅上传验证摘要，不上传生成目录、NuGet 缓存或密钥。

范围裁剪必须基于 PR base/merge-base 到 head 的完整差异及实际依赖，不用单一 `HEAD^` 代替多提交 PR。无法确定范围时全量执行；随包文档、props、lock、脚本与 workflow 都是质量输入。只有接替责任和变异证据完整时才削减组合入口的重复工作。

## 效率证据

使用同输入、同机器或 runner 规格、相同入口与明确的缓存条件，前后各至少三轮，报告全部值和中位数。本机共享负载波动大时，用前后交替的成对测量，并以产物哈希确认两种变体确实不同；调度类改动以至少三次真实 CI 运行的中位验收。含 build 与 `--no-build`、TRX 方法时间和入口墙钟、不同 SHA 的历史 CI、并行阶段不能混算。首轮不清缓存时不称为完全冷启动；跳过用例不称为已经执行。新调度模型与实际执行结果分开记录，实际关键路径变化后重新测量。

CI 墙钟从本次尝试的起点到最后一个必要质量作业的 `completed_at` 计算，不用含收尾时间的 `updated_at`：初次执行用 `created_at`，重跑按各自 `run_started_at` 分别计时，不包含两次尝试之间的间隔。每个作业分别记录启动偏移与 `completed_at - started_at` 的运行时间；启动偏移还包含依赖等待，不能全称 runner 排队。沿依赖链记录前序结束到后序开始的间隔，不与启动偏移重复相加。工作流建立耗时与 runner 排队分开记录。模型必须保留原输入与范围，若未计入建立、队列、artifact、汇总或容器成本，比较时须逐项对齐，不事后修改模型迁就实跑。

本地监听或容器访问受限时，经明确裁决可用同规格的完整远端 CI 验收；须核对实际日志、候选 head 与 PR merge 提交、全部必要作业和场景回执。单次历史前后对比只报告观测收益，不冒称同输入三轮复测或稳定 SLA；同时变更的依赖、运行时代码与 runner 镜像版本须披露。浏览器下载、依赖准备与网络耗时有波动，分片收益不能全部归因于代码优化，也不能仅靠本次较快准备阶段再次增加分片。

官方依据：[微软测试分层与执行时机](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/test-asp-net-core-mvc-apps)、[GitHub 矩阵与失败策略](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/run-job-variations)、[作业依赖与 always](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idneeds)、[SDK 分析器](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview)、[ASP.NET Core 测试层级](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0)、[EF Core 测试选型](https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy)、[xUnit 并行配置](https://xunit.net/docs/config-xunit-runner-json)。
