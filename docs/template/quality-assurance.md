# 模板质量验证

本文用于维护模板，不进入生成项目。闸门、检查接替与变异验收、CI 分片原则统一见[质量检查与验证分工](../framework/quality-assurance.md)；生成与条件裁剪入口见[模板开发规范](./development-guide.md)。

## 本地与 CI 的执行位置

| 阶段 | 验证责任 |
| --- | --- |
| 编辑中 | 已生成项目的目标测试；真实依赖变化做目标集成 |
| 完整改动形成后 | 局部源码按[本地集合](../framework/quality-assurance.md#模板本地场景集合)计算产品并运行完整适用阶段；全局生成/共享/未知输入保留完整 PR 档 |
| PR CI | 按实际候选计划完成必过责任；审查可并行，合并前同时通过 |
| 合入、夜间与发布 | 保持完整档；本地只复现完整档特有故障 |

源码中的修复最终必须写回模板并重新生成验收。静态条件覆盖证明不了产物能编译、lint 或运行；不能只挑一个默认形态，也不能将本地场景集合当作 CI 阶段裁剪计划。审查与失败重跑规则引用根质量规范，不重复维护场景清单。

## 测试层级与接替边界

UnitTests 验证隔离规则和边界、注册生命周期/幂等；IntegrationTests 用 `WebApplicationFactory` 组合真实 HTTP 管道。后者是进程内宿主，替换外部依赖后不能认证真实网络、OIDC 跨服务链路或 PostgreSQL 语义。浏览器模式组件测试验证类、注入和 DOM，HTTP mocks 与 TestBed 仍是测试替身，不等于完整应用 E2E。纯函数不启动 TestBed；需要 DOM/注入上下文时才创建 fixture，不以固定数量比例削减测试。

测试宿主把 `PasswordHash:IterationCount` 调到 1000：每个宿主都要播种管理员、每次登录都要校验口令，生产工作因子曾占集成测试一半以上的 CPU。默认值与密文格式由 `PasswordHashingTests` 按生产默认值钉住；PostgreSQL 与 OIDC 端到端使用生产默认值。宿主数量不靠合并测试类来压：多个类的用例依赖空库（精确用户名、全库计数、租户设置），共享宿主须改写断言，收益不抵风险。

集成测试与矩阵运行时冒烟都跑在真实 PostgreSQL 上（选型依据见[模板开发规范 §9](./development-guide.md#9-测试与开发只用-postgresql为什么不用-inmemory-或-sqlite)）：集成测试由 Testcontainers 起容器、迁移一次模板库后每个宿主克隆一份；冒烟在本次矩阵运行共用的容器里为每个场景建库，先用该场景构建出的 `DbMigrator --apply` 迁移（Resource 用 `MigrationTarget` 单目标，不回源 Identity），再启动 API。矩阵与集成测试因此都需要本机 Docker 引擎（数据库端口绑定在 127.0.0.1，远端上下文不适用），CI 的 ubuntu runner 自带。设计时快照比对仍保留在单元测试里，它不需要 Docker、几毫秒给出结论。PostgreSQL 端到端承担跨进程与多库拆分；真实库集成测试覆盖了某个路由断言，不代表端到端里的对应断言都能删除，逐项接替仍需变异证据。不能为了测试新建只有测试调用的生产 Provider 选择抽象。

条件 using 守卫覆盖 768 个原始符号赋值，生成矩阵覆盖登记场景，范围不同。默认前端保留真实 Chromium、隔离与完整 spec 发现；未经等价反例验证，不关闭隔离、改 jsdom 或删除发现/翻译/形态断言来提速。新增场景要验证特性组合，不能只测 Identity/Resource 正常路径。

场景定义、`pr`/`full` 档位及具名分片在 `scripts/template-matrix-scenarios.ps1`，定义、全集与分片必须一致；每片至少一个场景。人工验证用 `scripts/test-template-matrix.ps1` 的 `-Scenarios`、`-Tier pr`（可加 `-Slice <片名>`）或不带参数的全集；CI 每片一个作业，使用 `-Tier <档位> -Slice <片名>`，不能与 `-Scenarios` 混用。`pr` 档只收覆盖必需的场景：`check-template-scenario-coverage.py` 对全部模板文件求值文件级 modifiers、嵌套条件与 computed 符号，任何条件行只由 `full` 档独有场景生成即失败。它同时检查行覆盖和有效能力两两组合：同一文件里几处分支只在某个 `full` 档场景里同时出现时（例如外部登录与非本地化的两段登录页代码），二者的组合交互要到合入后才验证。新增条件分支让闸门变红时，把对应场景加入 `pr` 档或改写条件，不删除闸门；加入后按实测耗时放入合适的分片，必要时新增具名分片并写明它验证什么。默认形态 `identity` 不是覆盖必需，但它是 `dotnet new` 的默认产物，固定留在 `pr` 档。独立的全部有效形态生成作业必须在同候选汇总中成功；内部文档变更时按既有范围规则记为不适用。Resource 纯 API 的 Lint/Frontend/Test 阶段明确记为 not-applicable，汇总按场景元数据核对，拒绝伪造 pass。默认人工及 full 档每个入选场景保留生成形态、还原/构建、运行时冒烟、后端单测/集成测试、lint、前端构建、全部 spec 发现与浏览器测试。直接 PR 的局部计划按下节选择必要阶段，不能用手动跳过代替计划。容器检查由 `-ContainerSmoke` 挂到两档共有的含前端 Standalone 与纯 API Resource 场景，清单使用 `$ContainerScenarios`；PR 按完整 base→head 差异判定，范围不明时执行容器验证。

矩阵验收读取同一次 run 各分片的 artifact，使用 `scripts/check-template-matrix-results.ps1 -Tier <档位>` 核对候选 SHA、收据档位、独立预期计划的准确场景归属与 Backend/Runtime/Lint/Frontend/Test 状态（CI 传 -ValidationPlanPath；默认要求完整档）；要求容器验证时传入 `-ContainerSmoke`。还须核对日志中后端和真实浏览器测试确有用例执行，不能仅凭作业名称或退出码推定完整覆盖。Standalone 容器检查构建 API 与 Migrator 镜像，并在两种镜像内执行 `dotnet --info`；它认证镜像构建与 .NET 运行时可用，不代表 API 已按部署配置启动或已连接数据库。纯 API Resource 另外在镜像内实际迁移并启动 API，验证 liveness、无 SPA 与无 Node；跨服务认证及完整 PostgreSQL 隔离仍由各自独立作业承担，未执行的可选场景另行注明。

分片均衡同时计入实际承担的容器阶段、生成准备和浏览器安装，不只按场景数量划分。关键片可能随实测改变，调整前先核对各场景命令区间和准备阶段；准备阶段变快、未产生 lint 热缓存或另一片提前完成，都不能当作删减测试的依据。

OIDC 浏览器闭环复用 `scripts/test-template-oidc-e2e.ps1`：`-IncludeBrowserScenarios` 在 HTTP 场景后追加有头浏览器验证，`-BrowserOnly` 只执行浏览器闭环。另需 Node.js、npm 与已安装 Chromium 的 agent-browser；可用官方 `AGENT_BROWSER_EXECUTABLE_PATH` 选择浏览器程序。脚本按共享矩阵定义生成 Identity 外部登录与 Resource，生产构建前端由所属 API 同源托管；覆盖登录、服务端票据真实到期后的续期/拒绝（浏览器阶段把 Identity 切到快速档 `OAuth__AccessTokenLifetime=00:01:30`，签发寿命按档位断言，不等默认的 10 分钟；分层原则见[质量检查与验证分工](../framework/quality-assurance.md#分层执行与时间预算)）、官方退出、Google/GitHub 官方处理器及接续原授权请求。第三方通信使用本地 PKCE 协议夹具，不替换生产认证处理器。一次性浏览器会话关闭自动保存，避免额外访问已见过的源。证据保存在隔离 `.tmp/oidc-e2e/<run>/`，包含断言、脱敏 HAR、截图和 SQL 投影；浏览器会话与容器仅清理本轮创建的资源。浏览器阶段的 Resource 同时启用通知与业务实时，真实创建角色并验证已打开列表自动刷新、共享一条 Hub 连接；多租户阶段通过正式管理员 CLI 引导授权。CI 默认仍执行 HTTP 入口，浏览器开关按需显式启用。

默认 HTTP 端到端还生成启用业务实时的纯 API Resource，以正式管理员命令授予权限，再由独立 .NET SignalR 客户端通过真实 Bearer/WebSocket 订阅宿主角色列表。实际创建角色后验证提交后的事件，并拒绝跨作用域、未知资源及无作用域资源键的订阅；同时验证没有前端目录且根路径不提供 SPA。该用例认证外部客户端消费能力，浏览器列表自动刷新由上述浏览器场景另行验证。

## 业务维护 Job

框架的调度循环测试见[覆盖分工](../framework/quality-assurance.md#定时任务的覆盖分工)。模板只验证业务职责：清理汇总的失败/未解析数据库传播放无宿主单测；SQL 翻译、全局过滤器、共享租户与真实 DI 接线放现有 PostgreSQL 集成测试类。

截止时刻用官方假时钟固定；必要配置变体复用既有工厂的数据库，关闭该变体的自动调度后从 DI 直接执行真实 Job，避免偶发自动清理干扰断言。默认测试工厂仍启动调度器。通知当前契约是读/未读分别按保留期清理，`CreationTime < cutoff`（等于保留），且覆盖宿主和共享租户；会话是 `LastSeenTime <= cutoff`（等于删除），同时核对领域 `IsExpired`。固定 UTC 时刻对齐数据库微秒精度，截止前/后数据用毫秒间隔；批量删除后换作用域查询并按本用例标识断言。

不为每个 Job 新建容器、独立场景或协议 E2E，不以有 HTTP 集成测试为由删除上述边界。代表性生成覆盖 LocalIdentity 与 RemoteTokenAuth 通知、LocalIdentity 无通知侧；全集 CI 继续覆盖所有适用形态。新用例首先解决覆盖缺口，耗时同时披露入口与方法体，不把增加测试宣称为提速。

## 源码预检与 CI 接替

独立 `test-template-matrix.ps1` 默认先执行 symbols、using-guards、async-boundaries 源码预检，再打包、生成并审计实际生成的锁文件。GitHub CI 在同候选静态作业完整执行 `check-all.ps1`，分片显式使用 `-SkipSourcePreflight`，只检查接替入口仍在清单；必过汇总同时等待并核对静态和全部必要动态作业成功。预检和生成可以并行，静态失败不能被分片成功掩盖。源码预检职责不由生成编译替代，人工入口不能用该 CI 开关省略检查。

纯内部文档 PR 的例外由仓库 [CI 范围与聚合规则](../framework/quality-assurance.md) 决定；`template/` 下的文档和 Skill 属于生成载荷，继续运行完整 PR 档。未选择动态场景时汇总明确报告“不适用”，不产生矩阵回执；选择的场景按独立计划执行必要阶段，不能由回执自己决定哪些阶段适用。

## 局部 PR 的场景与阶段

唯一规则与跨层责任表见[同候选输入计划](../framework/quality-assurance.md#同候选输入计划)。只含支持的模板前端源码时，生成/形态、audit、安装、healthcheck、lint、构建、spec 发现和真实浏览器测试保持；只含 backend/src/tests C# 时，生成/形态、audit、后端还原/构建、真实 PostgreSQL 冒烟和单元/集成保持。实际生成锁文件的生产依赖 audit 阈值、每片执行与网络重试保留；同片内内容相同的锁文件只审计一次，不以源码不变推断漏洞库不变。参数、依赖、项目配置、跨层和未知输入保留完整阶段；独立 PG/OIDC 均保留。前后端各自省略的阶段要求实际生成输入保持不变，不能把浏览器 mock 当 API 契约验证。

场景从现有 sources/modifiers/computed 求出修改文件的生产场景，并保留默认与全特性代表；文件删除和跨边界移动用完整差异的两侧，不能只看最后一次提交。首次维护选择规则或回执时运行 `python scripts/test-quality-validation-plan.py`，实际生成六种 PR 产品的前后对照，核对省略阶段/场景的输入，并验证错误回执拒绝。这是维护回归入口，不加入每日静态闸门。

计划绑定实际候选 SHA、档位、场景、模式和 Framework/消费责任。矩阵传 `-ValidationPlanPath`，收据写实际 SHA/模式和阶段的 pass/not-applicable；汇总使用打包作业提供的独立计划，拒绝少场景、错模式、错 SHA、未执行必需阶段及缺失/取消。局部模式不能与 -SkipFrontend/-SkipRuntime/-Scenarios 混用，full 不接受裁剪计划。

## 前端依赖维护

默认与本地化载荷的 Angular 运行时、CDK、编译器和构建工具须按官方 peer 范围配套更新，并同时更新两套锁文件。先在隔离副本验证安装、原审计阈值、lint、构建与真实浏览器测试，再同步依赖文件；完整条件矩阵仍须执行。不得用 `--force`、`--legacy-peer-deps` 或降低审计阈值绕过依赖冲突与安全门禁。

## lint 缓存与失效

默认与本地化载荷的 `npm run lint` 都对原完整范围执行 ESLint、Stylelint 和 Prettier，使用各工具官方的 content cache，保留失败退出码与现有规则。缓存只复用未变文件的有效结果，不改为 Git diff 清单；新增文件仍会发现，配置变化按工具的实际配置/选项重新检查。

缓存位于生成前端的 `.cache/lint/`。每次依赖安装通过 `postinstall` 清掉该目录，覆盖插件更新不一定进入工具缓存键的问题；这是唯一的失效入口，不增加自写缓存键计算器。该路径只存工具结果，不放源码；Git、Prettier 以及模板载荷排除它。显式忽略生命周期脚本安装依赖时，使用者须先删除该目录，不能复用上一套依赖的 lint 结果。

依赖、配置或检查入口变化后，在已建缓存上注入 TS/HTML/CSS/格式违规和新增文件；配置收紧、插件更新后未改源文件的违规也须变红。测试真实 npm 安装生命周期会清缓存，不仅调用一个模拟清理函数。当前 ESLint 未启用跨文件类型检查；将来引入 type-aware 或跨文件规则时，先证明缓存依赖范围完整，否则对该检查重新采用全量执行。

本地同目录反复校验有热缓存收益；矩阵每次生成到新 run 目录，首次执行仍检查全量。不共享缓存目录或假定每片都热命中，不把本地收益乘场景数当作 CI 收益。Playwright 浏览器安装/缓存策略须独立测量，不把下载时间全部当成可省值。

官方依据：[ESLint 缓存](https://eslint.org/docs/latest/use/command-line-interface#--cache)、[Stylelint 缓存](https://stylelint.io/user-guide/cli/#--cache)、[Prettier 缓存与插件限制](https://prettier.io/docs/cli#--cache)、[Angular 组件测试](https://angular.dev/guide/testing/components-basics)、[Angular test 参数](https://angular.dev/cli/test)、[Playwright CI](https://playwright.dev/docs/ci)。

浏览器认证变更在 L1 跑 `-Tier pr`，全部登记场景由合入后的 `full` 档承担。修改令牌寿命、刷新或到期判据时，提交前还须运行 `pwsh scripts/test-template-oidc-e2e.ps1 -IncludeExpiryWait`，验证真实到期边界；PR CI 默认省略真实到期等待，full 显式补跑，不能用 PR 成功代替这项变更责任。真实官方处理器、Cookie 解保护、服务端票据删除、并发刷新、资源归属与可编译变异的边界见[浏览器认证维护规则](browser-authentication.md)。
