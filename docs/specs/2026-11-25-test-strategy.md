# 测试分层预算、E2E 准入、逐条耗时与缓存时钟

状态：已完成 · 风险档：高（跨框架与模板，改变默认运行语义） · 关联：[PR #321](https://github.com/zengqinglei/leistd-net/pull/321)

## 1. 需求

### 问题与背景

一个下游业务项目（三个仓库，都由模板生成）报告了 11-24～25 两天的实测教训：

- **E2E 失控。** 单个需求跑一次完整档 E2E 约 90 分钟。PR 档用例一个多月内从 31 条涨到 69 条，没有上限。
- **CI 耗时与写盘。**
  - 拆分并行后，每个作业各自还原和构建；每个测试类又克隆一个库，节点写盘打满。
  - 耗时预算只算测试步骤，没算还原和构建。
- **不稳定用例。** 有四条，都依赖墙钟或时序。其中一条的原因是 HybridCache 本地层不跟随测试宿主的 `FakeTimeProvider`。

核对本仓库现状：

- **框架：**
  - .NET 10 的 `MemoryCache` 只认 `MemoryCacheOptions.Clock`（`ISystemClock`）。
  - `MemoryDistributedCache` 用 `MemoryDistributedCacheOptions`（继承前者）构造一个独立的 `MemoryCache`。
  - HybridCache 一级缓存复用容器里的 `IMemoryCache`。
  - 三者的过期都不跟随容器里的 `TimeProvider`。
  - 框架和模板的测试里有多份手写的时钟适配器，覆盖了这两种 options，还有直接构造缓存的写法。
  - `AddDddInfrastructure` 已经把 `IClock` 接到 `TimeProvider`，模板各形态的宿主都会调用它。
- **模板测试规范：** `template/docs/standards/testing.md` 已有按风险选测试、E2E 三档和 E2E 范围原则。缺的是：
  - 分层预算；
  - 适用于所有层的精简口径；
  - 「一个实体只读一次时钟」「不断言墙钟内完成量」两条。
- **模板 `verify.ps1`：**
  - 不输出逐步耗时；
  - MR/PR 上不按改动选步骤；
  - 不产出逐条用例耗时的报告：后端只有总数；
  - 浏览器在 CI 的 before_script 里无条件安装。
- **模板测试夹具：**
  - **集成测试：** PostgreSQL 已用非持久化参数（`fsync=off` 等），但数据目录在容器磁盘上；先迁移一个模板库，再用默认的 `WAL_LOG` 为每个宿主克隆。
  - **E2E：** 直接迁移一个库，不克隆。
- **模板 E2E：**
  - 只在本地身份形态存在；
  - 已有 `@auth`、`@crud` 分组标签（打在 `describe` 上）、30 秒单例超时和 CI 上的整轮超时；
  - 没有「每条只证明一类跨进程接线」的准入核对，也没有数量上限。

### 取舍依据

- `docs/architecture/design-principles.md` §5。
- 模板按派生项目长大后的终局给规范，不按模板自带示例的体量取舍。
- **优先官方机制**：
  - Options 默认填充；
  - Playwright 标签与 `--list`，Playwright 和 Vitest 的 junit reporter；
  - Testcontainers tmpfs；
  - PostgreSQL `CREATE DATABASE … STRATEGY`；
  - GitLab `artifacts:reports:junit` 与 MR 预定义变量，GitHub 事件语义；
  - GitLab 文档推荐的 JunitXml.TestLogger。
- **不过度设计**：
  - 闸门只核对能证伪的判据：标签、枚举、数量；
  - 接线分类是否正确，留给 PR/MR 审查；
  - 一个数值只在一处维护。
- **只修默认就会出错的**：不为极少见的边缘或竞态场景加防护；判定不了的改动按全量处理。
- **显式选择给文档**：模板不知道项目的合并方式，这类内容只写文档。

### 目标与非目标

- **目标：**
  - 框架：缓存过期默认跟随宿主的 `TimeProvider`。
  - 模板：规范有分层预算和精简口径；E2E 有准入和上限；验证脚本按改动选步骤，输出逐步与逐条耗时；测试库写盘更少。
- **非目标：**
  - 不拆 CI 作业，仍然是一个 `verify` 入口。
  - 不让完整档 E2E 脱离合并关卡。
  - 不做 E2E 的 `lowerCoverage` 字段。
  - 不做 p95 统计。
  - 模板默认不去掉合入后的验证。
  - 宿主完全不执行 Leistd 登记时，不保证缓存时钟。

### 功能要求与验收

#### R1 缓存时钟（框架）

**适用范围：** 宿主执行了 `AddDddInfrastructure`。它和 `IClock` 的接线在同一处，与缓存的登记顺序无关。

- **Given / When / Then：**
  - **Given** 宿主执行了 `AddDddInfrastructure`，并用 `RemoveAll<TimeProvider>()` 换成 `FakeTimeProvider`。
  - **When** 假时钟推进越过 TTL。
  - **Then** 以下三类条目都已过期，各有一条修复前失败的测试：
    - `IMemoryCache` 条目；
    - HybridCache 一级条目；
    - `AddDistributedMemoryCache` 提供的 `IDistributedCache` 条目。
- **两种 options 都接：**
  - 采用官方 Options 默认填充：只在 `Clock` 为空时填入接到 `TimeProvider` 的时钟。
  - 宿主显式设置的 `Clock`，无论在框架登记之前还是之后设置，都保留。
- **生产环境：** 用 `TimeProvider.System`，行为不变。
- **测试侧的边界：**
  - DDD 基座和生成项目的集成测试验证宿主默认接线，删去这些宿主的重复适配器。生成项目里直接构造真实缓存的单元测试保留显式时钟，应用层用例独立验证自身行为。
  - ServiceClient、MultiTenancy、Settings 三个独立组件的测试项目不引用 DDD，以守住依赖方向闸门，也保持各自的覆盖边界。它们改用 `Leistd.TestBase` 里的一份共享测试时钟，显式配置真实缓存，并删除各项目重复的适配器。
- **同步文档：**
  - 契约写进 DDD 组合的随包文档（`AddDddInfrastructure` 的登记面）；
  - 模板 `testing.md` §2.2 的缓存说明相应简化。

#### R2 分层预算（模板规范）

`testing.md` §1 的耗时表改成分层表，写明每层证明什么、何时跑、默认预算：

| 层 | 默认预算 |
| --- | --- |
| 编辑中 | ≤1 分钟 |
| 推送前 | ≤3 分钟 |
| 合并关卡（含它所跑的 E2E） | 作业墙钟 ≤10 分钟，含还原、构建与浏览器安装 |

- 现有的 Playwright 整轮超时保持不变，它与这个预算一致。
- 关卡超预算时，项目先收窄范围：E2E 改跑受影响分组，完整档另设阶段。完整档另设阶段后的预算，由项目写进 `docs/project/testing.md`，模板不预设第二个数值。
- 单元和集成测试只设耗时预算，不设数量上限。
- 不靠反复重跑。

#### R3 精简口径（模板规范）

`testing.md` §4 的「下沉或删除」适用于所有层：

- **只按五类处理：**
  1. 重复覆盖；
  2. 已被下层覆盖；
  3. 测实现细节（例如 `as unknown as` 调内部成员）；
  4. 已删功能的遗留；
  5. 只测框架或第三方本身。
- **替代覆盖：** 第 1～3 类在 PR/MR 描述里指出替代覆盖；第 4、5 类写明删除理由即可，不要求补测试。
- **关键路径：** 认证授权、租户隔离、合规判定只下沉、不删除，先补下层再删上层。

#### R4 时间规则（模板规范，只写文档）

与后端既有的 `IClock` 规则对齐，补两句：

- 同一实体的多个时间字段取同一次时钟读数，前后端都适用。
- 推进类用例按条数或可控时钟断言，不断言墙钟时间内的完成量。

#### R5 E2E 准入（模板 E2E + 闸门，只在有 E2E 的形态生成）

- **用例发现：**
  - 以 Playwright 的发现结果为准：`playwright test --list` 的结构化输出，含 `describe` 上的标签和参数化用例。
  - 不解析源码文本。
  - 发现不启动浏览器和数据库，只需要已安装的前端依赖。
- **判据：**
  - 每条用例的有效标签里，恰好有一个 `@proves:<类别>`，类别属于固定集合：auth-session、service-call、gateway-route、deploy-orchestration、realtime、main-flow。
  - 缺标签、多个、或不在集合里，都失败。
  - 用例总数超过上限也失败。
  - 上限只在闸门所读的一处登记，默认值为模板现有用例数加少量余量。
- **范围：** 闸门只核对标签、枚举和数量；分类是否正确，由 PR/MR 审查负责。
- **运行时机：**
  - 闸门只做用例发现，不执行 E2E。
  - `-Fast`：在本地身份形态运行闸门，使用已安装的依赖。
  - 完整验证：E2E 被选中时运行闸门，紧跟前端依赖安装。
  - 只改文档时跳过。
- **自检：** 带 `--self-test`，至少覆盖缺标签、多标签、非法类别、超上限四种失败。
- **现有用例：** 全部按实际证明的接线打上标签。

#### R6 并发约定（模板规范，只写文档）

- 沿用 Playwright 默认的文件间并发。
- 文件内改成串行，或用独占 worker 的，必须注释写明改了哪项全局状态、为什么不能用自建租户隔离。

#### R7 逐步耗时（模板 `verify.ps1`）

- 每一步结束时输出该步耗时，最后汇总各步耗时和总墙钟；只输出，不阻断。
- 浏览器安装改为 `verify.ps1` 里的一步，由同一份步骤选择驱动：只在实际运行浏览器单测或 E2E 时安装。
- CI 的 before_script 不再安装浏览器，只准备系统依赖。

#### R8 按改动选步骤（模板 `verify.ps1` + CI）

**事件与范围：**

| 事件 | 范围 |
| --- | --- |
| GitLab MR 流水线、GitHub `pull_request` | 按改动选择 |
| GitHub `merge_group`、GitLab 合并结果或合并队列流水线（项目启用时） | 全量 |
| 默认分支推送 | 全量 |
| GitLab 手动（`web`）、GitHub `workflow_dispatch`（模板若有） | 全量 |
| 定时 | 维持现状：只做依赖审计 |
| 本地不带参数 | 全量 |

**改动基准：**

- **GitHub `pull_request`：** 检出的是 PR 合成提交，用 `base.sha..HEAD` 计算被验证树的增量。
- **GitLab MR：** 用 `CI_MERGE_REQUEST_DIFF_BASE_SHA..CI_COMMIT_SHA`。
- 两种平台都把基准作为参数传给 `verify.ps1`，由脚本计算改动。
- 浅克隆里取不到基准时先尝试取回，仍取不到就全量。
- 用 `--no-renames` 计算改动，这样删除和跨目录改名的新旧两侧都参与选择。

**路径到步骤的映射：**

- 一张表，写在 `verify.ps1` 里：
  - 后端：`backend/**`。
  - 前端：`frontend/**`。
  - 只改文档：`*.md`、`docs/**`。
  - 其余路径（根目录脚本、CI 文件、`template/.leistd` 等）无法判定，按全量处理。
- 静态闸门每次都跑。
- 只改后端：跳过前端的 lint 和单测。
- 只改前端：跳过后端的格式检查和测试项目。
- 只改文档：只跑静态闸门。
- **E2E 的前置：**
  - 有 E2E 的形态，前后端任一改动都选中 E2E，并自动带入它的前置步骤：后端还原、Release 构建，前端依赖安装与生产构建，以及 DbMigrator 产物（以 `global-setup.ts` 实际检查的为准）。
  - 没有 E2E 的形态，可以完整跳过另一端。
- 保留现有的顺序和失败即停契约。

**可核对：** `-List` 能接收一组改动路径并输出选择结果。仓库的模板矩阵与生成检查要断言以下几类的选择：
- 只改后端；
- 只改前端；
- 只改文档；
- 改了根目录脚本；
- 删除；
- 跨目录改名。

#### R9 测试库写盘（模板夹具）

- **集成测试：** 在同一节点、同一测试全集上，按三档分别测步骤耗时：
  1. 基线；
  2. 数据目录挂 tmpfs；
  3. tmpfs 加 `STRATEGY FILE_COPY`。
- **按实测保留：**
  - tmpfs 不变慢就保留。
  - `FILE_COPY` 在克隆前后各触发一次检查点，要单独判断：第 3 档不慢于第 2 档才保留。
  - 若淘汰 tmpfs，再在基线上单独测一次 `FILE_COPY`。
- **E2E：** 数据目录挂 tmpfs，路径按 `postgres:18-alpine` 镜像的实际数据目录。验收为确认数据目录确实在 tmpfs 上，并且 E2E 通过。
- **文档：** `testing.md` 补一句：分片前先确认依赖缓存在节点上。

#### R10 逐条耗时（模板 `verify.ps1` + CI）

- **报告：**
  - 后端测试用 JunitXml.TestLogger 产出 JUnit，前端单测和 E2E 用各自的 junit reporter，统一写进一个报告目录。
  - `verify` 开始时清空该目录，上一轮的报告不会混入本轮。
- **上报：**
  - GitLab 形态通过 `artifacts:reports:junit` 上报，`when: always`；
  - GitHub 形态在 `if: always()` 下作为产物上传。
- **慢用例汇总：**
  - `verify` 结束时，无论成败，按层列出最慢的若干条，超阈值输出警告。
  - 汇总本身不改变退出码：失败的验证仍以原步骤的退出码退出。
- **阈值：** 只有一个可执行来源，即汇总代码文件头的常量表。`testing.md` 引用该位置，不另写数值。生成项目按需改这一处即可覆盖。默认值：
  - 单元（前后端）：超过 300 ms 警告（Vitest 默认值；目标 ≤100 ms，来自 Feathers）；
  - 集成：单条超过 5 秒警告；
  - E2E：超过 15 秒警告。

#### R11 合入后不重跑（模板规范，只写文档）

`testing.md` §1.1 写明跳过合入后验证的前提：默认分支的每个变更都经过合并关卡，而且所发布的树就是关卡验证过的那棵树。具体到两种平台：

- **GitLab：**
  - 强制快进合并，并开启 Pipelines must succeed。
  - 不启用服务端自动 rebase：官方说明它不会对 rebase 后的结果重跑 CI。
  - 完整档 E2E 的合并责任仍要由关卡承担：按改动选择时，前后端任一改动都会跑完整 E2E。
- **GitHub：** merge queue 设为必需，并把 `merge_group` 上的完整 `verify` 设为必过检查。

## 2. 方案

### 契约

- **框架：**
  - `AddDddInfrastructure` 的登记面新增两项：`MemoryCacheOptions` 和 `MemoryDistributedCacheOptions` 的时钟默认填充（只填空值）。
  - 不新增公共类型，不新增包，不新增包依赖：`Leistd.Ddd.Infrastructure` 已经经多租户核心包引用了 Caching.Memory。实施时核实；若属间接依赖，改为显式引用。
- **模板 `verify.ps1`：**
  - 新增改动基准参数，`-List` 支持传入改动路径；不带参数时行为与现在相同，即全量。
  - 新增步骤：浏览器安装、E2E 准入闸门。
  - 新增报告目录和慢用例汇总。
- **模板 CI：**
  - 两种形态都传入改动基准；
  - 去掉 before_script 里的浏览器安装；
  - 上报 JUnit。
- **模板夹具：** PostgreSQL 数据目录与克隆策略按 R9 的实测决定。

### 回滚

各部分相互独立，可以单独回退：

- **框架：** 删掉 options 默认填充即可恢复原行为；测试改回显式时钟。
- **按改动选择：** CI 不再传基准即回到全量，不需要改脚本。
- **tmpfs / `FILE_COPY`：** 各是一行配置，单独撤回。
- **E2E 闸门：** 从步骤表移除，标签保留无害。
- **JUnit：** 去掉 logger 参数和上报配置即可。

## 3. 计划

| 步骤 | 内容 | 验证 |
| --- | --- | --- |
| 1 | R1 框架：options 默认填充；DDD 测试中的三类到期测试（先失败）；`Leistd.TestBase` 共享测试时钟，替换三个组件测试项目里的适配器；随包文档 | 受影响框架测试项目全量；打包到 `.tmp/local-feed`；包消费检查 |
| 2 | R1 模板：集成测试改走默认接线；直接构造缓存的单测保留显式时钟，同步 §2.2 | 受影响生成场景的单元与集成测试全量 |
| 3 | R9：三档实测与取舍；E2E 挂 tmpfs | 同一节点、同一全集，各档记录步骤耗时；确认 E2E 数据目录在 tmpfs 上 |
| 4 | R7、R8、R10：`verify.ps1`、两种 CI 文件、JUnit、汇总；同步仓库的矩阵与生成检查（`-List`、失败传播、CI 形态断言） | `-List` 覆盖 R8 的六类改动；可控失败一次，证明保留退出码、本轮 JUnit 已上传且不混入上轮；跨两次运行 |
| 5 | R5：现有用例打标签，加闸门、上限和自检；同步生成检查 | `--self-test`；故意缺标签和超上限各失败一次；无 E2E 形态不生成闸门也不登记步骤 |
| 6 | R2～R4、R6、R11：规范条文 | 模板规范读取成本闸门；`scripts/check-all.ps1` |
| 7 | 端到端闭环 | 见下 |

端到端闭环：

- **生成形态：** 至少实际生成 GitHub 和 GitLab 各一种形态，并覆盖两类场景：
  - 本地身份全功能（有 E2E）：真实运行完整 `verify`；
  - Resource（无 E2E）：确认闸门没有生成，步骤也没有登记。
- **干净工作区单端改动：** 在无构建产物的干净工作区里，分别以「只改后端」「只改前端」的改动基准运行 `verify`，证明选中的 E2E 自动带入了另一端的前置产物并通过。
- **报告：** 确认逐步耗时、慢用例汇总和 JUnit 都已产出，缓存时钟用例全部通过。
- **模板包：** 运行 `pwsh template-package/test-template-package.ps1`，确认重建零差异。

## 4. 验证

### R1 框架时钟

- 修复前，在同一份候选源码中只添加回归用例，运行 `dotnet test` 的 `CacheClockRegistrationTests`。普通内存缓存、HybridCache 一级和内存分布式缓存的到期断言均失败，两条宿主显式时钟优先用例通过。
- 修复后，以 Release 跑受影响项目全量，DDD Infrastructure 92、MultiTenancy 458、ServiceClient 226、Settings 215，共 991 条通过。
- `pwsh framework/build/pack-local-feed.ps1` 打包 82 个候选包。按仓库指定的独立 NuGet 解包目录运行 `pwsh framework/build/test-package-consumption.ps1` 通过，编译 28 个项目中的 165 段文档示例，6 个反例均被拒绝。
- 框架提交为 `7cfa8ecf`，注册契约已同步到 DDD 随包文档。

### R1 生成项目验证

通过 `pwsh scripts/template-dev-workspace.ps1 -Scenario <场景> -Sync` 使用候选包同步模板。Release 后端测试中，`identity-workspace` 通过 361 条单测与 872 条集成测试，`resource-workspace` 通过 201 条单测与 278 条集成测试，覆盖两端缓存时钟。`identity-all-features` 的集成测试全集为 841 条，三档计时见下文。

邮箱挑战单测保留显式连接假时钟的真实内存分布式缓存。它验证配额行为，宿主的 Options 默认接线由基座和生成项目的集成测试验证。

### R1 单条耗时

Release 构建独立计时，逐条耗时取自 `dotnet test -c Release --no-build --logger trx`。每个版本各重复三轮。ServiceClient 的 8 条用例和 MultiTenancy 的 24 条在本机对照，后者交错运行前后两份产物。Settings 的 20 条在 WSL 节点交错对照。生成项目 `identity-all-features` 的六轮均运行全部 361 条，表中列邮箱挑战类 24 条。该类经复审恢复至改动前源码，用同一份构建分别采样。

最初本机整批测量中，多租户目录读取有一次 365.580 ms。改成交错测量后，该例中位数从 116.219 降到 81.953 ms，候选最大 168.315 ms。Settings 旧夹具交错对照中冷读取有两次超过 300 ms。恢复容器的原有校验，对无日志断言的用例只注入空日志工厂后，Settings 全集 215 条通过，20 条的所有候选样本均低于 300 ms。冷读取中位数为 114.317 → 129.869 ms，基线与候选均超过 100 ms 目标。

邮箱挑战类的密码重置用例前后各有超过 300 ms 的样本。两组使用相同源码与同一二进制，结果包含本机负载下的调度波动。下表保留每条用例的中位数与最大值，单位为毫秒。

| 用例 | 改动前中位数（ms） | 改动前最大值（ms） | 改动后中位数（ms） | 改动后最大值（ms） |
| --- | ---: | ---: | ---: | ---: |
| Core.RemoteCachingAndSingleFlightTests.A_pending_route_returns_stale_within_one_second_and_throttles_all_waiters | 32.120 | 71.604 | 31.825 | 87.411 |
| Core.RemoteCachingAndSingleFlightTests.A_route_expires_from_memory_at_its_hard_deadline | 4.466 | 6.687 | 2.376 | 2.610 |
| Core.RemoteCachingAndSingleFlightTests.Bounded_route_waiting_preserves_results_deadlines_and_cancellation(scenario: "cancellation") | 45.578 | 48.654 | 26.595 | 32.097 |
| Core.RemoteCachingAndSingleFlightTests.Bounded_route_waiting_preserves_results_deadlines_and_cancellation(scenario: "cold") | 22.032 | 86.962 | 7.981 | 14.963 |
| Core.RemoteCachingAndSingleFlightTests.Bounded_route_waiting_preserves_results_deadlines_and_cancellation(scenario: "deadline") | 42.290 | 66.874 | 9.022 | 26.106 |
| Core.RemoteCachingAndSingleFlightTests.Bounded_route_waiting_preserves_results_deadlines_and_cancellation(scenario: "rejection") | 45.830 | 145.918 | 11.263 | 28.993 |
| Core.RemoteCachingAndSingleFlightTests.Bounded_route_waiting_preserves_results_deadlines_and_cancellation(scenario: "success") | 97.563 | 116.531 | 43.430 | 115.355 |
| Core.RemoteCachingAndSingleFlightTests.Stale_routes_are_shared_throttled_and_discarded_after_a_rejection | 24.689 | 25.170 | 9.566 | 12.840 |
| ServiceClient.RemoteDatabaseDirectoryCachingTests.A_cancelled_caller_does_not_cancel_the_shared_listing | 29.976 | 140.757 | 15.293 | 15.351 |
| ServiceClient.RemoteDatabaseDirectoryCachingTests.A_failed_listing_is_not_cached | 3.016 | 4.805 | 1.924 | 2.058 |
| ServiceClient.RemoteDatabaseDirectoryCachingTests.A_hit_returns_the_cached_instance_even_for_a_large_listing | 45.452 | 478.151 | 52.258 | 53.175 |
| ServiceClient.RemoteDatabaseDirectoryCachingTests.A_listing_is_served_from_cache_until_the_lifetime_ends | 3.280 | 4.750 | 2.084 | 3.285 |
| ServiceClient.RemoteDatabaseDirectoryCachingTests.A_pending_directory_returns_the_cached_listing_after_one_second | 62.326 | 229.531 | 17.326 | 29.709 |
| ServiceClient.RemoteDatabaseDirectoryCachingTests.An_out_of_range_lifetime_fails_with_the_configuration_key(lifetime: "-00:00:01") | 0.143 | 2.435 | 0.360 | 0.476 |
| ServiceClient.RemoteDatabaseDirectoryCachingTests.An_out_of_range_lifetime_fails_with_the_configuration_key(lifetime: "00:00:00") | 0.334 | 2.122 | 0.861 | 0.907 |
| ServiceClient.RemoteDatabaseDirectoryCachingTests.An_out_of_range_lifetime_fails_with_the_configuration_key(lifetime: "01:00:01") | 8.544 | 32.716 | 8.131 | 15.191 |
| ServiceClient.RemoteDatabaseDirectoryCachingTests.An_unavailable_directory_uses_the_last_listing_until_the_hard_deadline | 6.833 | 22.101 | 11.970 | 12.250 |
| ServiceClient.RemoteDatabaseDirectoryCachingTests.Concurrent_listings_share_a_single_remote_call | 132.716 | 171.513 | 39.596 | 146.889 |
| ServiceClient.RemoteDatabaseDirectoryCachingTests.Connection_name_and_active_only_each_have_their_own_entry | 3.798 | 4.669 | 2.820 | 2.988 |
| ServiceClient.RemoteDatabaseDirectoryCachingTests.Failed_tenants_are_kept_with_the_cached_listing | 116.219 | 158.215 | 81.953 | 168.315 |
| ServiceClient.RemoteDatabaseDirectoryCachingTests.Names_differing_only_in_case_share_one_entry_and_reach_the_control_plane_lowercased | 2.321 | 15.351 | 4.631 | 4.871 |
| ServiceClient.RemoteDatabaseDirectoryCachingTests.The_default_lifetime_is_thirty_seconds | 7.722 | 10.963 | 2.357 | 4.278 |
| ServiceClient.RemoteDatabaseDirectoryCachingTests.The_maximum_lifetime_is_accepted | 4.479 | 6.006 | 3.264 | 3.628 |
| ServiceClient.RemoteDatabaseDirectoryCachingTests.When_all_callers_cancel_the_next_caller_joins_the_same_listing | 80.566 | 174.656 | 35.743 | 71.805 |
| OAuth.OfficialClientTests.Token_cache_never_writes_distributed_storage_and_respects_output_expiry | 42.566 | 44.580 | 25.168 | 28.263 |
| OAuth.TokenCacheTests.A_background_machine_rejection_clears_the_timeout_fallback | 6.062 | 6.125 | 3.986 | 9.035 |
| OAuth.TokenCacheTests.A_delayed_cache_miss_reuses_the_completed_fetch | 7.369 | 10.208 | 4.365 | 8.656 |
| OAuth.TokenCacheTests.A_pending_machine_renewal_returns_stale_within_one_second | 20.854 | 26.002 | 19.152 | 41.702 |
| OAuth.TokenCacheTests.A_timed_out_renewal_does_not_revive_an_invalidated_or_expired_token(expire: False) | 8.494 | 32.993 | 3.673 | 12.169 |
| OAuth.TokenCacheTests.A_timed_out_renewal_does_not_revive_an_invalidated_or_expired_token(expire: True) | 44.044 | 88.760 | 48.628 | 49.424 |
| OAuth.TokenCacheTests.Machine_renewal_reuses_only_a_current_usable_token(invalidate: False) | 3.514 | 24.735 | 3.506 | 3.506 |
| OAuth.TokenCacheTests.Machine_renewal_reuses_only_a_current_usable_token(invalidate: True) | 6.115 | 8.607 | 3.672 | 6.402 |
| ServiceClient.SharedSettingReaderTests.A_declared_setting_the_provider_does_not_share_yields_its_fallback | 2.063 | 2.213 | 1.778 | 1.953 |
| ServiceClient.SharedSettingReaderTests.A_provider_is_registered_once_with_at_least_one_setting | 0.549 | 0.691 | 0.598 | 0.637 |
| ServiceClient.SharedSettingReaderTests.A_rejection_drops_earlier_values_and_yields_the_fallbacks(failure: "401") | 10.745 | 22.175 | 13.689 | 15.966 |
| ServiceClient.SharedSettingReaderTests.A_rejection_drops_earlier_values_and_yields_the_fallbacks(failure: "403") | 1.805 | 5.280 | 1.788 | 2.123 |
| ServiceClient.SharedSettingReaderTests.A_rejection_drops_earlier_values_and_yields_the_fallbacks(failure: "invalid") | 15.725 | 47.402 | 35.169 | 35.207 |
| ServiceClient.SharedSettingReaderTests.A_rejection_drops_earlier_values_and_yields_the_fallbacks(failure: "token") | 2.251 | 5.495 | 2.389 | 2.622 |
| ServiceClient.SharedSettingReaderTests.A_slow_provider_is_waited_for_at_most_one_second_when_stale_values_exist | 20.012 | 38.407 | 17.347 | 20.369 |
| ServiceClient.SharedSettingReaderTests.An_unavailable_provider_serves_stale_values_until_they_expire | 8.242 | 11.969 | 7.293 | 7.525 |
| ServiceClient.SharedSettingReaderTests.An_unknown_tenant_yields_the_fallbacks | 1.942 | 6.673 | 1.909 | 1.947 |
| ServiceClient.SharedSettingReaderTests.Cold_cache_with_a_cancelable_token_fetches_the_tenant_path | 114.317 | 238.402 | 129.869 | 181.178 |
| ServiceClient.SharedSettingReaderTests.Concurrent_reads_share_one_remote_call | 10.253 | 34.705 | 11.965 | 80.218 |
| ServiceClient.SharedSettingReaderTests.Each_scope_reads_its_own_tenant_with_a_scoped_tenant_accessor | 4.935 | 6.196 | 3.903 | 4.482 |
| ServiceClient.SharedSettingReaderTests.Expired_values_are_revalidated_with_the_previous_etag | 2.556 | 3.397 | 2.353 | 2.556 |
| ServiceClient.SharedSettingReaderTests.Fresh_values_are_served_from_cache | 3.644 | 8.661 | 3.655 | 3.666 |
| ServiceClient.SharedSettingReaderTests.Invalid_options_are_reported_with_their_configuration_keys(key: "FreshFor", value: "00:00:00") | 0.137 | 0.277 | 0.224 | 0.261 |
| ServiceClient.SharedSettingReaderTests.Invalid_options_are_reported_with_their_configuration_keys(key: "MaxStaleness", value: "-00:00:01") | 1.781 | 3.119 | 1.768 | 1.840 |
| ServiceClient.SharedSettingReaderTests.Invalid_options_are_reported_with_their_configuration_keys(key: "Path", value: " ") | 0.407 | 0.854 | 0.405 | 0.414 |
| ServiceClient.SharedSettingReaderTests.Reading_an_undeclared_setting_or_provider_fails | 2.801 | 7.760 | 2.962 | 3.427 |
| ServiceClient.SharedSettingReaderTests.The_current_tenant_selects_the_cache_entry_and_the_path | 9.727 | 19.533 | 9.460 | 9.983 |
| ServiceClient.SharedSettingReaderTests.Without_earlier_values_an_unavailable_provider_yields_the_fallbacks | 2.235 | 3.188 | 2.526 | 2.817 |
| Application.EmailChallengeStoreTests.A_cleanup_failure_does_not_replace_the_send_exception | 1.179 | 4.024 | 0.422 | 0.424 |
| Application.EmailChallengeStoreTests.A_delayed_cleanup_cannot_release_the_next_holders_reservation | 60.303 | 2319.189 | 19.024 | 21.512 |
| Application.EmailChallengeStoreTests.A_failed_password_reset_delivery_keeps_the_quota | 1.622 | 7.304 | 1.048 | 1.383 |
| Application.EmailChallengeStoreTests.A_failed_send_voids_its_own_challenge_and_reservation | 1.407 | 2.316 | 1.651 | 14.942 |
| Application.EmailChallengeStoreTests.A_late_failure_cannot_release_the_next_holders_reservation | 198.951 | 210.276 | 64.261 | 122.430 |
| Application.EmailChallengeStoreTests.A_password_reset_code_is_sent_in_the_background_and_proves_the_user_and_stamp | 366.329 | 670.491 | 435.163 | 639.417 |
| Application.EmailChallengeStoreTests.A_password_reset_code_that_expired_in_the_queue_is_not_sent | 2.162 | 3.279 | 0.422 | 0.494 |
| Application.EmailChallengeStoreTests.A_password_reset_without_a_target_sends_nothing_and_never_validates | 2.355 | 11.753 | 6.144 | 40.558 |
| Application.EmailChallengeStoreTests.A_request_canceled_after_a_successful_send_keeps_the_challenge_and_quota | 95.763 | 242.285 | 57.647 | 90.633 |
| Application.EmailChallengeStoreTests.A_sign_in_code_is_sent_in_the_background_and_proves_the_user_and_stamp | 0.656 | 2.700 | 1.141 | 24.108 |
| Application.EmailChallengeStoreTests.A_sign_in_send_within_the_interval_reuses_the_previous_code | 9.281 | 46.999 | 14.234 | 53.584 |
| Application.EmailChallengeStoreTests.A_used_sign_in_code_frees_the_send_interval | 1.995 | 9.522 | 5.106 | 7.052 |
| Application.EmailChallengeStoreTests.An_account_that_needs_an_administrator_gets_a_notice_instead_of_a_code | 1.744 | 13.274 | 0.830 | 0.853 |
| Application.EmailChallengeStoreTests.An_exhausted_sign_in_code_keeps_the_send_interval | 8.232 | 8.754 | 2.105 | 5.151 |
| Application.EmailChallengeStoreTests.Canceling_the_request_during_the_send_aborts_it_and_voids_the_reservation | 138.358 | 183.457 | 64.446 | 223.770 |
| Application.EmailChallengeStoreTests.Losing_the_lock_before_sending_skips_the_send | 7.097 | 65.417 | 47.294 | 140.315 |
| Application.EmailChallengeStoreTests.Losing_the_lock_during_the_send_neither_aborts_nor_rolls_it_back | 1.554 | 3.841 | 0.964 | 1.901 |
| Application.EmailChallengeStoreTests.Only_a_stopping_consumer_lets_a_canceled_delivery_escape | 21.016 | 29.698 | 6.724 | 9.675 |
| Application.EmailChallengeStoreTests.Password_reset_codes_are_capped_per_fixed_window | 4.225 | 5.230 | 2.428 | 4.144 |
| Application.EmailChallengeStoreTests.Reuse_near_the_sign_in_deadline_succeeds_but_a_new_code_is_refused | 3.528 | 5.012 | 0.727 | 0.768 |
| Application.EmailChallengeStoreTests.Sign_in_codes_have_their_own_send_interval | 2.075 | 8.905 | 1.120 | 16.957 |
| Application.EmailChallengeStoreTests.The_password_reset_cap_does_not_count_registration_codes | 3.661 | 62.982 | 0.346 | 0.441 |
| Application.EmailChallengeStoreTests.The_send_interval_ends_exactly_at_its_boundary | 1.448 | 7.617 | 24.137 | 59.150 |
| Application.EmailChallengeStoreTests.Validation_tells_an_expired_challenge_from_a_wrong_code | 7.032 | 31.277 | 4.094 | 91.952 |

### R5 准入反例

对生成的 `identity-workspace` 执行 `node frontend/scripts/check-e2e-admission.mjs --self-test` 及正常检查。自检通过，实际发现 16 条用例。删除登录用例标签，以及临时追加 20 条参数化用例，分别得到退出码 1。反例只在隔离生成目录构造。

```text
E2E admission passed: 16/20 tests.
login.e2e.ts:6 signs in through the login page: expected one @proves: tag, got 0.
Full E2E suite has 36 tests, above the cap of 20.
```

### R9 测试库对照

在同一台本机节点上先构建 Release，再对同一个生成场景 `identity-all-features` 的集成测试项目执行 `dotnet test -c Release --no-build`，每档两轮。墙钟包括测试进程、数据库启动与清理，构建独立计时。

| 配置 | 第一轮（s） | 第二轮（s） | 均值（s） |
| --- | ---: | ---: | ---: |
| 基线（磁盘与 WAL_LOG） | 169.386 | 193.679 | 181.533 |
| tmpfs 与 WAL_LOG | 149.294 | 177.171 | 163.233 |
| tmpfs 与 FILE_COPY | 161.811 | 199.990 | 180.901 |

六轮均为 841 条通过。tmpfs 的实测均值较基线低约 10.1%。加入 `FILE_COPY` 后的均值较单用 tmpfs 高约 10.8%，所以保留 tmpfs 和默认克隆策略 `WAL_LOG`。

在浏览器 E2E 的 PostgreSQL 容器内执行 `stat -f -c "%T %n" "$PGDATA"`，确认实际目录为 `tmpfs /var/lib/postgresql/18/docker`。

### 模板包隔离安装

`pwsh template-package/test-template-package.ps1` 在复审后的 `6d20dfec` 通过。临时模板包 `0.0.1-local.20261125073129` 的 1,725 个文件、隔离安装和来源标记均通过。按标记分别重建 `identity-all-features` 与 `resource-capabilities-03`，恢复原有 UserSecretsId 后均为零差异。

### 仓库静态检查

在本机以 Python 3.14 和 PyYAML 运行 `pwsh scripts/check-all.ps1`，52 道闸门全部通过，30 个场景覆盖 255,147 个可达条件行。复审修订后重新运行 `python3 scripts/measure-template-read-cost.py --check`，所有阅读路径仍在原上限内。

在本机运行 `python3 scripts/test-template-generation.py --shard control --workers 2`，6,144 个输入映射、18 个实际生成等价对照、4 个 CI 变体和 10 个 `verify.ps1` 签名全部通过。步骤清单覆盖仅后端、仅前端、仅文档、根脚本、删除和跨目录改名。命令替身注入的步骤失败保留原退出码并停止后续步骤，报告目录清除了前一轮 XML。

### 生成项目验证与报告

在本机为 GitHub 形态 `identity-workspace` 实际运行 `pwsh scripts/verify.ps1`。`ng test` 通过 194 个文件中的 2,249 条浏览器单测，写出 `test-reports/unit-frontend.xml`。解析 XML 得到 2,249 个用例，零失败、零跳过。同一轮的后端单测和集成 JUnit 分别包含 361 与 872 个用例，均为零失败、零跳过。

默认 4 worker 的 E2E 首轮通过 10 条，另有 3 条重试后通过、3 条超时。`verify` 保留 Playwright 的退出码 1，仍输出逐步墙钟和三层慢用例，并保留含 16 条记录的 `e2e.xml`。超时 trace 中，通行密钥登记约 6.14 秒、一次登录约 6.61 秒、最慢的 Webhook 查询约 12.64 秒，超过相关断言的 5 秒等待。

同机以 `develop` 基线 `28654461` 重新打包并生成 `identity-workspace`，完成 Release 与前端构建后执行 `npm run e2e`。默认 4 worker 运行同一批 16 条，全部首次通过，Playwright 报告约 72 秒。候选首次 E2E 紧接完整 `verify` 的前置步骤；另用和基线相同的准备步骤与直接入口对照。

同一份候选构建以默认 4 worker 依次跑三轮 `npm run e2e`。数据库挂载只在隔离生成目录临时切换，其余测试与产物保持一致。

| 数据目录 | 首次通过 | 重试通过 | 失败 | Playwright 墙钟 |
| --- | ---: | ---: | ---: | ---: |
| tmpfs | 7 | 1 | 8 | 约 318 s |
| 容器磁盘 | 12 | 3 | 1 | 约 120 s |
| 恢复 tmpfs | 15 | 1 | 0 | 约 78 s |

磁盘轮仍复现超时，恢复 tmpfs 后通过。超时未随挂载方式稳定复现。四 worker 的完整 `verify` 与首次直接 E2E 均保留失败记录，候选的独立 Chromium 全集有一次通过，默认并发及超时配置保持原设定。

`resource-capabilities-03` 为纯 API 形态，未生成 E2E 准入脚本，`verify.ps1 -List` 中也没有准入或 E2E 步骤。另一次全量探测通过 197 条单测，192 条集成测试中有一条操作日志测试在解析结构化 `OperationTime` 时失败。Resource 的缓存覆盖见前述通过的 `resource-workspace` 全量测试。

同机以 `28654461` 重新生成 `resource-capabilities-03`，两次独立执行 `dotnet test -c Release --no-build --filter FullyQualifiedName~OperationRecordLoggingTests`。每轮均为 2 条通过、1 条失败，退出码均为 1。失败均为 `Security_log_is_emitted_only_after_the_real_business_transaction_commits(commit: True)`，位置均为 `OperationRecordLoggingTests.cs:84`，TRX 原文如下。

```text
Assert.True() Failure
Expected: True
Actual:   False
```

```csharp
Assert.True(DateTimeOffset.TryParse(OperationRecordLogCapture.Field(record, "OperationTime"), out _));
```

**待跟进。** 对齐该日志测试的结构化时间值与解析契约。相同失败在基线两轮均复现，本轮保留原测试和实现。

在 WSL 节点先对纯 API Resource 执行成功的 `pwsh scripts/verify.ps1 -Fast`，再在隔离目录注入一个延迟 350 ms 后失败的单测并添加旧报告。第二轮仍用 `-Fast`，收到失败步骤的退出码 1，旧报告已清除，新 JUnit 有 198 条记录且恰有该条失败。逐步、总耗时和慢用例汇总仍输出。

```text
Verification failed at step 'backend-test:Matrix.ResourceCapabilities03.UnitTests' (exit code 1); remaining steps were not run.
Total wall time                             19.52 s
WARNING:   358.5 ms  Matrix.ResourceCapabilities03.UnitTests.VerificationReportProbeTests :: A_deliberate_failure_is_reported
```

### 逐步墙钟与单端产物

在生成的 Identity Workspace 内提交单端路径变更，然后清掉后端 `bin/obj` 和前端 `dist`，分别用 `pwsh scripts/verify.ps1 -ChangedBase <基准>` 验证。两轮均为默认 4 worker，16 条 E2E 全部首次通过。

- 仅后端改动运行后端验证、前端依赖安装和生产构建，跳过前端 lint 与单测。墙钟 524.43 秒。
- 仅前端改动生成 Release API 和 DbMigrator，跳过后端格式与测试。前端单测 2,249 条通过，墙钟 208.33 秒。
- 本地身份形态 `-Fast` 在既有前端依赖下只跑 E2E 用例发现，墙钟 55.03 秒。

下表均为秒。Identity 默认全量列保留先前 E2E 超时的真实耗时，Resource 全量列保留操作日志断言失败的一轮，控制失败列为故障报告反例。GitHub 与 GitLab 的 JUnit 上传条件由实际生成的工作流结构检查验证，生成项目未另行触发托管流水线。

| 步骤 | identity-full | identity-fast | backend-only | frontend-only | resource-full | resource-fast | controlled-failure |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| check-error-codes | 8.79 | 4.23 | 3.94 | 4.49 | 1.73 | 0.23 | 0.24 |
| check-i18n | 1.24 | 1.68 | 1.05 | 0.88 | — | — | — |
| check-operation-action-i18n | 0.16 | 0.10 | 0.21 | 0.09 | — | — | — |
| check-test-diagnostics | 11.67 | 3.46 | 3.43 | 2.94 | 0.78 | 0.10 | 0.20 |
| check-setting-group-icons | 0.34 | 0.20 | 0.17 | 0.14 | — | — | — |
| backend-restore | 9.56 | 4.11 | 7.88 | 4.80 | 11.12 | 1.06 | 1.56 |
| backend-build | 82.44 | 4.17 | 50.52 | 16.23 | 38.42 | 9.06 | 3.75 |
| backend-format | 51.04 | 27.04 | 38.40 | — | 29.46 | 12.28 | 11.64 |
| backend-test:UnitTests | 5.22 | 4.50 | 5.00 | — | 8.15 | 2.54 | 2.07 |
| backend-test:IntegrationTests | 317.22 | — | 297.54 | — | 173.30 | — | — |
| frontend-install | 28.56 | — | 25.43 | 24.03 | — | — | — |
| check-e2e-admission | 0.46 | 0.44 | 0.50 | 0.61 | — | — | — |
| frontend-browser-install | 0.90 | — | 1.18 | 0.92 | — | — | — |
| frontend-lint | 23.18 | 5.02 | — | 22.49 | — | — | — |
| frontend-test | 151.27 | — | — | 73.96 | — | — | — |
| frontend-build | 39.90 | — | 29.23 | 12.08 | — | — | — |
| frontend-e2e | 192.92 | — | 59.85 | 44.59 | — | — | — |
| 总墙钟 | 925.34 | 55.03 | 524.43 | 208.33 | 263.18 | 25.30 | 19.52 |

### 列表详情规范

前端列表和 API 规范以字段是否有上界选择随行渲染或单资源读取。两篇分别减少 34、21 字符，读取成本闸门通过。Webhook 投递列表已有摘要和单资源详情区分，载荷与尝试历史通过 `/deliveries/{id}` 读取。操作记录的 `FailureDetail` 已有长度上限，列表却仍直接返回无上界的 `FailureData` JSON，占位参数也用于现有前端回落渲染。本轮只修改规范，后续另审操作记录列表契约与消费者兼容。

### 合入追加范围后的复验

在 `bec77530` 上，`pwsh scripts/check-all.ps1` 通过全部 52 道静态闸门。`pwsh template-package/test-template-package.ps1` 验证 `0.0.1-local.20261125092925` 的 1,727 个文件、隔离安装和两种形态零差异重建。`pwsh scripts/verify.ps1 -Fast` 复测 Identity Workspace 371 条后端单测和 E2E 发现 16/20，Resource Workspace 206 条后端单测均通过。收件人行为对照和条件形态见[收件人渲染规格 §4](./2026-11-25-recipient-culture-rendering.md#4-验证)。

同机对最终候选重新运行 Identity Workspace 默认 `pwsh scripts/verify.ps1`，371 条后端单测、872 条集成、2,249 条前端单测均通过。默认 4 worker 的 16 条 E2E 全部首次通过，浏览器执行 34.7 秒，E2E 步骤墙钟 35.33 秒，完整入口墙钟 351.42 秒。后端构建 9.83 秒，后端单测 4.51 秒，集成 156.31 秒，前端单测 63.58 秒，前端构建 12.83 秒。逐层解析实际 JUnit，后端两层、`unit-frontend.xml` 和 `e2e.xml` 的条数与输出一致，零失败零跳过。保留前文首轮超时及同机对照记录，合入仍以 PR 的 CI 为准。
