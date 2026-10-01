# 质量检查与验证分工

本文定义 leistd-net 的质量检查、删除验收和 CI 调度原则，不随 NuGet 分发。框架用例的目录、注册契约与覆盖取舍见[开发规范 §7](./development-guide.md#7-测试)，模板层级与 lint 缓存见[模板质量验证](../template/quality-assurance.md)。

## 编译器、分析器与静态闸门

编译器和官方分析器能完整表达的规则优先在构建中执行，通过 `.editorconfig` 与 Release 的 `TreatWarningsAsErrors` 确保诊断会失败。例如公共 XML 缺失由 CS1591、命名空间由 IDE0130、无用 using 由 IDE0005 承担，不再建同义脚本。

自写闸门只承担它们不能表达的仓库语义：包/家族边界、条件模板的全集求值、随包文档与源码关系、词条/动作码、替换点或精确豁免。判断是否等价须同时核对输入范围、正反例、豁免、退出码和未执行分支；同名诊断或一次构建通过不算等价。官方机制放行而仓库禁止的语义须保留窄规则，不能为了减少闸门而放宽契约。

闸门清单唯一入口是 `scripts/check-all.ps1`，数量不在其他文档或 workflow 另行维护。检查器的正反例自检与生产输入检查保护不同对象，不能互相替代。优化可合并同一输入的重复遍历或索引构建，但仍须执行原自检与完整扫描；不建立没有实测收益的跨 run 索引缓存。 API 漂移入口在同一进程中先执行全部正反例，再以本次源码索引扫描正文；规则失效立即失败，不提供只自检后提前退出的模式。

## 删除与替换的验收

每处删除或替换检查、断言或测试都必须说明：它原来能抓什么缺陷，最终由哪个入口、规则或用例接替。没有接替者就保留。纯规则边界、注册生命周期与幂等、关系型约束和失败路径不能仅凭宿主启动或正常 HTTP 请求成功而删除。

接替关系必须用变异验证证明：在隔离输入中逐项注回旧链能发现的缺陷，记录旧入口和新入口的非零退出码与实际诊断；合法对照与撤销注入后的入口须绿。缓存或调度替换还须验证新增文件、配置/依赖变化、缺失/取消的检查不会产生假绿。仅自检全绿、覆盖率或推理不足以验收删除。最终入口直接使用接替者，不留新旧检查并行的过渡模式或迁移开关。

## CI 划分与分片

先测作业的运行、队列和依赖，按 DAG 关键路径决定优化顺序，不把所有作业节省的秒数相加当作墙钟收益。独立的包消费、框架契约与真实服务闭环可在同一候选 SHA 上并行，但质量结果须包含它们；拆成独立作业不能变成可选检查。

模板分片须完整分配所有已登记场景，每场景仍执行原有阶段；验证分片集合与原全集恰好相等，漏片、取消和失败都不能通过质量聚合。按实测场景耗时均衡负载，不自动为未来规模引入动态调度器；各作业使用独立 feed/hive/缓存/端口和清理边界。是否增加分片同时评估队列、准备重复与 runner 总时间。发布继续等待同 SHA 的完整质量结果，不解除串行发布锁。

当前 CI 用两片，场景定义和分片归属只维护在 `scripts/template-matrix-scenarios.ps1`，不在 workflow 重抄场景清单。`framework-pack` 打包一次并上传当前候选的不可变包 artifact；两片 `template-shards` 和独立 `package-consumption` 下载到各自私有目录。默认全量消费先核对源码项目与包集完整性，漏包失败；人工 `-PackageIds` 才可缩小消费范围。消费校验与矩阵同时开始，矩阵不等待消费；三个消费者不共写包源，不额外重复打包。原 `template-matrix` 必过检查名作为汇总入口，使用 `always()` 核对必要作业成功与两片实际结果的场景全集、阶段和容器责任；缺片、跳过、取消、重复或错片均失败。结果只在完整执行后写出，文件仅上传验证摘要，不上传生成目录、NuGet 缓存或密钥。

范围裁剪必须基于 PR base/merge-base 到 head 的完整差异及实际依赖，不用单一 `HEAD^` 代替多提交 PR。无法确定范围时全量执行；随包文档、props、lock、脚本与 workflow 都是质量输入。只有接替责任和变异证据完整时才削减组合入口的重复工作。

## 效率证据

使用同输入、同机器或 runner 规格、相同入口与明确的缓存条件，前后各至少三轮，报告全部值和中位数。含 build 与 `--no-build`、TRX 方法时间和入口墙钟、不同 SHA 的历史 CI、并行阶段不能混算。首轮不清缓存时不称为完全冷启动；跳过用例不称为已经执行。新调度模型与实际执行结果分开记录，实际关键路径变化后重新测量。

官方依据：[GitHub 矩阵与失败策略](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/run-job-variations)、[作业依赖与 always](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idneeds)、[SDK 分析器](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview)、[ASP.NET Core 测试层级](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0)、[EF Core 测试选型](https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy)、[xUnit 并行配置](https://xunit.net/docs/config-xunit-runner-json)。
