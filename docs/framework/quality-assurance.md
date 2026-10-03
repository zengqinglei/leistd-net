# 质量检查与验证分工

本文定义 leistd-net 的质量检查、删除验收和 CI 调度原则，不随 NuGet 分发。框架用例的目录、注册契约与覆盖取舍见[开发规范 §7](./development-guide.md#7-测试)，模板层级与 lint 缓存见[模板质量验证](../template/quality-assurance.md)。

## 分层执行与时间预算

按改动选最小充分集合，不在每次改动后跑全量。完整集成测试与全部模板场景交给 CI 和合入后执行，与微软"单元测试每次推送前跑、完整集成测试放构建服务器"的分层一致。

| 档位 | 时机 | 预算 | 内容 |
| --- | --- | --- | --- |
| L0 编辑循环 | 每次小改动 | ≤ 1 分钟 | 受影响项目构建 + `dotnet test <测试项目> --filter "FullyQualifiedName~<类>"`；改了某道闸门就单跑它 |
| L1 阶段完成 / 提交前 | 交评审前 | 框架 ≤ 5 分钟，模板 ≤ 10 分钟 | `check-all.ps1` + 下表按路径选的入口 |
| L2 PR CI | 每次推送 PR | 墙钟 ≤ 8.5 分钟 | 完整静态闸门；依据同候选验证计划执行必要框架测试、包内容/消费、PostgreSQL/OIDC 与模板场景/阶段；未知及共享输入取完整 PR 档 |
| L3 全集 | develop 推送、工作日夜间、发布（main 推送仅在框架或 VERSION 变化时） | 不设严格预算 | 同 L2，模板跑 `full` 档全部场景；发布等待同 SHA 的 L3 结果 |

L1 按改动路径选择入口：

| 改动 | 必跑 | 视情况加跑 |
| --- | --- | --- |
| 只改内部文档或 Skill（CI 白名单见 `ci.yml` 的 `framework-pack/scope`，含根 `skills/`） | `check-all.ps1` | — |
| 随包 `framework/docs/`、模板 `template/docs/` 或项目 Skill | `check-all.ps1` + 打包内容或代表性生成检查 | 影响运行契约时加相应隔离消费或生成场景验证 |
| 框架某组件家族的实现 | 框架全量测试 + `check-all.ps1` | 公共 API、注册、包依赖变化时加打包与 `-PackageIds` 隔离消费；模板消费方式变化时按下一行验证 |
| 模板某个特性（通知、外部登录、本地化）或某种 `ServiceRole` | 含该特性/形态的一个场景 + `identity-all-features` + 关闭侧场景（通常是 `identity`） | 前端交互变化做浏览器验证 |
| 模板参数、条件块、公共生成逻辑、共享依赖或构建配置 | `test-template-matrix.ps1 -Tier pr` | — |
| 数据库映射、迁移、租户路由 | 受影响场景 + `test-template-postgresql-e2e.ps1` | — |
| 认证、令牌、外部登录协议 | 受影响场景 + `test-template-oidc-e2e.ps1` | 浏览器链路加 `-IncludeBrowserScenarios`；令牌到期变更的 `-IncludeExpiryWait` 责任见[模板质量规范](../template/quality-assurance.md) |
| Dockerfile、部署资产 | `-Scenarios standalone -ContainerSmokeScenarios standalone` | — |
| CI、矩阵或闸门脚本 | `check-all.ps1` + 被改脚本的自检与夹具 | 范围/聚合变化运行 `python scripts/test-workflow-change-scope.py`，选测/回执变化运行 `python scripts/test-quality-validation-plan.py`；预检接替变化运行 `python scripts/test-template-source-preflight.py`；影响调度时用远端 CI 验收 |

L0–L2 不以真实时间流逝等待安全有效期（锁定、挑战、令牌寿命、缓存寿命、限频窗口）来验证时间边界：应用控制的判据用官方 `FakeTimeProvider` 或显式时刻在单元、集成测试里验证；端到端只验接线与生效值。跨进程的真实到期只放在 L3：OIDC 端到端的 `-IncludeExpiryWait` 由 `full` 档传入，以快速档（`OAuth__AccessTokenLifetime=00:01:30`）执行撤销到期与交换令牌到期，并从签发的令牌断言快速档已生效。实际 I/O、同步、取消与超时用有上限且观察目标完成的等待，不在此列。生成项目的同一原则见模板 [`testing.md`](../../template/docs/standards/testing.md) §2.2。

模板矩阵与生成项目的集成测试需要 Docker：集成测试用 Testcontainers 起 PostgreSQL，运行时冒烟先迁移再启动 API。`full` 档在本地只在需要复现合入后失败时执行。未执行的档位与入口须在交付说明里列出。

框架 L0 可收窄到目标类或家族；L1 跑 `dotnet test framework/Leistd.Framework.slnx -c Release`，覆盖其他家族的反向依赖，且保留必要的还原与构建。只使用 `--no-build` 时必须先构建本次源码，不能拿旧程序集验证新改动。

模板维护的 L0 包含生成准备，不能承诺重新生成、还原、构建和测试都在一分钟内。可以在一个已生成的项目中用 `--filter` 或 `--include` 探索行为；最终改动写回模板源，并重新生成受影响场景验证替换、条件和裁剪。生成目录的临时修改不能直接复制回模板。生成后业务项目使用自身测试规范，不执行仓库生成矩阵。

## PR 的内部文档例外

直接 PR 的纯内部文档资格只由 [CI 范围步骤](../../.github/workflows/ci.yml) `framework-pack/scope` 的路径白名单决定。它读取 base 到实际候选的完整差异，跨边界重命名按两端处理；空差异、未知路径或无法确定基准均取全集。Framework 随包文档、Template 的全部载荷、脚本、workflow 和根构建输入不属于该例外。手动和复用入口不享受 docs-only，按所选档位执行，发布仍为 full。

docs-only 仍运行完整静态闸门、范围/计划与必过质量汇总；动态作业按规则跳过，汇总报告“不适用”。不使用 workflow 级 `paths-ignore`。范围失败、取消、输出缺失或静态失败均阻止汇总通过；不能把缺少矩阵回执称为执行成功。代码 PR 的责任由下述输入计划决定，不能按文件数或需求大小省略测试。

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

模板场景、档位与分片只维护在 `scripts/template-matrix-scenarios.ps1`。默认人工 PR 档执行六场景完整阶段；full 执行十场景、真实集成及适用容器，发布等待同 SHA 的完整结果。PR 档的条件行覆盖由 `check-template-scenario-coverage.py` 逐行求值；组合交互仍由 full 兜底，不以覆盖率删除测试。

`framework-pack` 无作业依赖，checkout 候选后执行内部文档判定与 `plan-quality-checks.py`，生成绑定 SHA/档位的计划和选定分片。它同时承担范围结果成功责任；docs-only 不安装 SDK、不打包、不上传产物。其他输入只打包一次，immutable artifact 供各消费者只读下载。所有动态作业显式检查范围，不能依赖打包作业被跳过来间接过滤。移除独立规划 runner，避免 pack 等待另一个 runner 的启动和完成。

### 同候选输入计划

选择规则唯一实现为 `scripts/plan-quality-checks.py`；内部文档白名单仍只在 workflow。完整 base→候选差异非空，且只包含支持的前端源码或后端 C# 时，才允许模板阶段裁剪：

| 输入 | Framework 测试 | 包内容/隔离消费 | 模板阶段 | 独立 PG/OIDC |
| --- | --- | --- | --- | --- |
| 仅模板前端 src/public/_mock 的支持文件 | 不适用，输入未变 | 全部包内容；消费构建不适用 | 生成/形态、audit、npm ci、healthcheck、lint、build、spec 发现与真实 Chromium | 保留 |
| 仅模板 backend/src 或 tests 的 C# | 不适用，输入未变 | 全部包内容；消费构建不适用 | 生成/形态、audit、restore/build、真实运行时、后端单元/集成 | 保留 |
| Framework 组件/DDD 项目内局部 C# | 全量 | 全部内容；该包与候选 nuspec 反向传递依赖的隔离消费 | 完整 PR 场景/阶段 | 保留 |
| 跨前后端、共享配置/依赖、随包/生成文档、脚本/workflow、未知输入 | 全量 | 全部内容与全部消费 | 完整 PR 场景/阶段；适用容器 | 保留 |
| 手动/复用/full 或未知/无效 base | 全量 | 全部内容与全部消费 | 所选档位完整场景/阶段 | 保留 |

局部模板场景依据现有 template.json 的 sources/modifiers/computed 条件求文件生产场景，纳入旧/新树两侧与默认/全特性代表；不另建特性目录映射。模板参数、项目/前端配置、依赖变化及未建模的 source/modifier 字段取完整档。选测证明是输入闭包。后端变化仍需真实 PG/OIDC 检查契约，前端 mocks 不能替代该职责；纯前端模式也暂保留 PG/OIDC，因为本轮未逐项完成其所有入口的独立输入闭包证明，不能据此宣称它们不可裁剪。

Framework 依赖闭包只裁剪各自含单一 PackageReference 的空 restore/build 消费项目；全包内容、源码包集、DLL/XML/文档、重复包、缺失候选依赖仍先核对。它不裁剪 Framework 用例、DI/反射/配置语义、模板或服务闭环；出现显式跨项目编译输入时回退全量。人工 `-PackageIds` 保持已有局部入口，CI 使用独立计划并仍要求完整候选 feed。

汇总 `template-matrix` 使用 always，要求静态与范围/打包作业成功，按计划严格核对每个动态作业的 success/skipped，再按独立预期计划核对准确分片、场景、阶段、SHA 与档位。省略阶段写 `not-applicable`，不能用 skipped/pass 冒充执行；失败、取消、意外跳过、缺片、重复、错 SHA/档位/阶段及缺少容器责任全部拒绝。矩阵和检查器共享计划验证入口 `quality-validation-plan.ps1`；默认完整档拒绝局部收据。生成目录、数据库、feed/hive、包解包缓存和端口仍隔离，不共享可变产物。

本地也可显式生成同候选计划：`python scripts/plan-quality-checks.py --tier pr --event pull_request --base <完整SHA> --output .tmp/quality-plan.json`，矩阵传 `-Tier pr -ValidationPlanPath .tmp/quality-plan.json`。默认人工入口不自动推测 base，继续完整执行；不能将局部计划与手动跳过或 -Scenarios 混用。

范围裁剪必须基于 PR base/merge-base 到 head 的完整差异及实际依赖，不用单一 `HEAD^` 代替多提交 PR。无法确定范围时全量执行；随包文档、props、lock、脚本与 workflow 都是质量输入。只有接替责任和变异证据完整时才削减组合入口的重复工作。

同一 CI 候选的模板源码预检由 `docs-sync` 完整运行 `check-all.ps1` 承担，矩阵传入 `-SkipSourcePreflight` 省略重复扫描，并核对总入口仍登记了三个实际预检入口。所有作业显式 checkout 相同候选 SHA；生成可与静态检查并行，必过汇总必须等待并核对范围、静态、打包、Framework 测试、包消费、PostgreSQL、OIDC 和各模板分片全部成功，再核对本档完整回执。独立人工矩阵默认在生成前预检，不使用该 CI 开关。清单存在不能代替缺陷注入和失败传播验证。

## 效率证据

验收先区分工作量变化：docs-only 按真实 PR 的路径判定、必要作业和跳过责任验收；同候选去重按被删除的命令、接替缺陷检测及新增命令/依赖的净成本验收。两者仍完整披露实跑墙钟和 runner 合计，新增可归因成本吞掉节省或拖长关键路径时应修正。仅重排相同工作量的调度才使用固定配对的墙钟与 runner 联合门槛，不能将十秒级去重收益套入高噪声重排实验。以下重复测量原则用于声称定量性能改善和稳定预算，单次真实 PR 仅是观测。

使用同输入、同机器或 runner 规格、相同入口与明确的缓存条件，前后各至少三轮，报告全部值和中位数。本机共享负载波动大时，用前后交替的成对测量，并以产物哈希确认两种变体确实不同；调度类改动以至少三次真实 CI 运行的中位验收。含 build 与 `--no-build`、TRX 方法时间和入口墙钟、不同 SHA 的历史 CI、并行阶段不能混算。首轮不清缓存时不称为完全冷启动；跳过用例不称为已经执行。新调度模型与实际执行结果分开记录，实际关键路径变化后重新测量。

CI 墙钟从本次尝试的起点到最后一个必要质量作业的 `completed_at` 计算，不用含收尾时间的 `updated_at`：初次执行用 `created_at`，重跑按各自 `run_started_at` 分别计时，不包含两次尝试之间的间隔。每个作业分别记录启动偏移与 `completed_at - started_at` 的运行时间；启动偏移还包含依赖等待，不能全称 runner 排队。沿依赖链记录前序结束到后序开始的间隔，不与启动偏移重复相加。工作流建立耗时与 runner 排队分开记录。模型必须保留原输入与范围，若未计入建立、队列、artifact、汇总或容器成本，比较时须逐项对齐，不事后修改模型迁就实跑。

本地监听或容器访问受限时，经明确裁决可用同规格的完整远端 CI 验收；须核对实际日志、候选 head 与 PR merge 提交、全部必要作业和场景回执。单次历史前后对比只报告观测收益，不冒称同输入三轮复测或稳定 SLA；同时变更的依赖、运行时代码与 runner 镜像版本须披露。浏览器下载、依赖准备与网络耗时有波动，分片收益不能全部归因于代码优化，也不能仅靠本次较快准备阶段再次增加分片。

官方依据：[NuGet 实际依赖解析](https://learn.microsoft.com/en-us/nuget/concepts/dependency-resolution)、[微软测试分层与执行时机](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/test-asp-net-core-mvc-apps)、[GitHub 矩阵与失败策略](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/run-job-variations)、[作业依赖与 always](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idneeds)、[SDK 分析器](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview)、[ASP.NET Core 测试层级](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0)、[EF Core 测试选型](https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy)、[xUnit 并行配置](https://xunit.net/docs/config-xunit-runner-json)。
