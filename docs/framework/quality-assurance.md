# 质量检查与验证分工

本文定义 leistd-net 的质量检查、删除验收和 CI 调度原则，不随 NuGet 分发。框架用例的目录、注册契约与覆盖取舍见[开发规范 §7](./development-guide.md#7-测试)，模板层级与 lint 缓存见[模板质量验证](../template/quality-assurance.md)。

## 分层执行与时间预算

按变化行为和依赖选择验证，不在每次编辑后跑全量。本地用目标单测和必要的真实集成定位失败，PR CI 承担完整适用回归；模板全集由合入后与发布验证。生成后的业务项目按自身[测试规范](../../template/docs/standards/testing.md)执行，未配置必过 CI 时不能把完整责任交给 CI。

| 档位 | 时机 | 预算 | 内容 |
| --- | --- | --- | --- |
| L0 编辑循环 | 每次小改动 | ≤ 1 分钟 | 受影响项目构建 + `dotnet test <测试项目> --filter "FullyQualifiedName~<类>"`；改了某道闸门就单跑它 |
| L1 阶段完成 / 推送前 | 完整改动形成后 | 框架 ≤ 5 分钟；模板以实测为准，通常目标 ≤ 10 分钟 | `check-all.ps1` + 下表按路径选的入口；不为预算跳过检查 |
| L2 PR CI | 每次推送 PR | 墙钟 ≤ 8.5 分钟 | 完整静态闸门；依据同候选验证计划执行必要框架测试、包内容/消费、PostgreSQL/OIDC 与模板场景/阶段；未知及共享输入取完整 PR 档 |
| L3 全集 | develop 推送、工作日夜间、发布（main 推送仅在框架或 VERSION 变化时） | 不设严格预算 | 同 L2，模板跑 `full` 档全部场景；发布等待同 SHA 的 L3 结果 |

L1 按改动路径选择入口：

| 改动 | 必跑 | 视情况加跑 |
| --- | --- | --- |
| 只改内部文档或 Skill（CI 白名单见 `ci.yml` 的 `framework-pack/scope`，含根 `skills/`） | `check-all.ps1` | — |
| 随包 `framework/docs/`、模板 `template/docs/` 或项目 Skill | `check-all.ps1` + 打包内容或代表性生成检查 | 影响运行契约时加相应隔离消费或生成场景验证 |
| 框架某组件家族的实现 | 框架测试全集 + `check-all.ps1` | 公共 API、注册、包依赖变化时加打包与 `-PackageIds` 隔离消费；模板消费方式变化时验证实际生成产品 |
| 模板支持的局部前端/后端源码，含文件内条件块 | 按下节计算场景，执行每个产品的完整适用阶段 | 前端交互变化做浏览器验证；真实依赖变化加目标集成 |
| 模板参数/符号、computed、modifiers、公共生成逻辑、共享依赖、构建配置或未知输入 | `test-template-matrix.ps1 -Tier pr` | 静态条件覆盖辅助定位，不能替代实际生成、lint、构建和行为验证 |
| 数据库映射、迁移、租户路由 | 受影响场景 + `test-template-postgresql-e2e.ps1` | — |
| 认证、令牌、外部登录协议 | 受影响场景 + `test-template-oidc-e2e.ps1` | 浏览器链路加 `-IncludeBrowserScenarios`；令牌到期变更的 `-IncludeExpiryWait` 责任见[模板质量规范](../template/quality-assurance.md) |
| Dockerfile、部署资产 | `-Scenarios standalone -ContainerSmokeScenarios standalone` | — |
| CI、矩阵或闸门脚本 | `check-all.ps1` + 被改脚本的自检与夹具 | 范围/聚合变化运行 `python scripts/test-workflow-change-scope.py`，选测/回执变化运行 `python scripts/test-quality-validation-plan.py`；预检接替变化运行 `python scripts/test-template-source-preflight.py`；影响调度时用远端 CI 验收 |

L0–L2 不以真实时间流逝等待安全有效期（锁定、挑战、令牌寿命、缓存寿命、限频窗口）来验证时间边界：应用控制的判据用官方 `FakeTimeProvider` 或显式时刻在单元、集成测试里验证；端到端只验接线与生效值。跨进程的真实到期只放在 L3：OIDC 端到端的 `-IncludeExpiryWait` 由 `full` 档传入，以快速档（`OAuth__AccessTokenLifetime=00:01:30`）执行撤销到期与交换令牌到期，并从签发的令牌断言快速档已生效。实际 I/O、同步、取消与超时用有上限且观察目标完成的等待，不在此列。生成项目的同一原则见模板 [`testing.md`](../../template/docs/standards/testing.md) §2.2。

模板矩阵与生成项目的集成测试需要 Docker：集成测试用 Testcontainers 起 PostgreSQL，运行时冒烟先迁移再启动 API。`full` 档在本地只在需要复现合入后失败时执行。未执行的档位与入口须在交付说明里列出。

### 框架与真实依赖

框架 L0 可收窄到目标类或家族；L1 跑 `dotnet test framework/Leistd.Framework.slnx -c Release`，覆盖其他家族的反向依赖，保留必要的还原与构建。该全集包含关系型 Provider、TestServer 和真实 Redis 契约，不全是纯单测。本地缺 Redis 时按现有规则显式跳过并列为未执行；变化涉及锁实现或 Redis 接线时，必须在可达环境验证相关契约。无关的纯内存规则不因此启动 Redis。

纯领域拒绝或参数判断用单测；数据库事务、HTTP 授权、协议和网络取消等依赖协作契约用目标真实集成。CI 承担完整回归，不意味着已知失败要等到 CI 才第一次复现。`--no-build` 必须先构建本次源码。

### 模板本地场景集合

此入口用于模板维护的局部源码；框架、仓库脚本或文档任务按上表选择入口。L1 使用完整任务基线到当前候选的差异，只支持干净、已提交的局部前端/后端源码，也支持两者混合。默认产物、全特性产物与全部文件产出场景均纳入，复用既有 planner、条件引擎和登记清单。

```powershell
$taskBase = git merge-base origin/develop HEAD
python3 scripts/plan-quality-checks.py --local-scenarios --tier pr --base $taskBase --output .tmp/local-template-scenarios.json
$selection = Get-Content .tmp/local-template-scenarios.json -Raw | ConvertFrom-Json
& ./scripts/test-template-matrix.ps1 -Scenarios @($selection.Scenarios)
```

以上在 PowerShell 执行，Windows 使用本机实际 Python 命令。先更新远端引用；也可使用记录的任务基线完整 SHA，须覆盖全部任务提交。本地集合只选择产品，每个产品仍运行全部适用阶段，不传 `-ValidationPlanPath` 或跳过开关；该输出不能当作 CI 阶段裁剪计划。

文件内条件的可达分支由全部产出场景覆盖；文件被裁剪的产品输入未变，不额外加入仅用于关闭侧的产品。元数据/共享/未知输入、无效基线、删除或重命名无法证明、未建模规则、脏树或选择期间输入变化，均退回登记的完整 PR 集合并说明原因。集合不一定减少；静态覆盖不替代实际生成后的 lint、构建和测试。

模板维护的 L0 包含生成准备，不能承诺重新生成、还原、构建和测试都在一分钟内。可以在一个已生成的项目中用 `--filter` 或 `--include` 探索行为；最终改动写回模板源，并重新生成受影响场景验证替换、条件和裁剪。生成目录的临时修改不能直接复制回模板。生成后业务项目使用自身测试规范，不执行仓库生成矩阵。

第三方 SDK 不提供可注入时间时，重复的故障场景可在测试宿主通过官方配置缩短传输等待；默认生产超时由独立组合契约验证，并保留一个真实超时行为用例。共享异步工作使用开始／释放信号与显式取消，保护性超时只拒绝挂起；入口观察不能冒称 SDK 内部等待者观察，仍需取消隔离、共享结果、请求次数和故障反例证据。测试缓存 TTL 时，计算与实际本地缓存到期共用同一官方假时钟，不能只快进一层或主动删除条目。

### 审查与失败后的重跑

完整可审查改动形成后，按用户或团队流程审查代码及文档一致性，可与 PR CI 并行。公共 API、权限、事务或迁移等难返工的决策可提前审查；普通小 bug 不强制增加方案会签。

失败时先复现目标路径，修复后运行目标验证及受影响回归。矩阵失败使用 `-Scenarios <失败场景>` 定位，不靠猜测反复推送。审查者复核有效证据，不因更换评审者重跑无关全套；新增提交均复核增量，相关代码、配置、依赖、fixture 或平台变化使原证据失效。

审查覆盖最终 head 与基线，CI 核对实际执行的 merge 候选及其 head/base 关系；不能机械要求二者 SHA 相同。base 更新后重验融合候选，发布使用最终合入 SHA。本地结果记录命令、范围、环境、实际结果及 SHA/未提交 diff 摘要；跳过不等于通过，Mac 结果不替代 Linux CI。

规则依据：[Anthropic 长任务应用开发](https://www.anthropic.com/engineering/harness-design-long-running-apps)、[Claude Code 最佳实践](https://code.claude.com/docs/en/best-practices)。验证围绕可观察完成条件与真实结果，不固定增加评审代理或逐阶段会签。

## PR 的内部文档例外

直接 PR 的纯内部文档资格只由 [CI 范围步骤](../../.github/workflows/ci.yml) `framework-pack/scope` 的路径白名单决定。它读取 base 到实际候选的完整差异，跨边界重命名按两端处理；空差异、未知路径或无法确定基准均取全集。Framework 随包文档、Template 的全部载荷、脚本、workflow 和根构建输入不属于该例外。手动和复用入口不享受 docs-only，按所选档位执行，发布仍为 full。

docs-only 仍运行完整静态闸门、范围/计划与必过质量汇总；动态作业按规则跳过，汇总报告“不适用”。不使用 workflow 级 `paths-ignore`。范围失败、取消、输出缺失或静态失败均阻止汇总通过；不能把缺少矩阵回执称为执行成功。代码 PR 的责任由下述输入计划决定，不能按文件数或需求大小省略测试。

## 编译器、分析器与静态闸门

编译器和官方分析器能完整表达的规则优先在构建中执行，通过 `.editorconfig` 与 Release 的 `TreatWarningsAsErrors` 确保诊断会失败。例如公共 XML 缺失由 CS1591、命名空间由 IDE0130、无用 using 由 IDE0005 承担，不再建同义脚本。

自写闸门只承担它们不能表达的仓库语义：包/家族边界、条件模板的全集求值、随包文档与源码关系、词条/动作码、替换点或精确豁免。判断是否等价须同时核对输入范围、正反例、豁免、退出码和未执行分支；同名诊断或一次构建通过不算等价。官方机制放行而仓库禁止的语义须保留窄规则，不能为了减少闸门而放宽契约。

闸门清单唯一入口是 `scripts/check-all.ps1`，数量不在其他文档或 workflow 另行维护。检查器的正反例自检与生产输入检查保护不同对象，不能互相替代。优化可合并同一输入的重复遍历或索引构建，但仍须执行原自检与完整扫描；不建立没有实测收益的跨 run 索引缓存。 API 漂移入口在同一进程中先执行全部正反例，再以本次源码索引扫描正文；规则失效立即失败，不提供只自检后提前退出的模式。

## 定时任务的覆盖分工

通用调度行为由 BackgroundJobs 组件集中验证：真实 `RecurringJobScheduler` 配合官方 `FakeTimeProvider`、内存依赖与计时器登记/完成信号，覆盖每日排期的未到点、到点、下一轮、全局/单任务禁用、非法排期隔离、等待/执行中的停止。先观察后台初始化完成再推进时钟；停止后同时核对执行任务正常完成与没有新增运行。真实周期等待不加入编辑或提交入口，保护性超时只用于拒绝卡死。

Job 的集群锁、水位和失败重试仍由执行器契约测试承担。模板和业务项目只补自身业务判据、失败传播以及真实持久化/租户/DI 接线，不对每个 Job 复制通用调度循环测试。服务成功启动不能替代定向行为断言；新增测试复用现有测试项目与 CI，不另建定时维护流水线。

## 删除与替换的验收

每处删除或替换检查、断言或测试都必须说明：它原来能抓什么缺陷，最终由哪个入口、规则或用例接替。没有接替者就保留。纯规则边界、注册生命周期与幂等、关系型约束和失败路径不能仅凭宿主启动或正常 HTTP 请求成功而删除。

接替关系必须用变异验证证明：在隔离输入中逐项注回旧链能发现的缺陷，记录旧入口和新入口的非零退出码与实际诊断；合法对照与撤销注入后的入口须绿。缓存或调度替换还须验证新增文件、配置/依赖变化、缺失/取消的检查不会产生假绿。仅自检全绿、覆盖率或推理不足以验收删除。最终入口直接使用接替者，不留新旧检查并行的过渡模式或迁移开关。

运行时或测试接替的变异须可编译，先确认构建成功，再确认指定断言失败。`if (false)` / `if (true)` 引发的 CS0162 或其他编译失败不能证明测试接替有效；只有接替者本身是编译器/分析器时，对应编译诊断才是验收证据。共享工作树中的验证可能依赖其他会话未提交的修复，提交前还须验证实际候选快照，不能用工作树全绿代替候选全绿。

## CI 划分与分片

先测作业的运行、队列和依赖，按 DAG 关键路径决定优化顺序，不把所有作业节省的秒数相加当作墙钟收益。独立的包消费、框架契约与真实服务闭环可在同一候选 SHA 上并行，但质量结果须包含它们；拆成独立作业不能变成可选检查。

模板场景、档位与分片只维护在 `scripts/template-matrix-scenarios.ps1`。默认人工 PR 档执行登记的 PR 场景完整阶段；full 执行登记全集、真实集成及适用容器，发布等待同 SHA 的完整结果。场景数量不在本文重复维护，以该脚本的档位与分片清单为准。PR 档的条件行覆盖由 `check-template-scenario-coverage.py` 逐行求值；组合交互仍由 full 兜底，不以覆盖率删除测试。

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
