# 模板质量验证

本文用于维护模板，不进入生成项目。闸门、检查接替与变异验收、CI 分片原则统一见[质量检查与验证分工](../framework/quality-assurance.md)；生成与条件裁剪入口见[模板开发规范](./development-guide.md)。

## 测试层级与接替边界

UnitTests 验证隔离规则和边界、注册生命周期/幂等；IntegrationTests 用 `WebApplicationFactory` 组合真实 HTTP 管道。后者是进程内宿主，替换外部依赖后不能认证真实网络、OIDC 跨服务链路或 PostgreSQL 语义。浏览器模式组件测试验证类、注入和 DOM，HTTP mocks 与 TestBed 仍是测试替身，不等于完整应用 E2E。纯函数不启动 TestBed；需要 DOM/注入上下文时才创建 fixture，不以固定数量比例削减测试。

模板的内存库 fixture 只认证它实际覆盖的应用组合、状态与跟踪行为；唯一约束、SQL 翻译、事务、schema、migration 执行及 Shared/Dedicated 物理隔离必须有关系型或真实 PostgreSQL 证据。设计时 Npgsql 模型/快照比对不能替代实际 migration。真实库闭环已覆盖某个路由断言，不代表全部内存 fixture 断言都能删除；逐项接替仍需变异证据。不能为了测试新建只有测试调用的生产 Provider 选择抽象。

条件 using 守卫覆盖 24 个符号赋值，生成矩阵覆盖登记场景，范围不同。默认前端保留真实 Chromium、隔离与完整 spec 发现；未经等价反例验证，不关闭隔离、改 jsdom 或删除发现/翻译/形态断言来提速。新增场景要验证特性组合，不能只测 Identity/Resource 正常路径。 场景定义及两片归属在 `scripts/template-matrix-scenarios.ps1`，定义、全集与分片必须一致。人工验证仍用 `scripts/test-template-matrix.ps1` 全量或 `-Scenarios`；CI 使用 `-Shard 1` / `-Shard 2`，不能与 `-Scenarios` 混用。每片保留生成形态、还原/构建、运行时冒烟、后端单测/集成测试、lint、前端构建、全部 spec 发现与浏览器测试，不用分片替换测试阶段。容器责任保留在含 Standalone 的分片；PR 按完整 base→head 差异判定，范围不明时执行容器验证。

矩阵验收读取同一次 run 的两片 artifact，使用 `scripts/check-template-matrix-results.ps1` 核对准确的场景归属、全集与 Backend/Runtime/Lint/Frontend/Test 状态；要求容器验证时显式传入 `-ContainerSmokeScenarios standalone`。还须核对日志中后端和真实浏览器测试确有用例执行，不能仅凭作业名称或退出码推定完整覆盖。Standalone 容器检查构建 API 与 Migrator 镜像，并在两种镜像内执行 `dotnet --info`；它认证镜像构建与 .NET 运行时可用，不代表 API 已按部署配置启动或已连接数据库。真实 PostgreSQL 与 OIDC 跨服务责任仍由各自独立作业承担，未执行的可选场景另行注明。

分片均衡同时计入实际承担的容器阶段、生成准备和浏览器安装，不只按场景数量划分。关键片可能随实测改变，调整前先核对各场景命令区间和准备阶段；准备阶段变快、未产生 lint 热缓存或另一片提前完成，都不能当作删减测试的依据。

OIDC 浏览器闭环复用 `scripts/test-template-oidc-e2e.ps1`：`-IncludeBrowserScenarios` 在 HTTP 场景后追加有头浏览器验证，`-BrowserOnly` 只执行浏览器闭环。另需 Node.js、npm 与已安装 Chromium 的 agent-browser；可用官方 `AGENT_BROWSER_EXECUTABLE_PATH` 选择浏览器程序。脚本按共享矩阵定义生成 Identity 外部登录与 Resource，生产构建前端由所属 API 同源托管；覆盖登录、服务端票据真实到期后的续期/拒绝、官方退出、Google/GitHub 官方处理器及接续原授权请求。第三方通信使用本地 PKCE 协议夹具，不替换生产认证处理器。一次性浏览器会话关闭自动保存，避免额外访问已见过的源。证据保存在隔离 `.tmp/oidc-e2e/<run>/`，包含断言、脱敏 HAR、截图和 SQL 投影；浏览器会话与容器仅清理本轮创建的资源。CI 默认仍执行 HTTP 入口，浏览器开关按需显式启用。

## 前端依赖维护

默认与本地化载荷的 Angular 运行时、CDK、编译器和构建工具须按官方 peer 范围配套更新，并同时更新两套锁文件。先在隔离副本验证安装、原审计阈值、lint、构建与真实浏览器测试，再同步依赖文件；完整条件矩阵仍须执行。不得用 `--force`、`--legacy-peer-deps` 或降低审计阈值绕过依赖冲突与安全门禁。

## lint 缓存与失效

默认与本地化载荷的 `npm run lint` 都对原完整范围执行 ESLint、Stylelint 和 Prettier，使用各工具官方的 content cache，保留失败退出码与现有规则。缓存只复用未变文件的有效结果，不改为 Git diff 清单；新增文件仍会发现，配置变化按工具的实际配置/选项重新检查。

缓存位于生成前端的 `.cache/lint/`。每次依赖安装通过 `postinstall` 清掉该目录，覆盖插件更新不一定进入工具缓存键的问题；这是唯一的失效入口，不增加自写缓存键计算器。该路径只存工具结果，不放源码；Git、Prettier 以及模板载荷排除它。显式忽略生命周期脚本安装依赖时，使用者须先删除该目录，不能复用上一套依赖的 lint 结果。

依赖、配置或检查入口变化后，在已建缓存上注入 TS/HTML/CSS/格式违规和新增文件；配置收紧、插件更新后未改源文件的违规也须变红。测试真实 npm 安装生命周期会清缓存，不仅调用一个模拟清理函数。当前 ESLint 未启用跨文件类型检查；将来引入 type-aware 或跨文件规则时，先证明缓存依赖范围完整，否则对该检查重新采用全量执行。

本地同目录反复校验有热缓存收益；矩阵每次生成到新 run 目录，首次执行仍检查全量。不共享缓存目录或假定每片都热命中，不把本地收益乘场景数当作 CI 收益。Playwright 浏览器安装/缓存策略须独立测量，不把下载时间全部当成可省值。

官方依据：[ESLint 缓存](https://eslint.org/docs/latest/use/command-line-interface#--cache)、[Stylelint 缓存](https://stylelint.io/user-guide/cli/#--cache)、[Prettier 缓存与插件限制](https://prettier.io/docs/cli#--cache)、[Angular 组件测试](https://angular.dev/guide/testing/components-basics)、[Angular test 参数](https://angular.dev/cli/test)、[Playwright CI](https://playwright.dev/docs/ci)。

浏览器认证变更须覆盖十场景全集，新增 `standalone-external-login` 放入第 2 片。真实官方处理器、Cookie 解保护、服务端票据删除、并发刷新、资源归属与可编译变异的边界见[浏览器认证维护规则](browser-authentication.md)。
