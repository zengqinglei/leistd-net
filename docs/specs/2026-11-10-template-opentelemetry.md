# 模板默认接入 OpenTelemetry 指标与追踪

状态：已完成（Claude 代码审查通过） · 风险档：中 · 基线：develop（含 PR #257 合并提交 `325735c7`） · 实施：Codex · 审查与跟进：Claude

## 1. 需求

下游项目反馈：模板没有任何遥测导出，资源服务各自手写了 OpenTelemetry 加 Prometheus `/metrics` 的接线；项目的监控设计已假定"第三方与跨服务调用的成功率、耗时由 OpenTelemetry 自带的 HttpClient 指标进观测平台"。请模板按官方做法提供默认接线，并说明端点暴露、鉴权与 Worker 宿主的导出方式。

leistd 侧用户 2026-11-10 已确认：
- 进模板代码，不进框架包（导出是宿主组合，官方 Aspire ServiceDefaults 也是解决方案里的项目代码）。
- 默认 OTLP 推送，不默认开 Prometheus `/metrics`：官方 Prometheus ASP.NET Core 导出器只有预发布版，README 写明生产应考虑 OTLP，且抓取端点默认不受保护；HttpListener 导出器只用于开发内循环。
- 指标与追踪同一开关一起加；日志继续走 Serilog，不改。

非目标：
- 不导出日志到 OTLP，不替换 Serilog。
- DbMigrator 一次性作业不接。
- 不给框架组件加自定义 Meter，不在模板里新增业务指标示例。
- 不提供 Prometheus 抓取端点的代码；需要拉取的部署按规范里的非默认做法自行接。

验收：
- Given 没有配置 `OTEL_EXPORTER_OTLP_ENDPOINT`（或为空白）；When 启动 Api 并处理请求；Then 指标与追踪都不登记 OTLP 导出器、不产生 OTLP 导出流量（采集管道照常注册，只是没有导出器）。
- Given 配置了 `OTEL_EXPORTER_OTLP_ENDPOINT` 指向 OpenTelemetry Collector；When 一个入站请求在处理中发起出站 HttpClient 调用；Then Collector 收到：
  - 指标：`http.server.request.duration`、`http.client.request.duration` 的数据点，以及一项 .NET 10 内置运行时指标（按 `System.Runtime` 取证，不硬编码旧版 `process.runtime.dotnet.*` 名称）；
  - 追踪：入站请求的 Server span 与出站 HttpClient 的 Client span 同一 TraceId，且 Client span 是 Server span 的后代（核对父 SpanId 链）；
  - 以生成后项目前缀命名的 Meter（一个 Counter）与 ActivitySource（一个 Activity）无需另行登记即到达 Collector；不匹配前缀的不到达。
- Given 已对普通 API 建立正向对照；When 调用 `/api/health/live`、`/api/health/ready` 并完成导出屏障；Then 两个健康端点的服务端 span 不导出，且不计入 `http.server.request.duration`（活动请求数与 Kestrel 连接指标不在保证范围）。
- Given 配置了 OTLP；Then 不注册 OpenTelemetry 日志 provider，Collector 的 logs 管道收不到本服务日志。
- Given 配置了 `OTEL_SERVICE_NAME`；Then 指标与追踪的 `resource.service.name` 都取该值。
- Given 同一入口被调用两次；Then 指标与追踪各只有一个 OTLP 导出器。

## 2. 方案

现状：模板 Api 用 Serilog 与框架的 CorrelationId，没有 OpenTelemetry 包；健康检查映射在 `Program.cs` 的 `/api/health/live`、`/api/health/ready`；模板后端只有 Api 与 DbMigrator 两个宿主。

设计（照官方 Aspire ServiceDefaults 的指标与追踪部分；它的日志部分、服务发现、重试与健康检查注册不照搬）：
- 新增 `Api/Hosting/TelemetryExtensions.cs`：`AddMyProjectTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder`。泛型约束用 `IHostApplicationBuilder`，Web 与通用宿主（Worker）都能调用同一方法。
  - 指标：`AddAspNetCoreInstrumentation()`、`AddHttpClientInstrumentation()`、`AddRuntimeInstrumentation()`、`AddMeter("CompanyName.ProjectName.*")`。
  - 追踪：`AddSource("CompanyName.ProjectName.*")`、`AddAspNetCoreInstrumentation`（过滤 `/api/health` 前缀）、`AddHttpClientInstrumentation()`。
  - 导出：一处判定 `!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])`，成立时在指标与追踪的 builder 上各自调用 `AddOtlpExporter()`。**不用 `UseOtlpExporter()`**：它同时启用日志、指标、追踪三路（OTLP 1.20.1 源码），与"日志不动"冲突。不注册 OpenTelemetry 日志 provider。协议、地址、头、服务名都交官方 `OTEL_*` 环境变量，不另设配置节，不加连通性门禁、重试或热重配。
  - 幂等：重复调用不重复追加导出器（`AddOtlpExporter` 每次追加 reader/processor）。沿用邻近 `WebHostExtensions` 的入口私有注册标记，不加锁或全局状态。
  - 健康检查排除：追踪 Filter 用 `context.Request.Path.StartsWithSegments("/api/health")`，避免误伤 `/api/healthcare` 一类路径。
  - 前缀常量是本类的采集匹配细节，留在类内；Application、Domain 自己按规范命名 Meter 与 ActivitySource，不为复用常量引用 Api。
- `Program.cs` 在 `AddMyProjectLogging()` 之后调用 `builder.AddMyProjectTelemetry()`；两条健康检查映射追加官方 `DisableHttpMetrics()`。
- 包（`Directory.Packages.props` 登记稳定版，Api 引用）：`OpenTelemetry.Extensions.Hosting` 1.20.1、`OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.20.1、`OpenTelemetry.Instrumentation.AspNetCore` 1.20.2、`OpenTelemetry.Instrumentation.Http` 1.20.2、`OpenTelemetry.Instrumentation.Runtime` 1.20.1。实施时以 nuget.org 当时的最新稳定版为准，不用预发布版。
- 文档：
  - `template/docs/deploy/configuration.md` 新增"遥测导出"一节：
    - 只由 `OTEL_EXPORTER_OTLP_ENDPOINT` 控制启用；`OTEL_EXPORTER_OTLP_PROTOCOL` 与地址要配对（SDK 默认 gRPC、常用端口 4317；`http/protobuf` 常用 4318，通用 endpoint 写基地址）；`OTEL_SERVICE_NAME`；`OTEL_EXPORTER_OTLP_HEADERS`（采集器认证）。信号专用变量只写 `AddOtlpExporter()` 在该版本实际支持的，不笼统承诺。
    - Compose 只转发 `backend.environment` 里显式列出的变量：启用遥测时把上述变量加进去；写进 `.env` 不会自动进容器。本次不改 Compose 与 `.env.example`，不加必填项。
    - 自建采集器建议只在集群内可达（托管采集器按服务商要求）；进 Prometheus 用 Collector 转换或支持 OTLP 接收的 Prometheus。确需拉取时的非默认做法：抓取端点挂单独内部端口、网关不转发、依赖预发布包由项目承担，附官方链接。
  - `template/docs/standards/coding-backend.md` §7：Meter 经 `IMeterFactory` 创建、ActivitySource 同样用项目前缀命名；指标标签必须有界（不带租户 Id、用户、地址），只在确有可变维度时设上限归入 `other`，不建通用截断组件。短规则。
  - 同一节一句话：新增独立 Worker 宿主时把 `TelemetryExtensions` 源码及其包引用迁到宿主共用位置后调用同一方法，Worker 不反向引用 Api；Worker 没有入站 HTTP，也不需要指标端点。
  - `template/docs/standards/tech-stack.md` 技术栈表补一行 OpenTelemetry。
  - `template/docs/README.md` 按任务读取表补遥测配置与排障入口。
  - 改 `template/docs/` 后执行读取成本闸门，不为通过而上调预算。
- 不出框架 beta；模板源码合入 develop 即可供下游移植，模板包随下一次正式发布。

## 3. 计划

分工：Codex 实施代码、测试与端到端验收；Claude 做代码审查、跟进与回执。实施遵循模板的前后端开发规范、目录与分层规范、文件命名规范（`template/docs/standards/coding-common.md`、`coding-backend.md`（§2 分层与目录、§5 命名）、`coding-frontend.md`、`project-structure.md`、`testing.md`，模板维护规则 `docs/template/development-guide.md`）。

1. 实施上述代码与文档。所有角色都有 Api，不新增模板参数或词条；按现有选择器核对本地化开/关、Identity、Standalone、Resource 与 Resource 纯 API 的生成产物。前端不受影响，不改前端依赖与 Mock。
2. 新增测试（`template/docs/standards/testing.md` §2.2）：
   - 裸服务集合可验证的放 UnitTests/Registration：端点缺失或空白时两种导出器都不登记；有效端点时两者都登记且没有 OpenTelemetry 日志 provider；重复调用不重复登记导出器；前缀匹配与不匹配的 Meter、ActivitySource。
   - 需要建宿主的放 IntegrationTests。
   - 受影响生成场景的后端构建、单测与集成测试照常通过；代表场景完整构建，其余完整阶段交 CI。
3. 端到端验收（真实 Kestrel、真实 HttpClient、真实 Collector；不需要浏览器）：
   - 环境：复用 `scripts/test-template-oidc-e2e.ps1` 的 Identity + Resource 环境（DbMigrator 初始化真实 PostgreSQL，再原样启动生成的 Api），记录场景、生成名、候选框架包来源、Collector 镜像版本与配置。构建与容器按 remote-project-runner 调度。
   - 入站触发出站：Resource 当前用户 `/me` 的账户偏好回源 Identity（写明令牌与数据准备）。不在模板生产 API 里加示范端点。
   - 项目前缀：用只属于验收的夹具在 Api 进程内创建带生成后前缀的 Meter 记一个 Counter、启动并结束一个 Activity（例如经 `ASPNETCORE_HOSTINGSTARTUPASSEMBLIES` 加载的验收程序集），另造一个不匹配前缀的作对照。夹具不进模板源码。
   - Collector 同时开 metrics、traces、logs 三条接收管道（file 导出器落盘便于断言）。验收环境设 `OTEL_METRIC_EXPORT_INTERVAL`、`OTEL_BSP_SCHEDULE_DELAY` 缩短批次，用有上限的条件等待或 flush 屏障，不用固定睡眠判成功。
   - 无端点：清除继承的 `OTEL_*` 变量启动，结合第 2 步的"无导出器"断言与真实启动日志取证；不能只凭 Collector 没收到数据判定。
   - 按 §1 逐条断言并记录命令、断言与原始产物路径，结论写进本规格 §4。
4. 仓库静态检查（`scripts/check-all.ps1` 全部闸门）、模板读取成本与条件块、模板包测试 `pwsh template-package/test-template-package.ps1`（核对新增源文件入包与隔离生成、重建）。开发工作区用 `-Sync`、场景选择器与 `-GenerateOnly` 验证条件、名称替换与文档链接。
5. 本地提交（不推送），交 Claude 代码审查；审查通过后由 Claude 推送、开 PR、处理 CI 并合并。

## 4. 结果

2026-11-10 实施与本地端到端验收完成，待 Claude 审查；仅本地提交，未推送。新增模板宿主入口、五个稳定版包引用及版本、两个健康端点的指标排除和注册测试。常驻文档已同步 `template/docs/deploy/configuration.md`、`standards/coding-backend.md`、`standards/tech-stack.md` 与 `README.md`。未改 Framework、前端依赖、Mock、Compose 或 `.env.example`，未新增 Worker 项目。

### 执行环境与复现入口

可迁移工作均经 remote-project-runner `--target auto` 调度。doctor 发现 worker-01 不可达，选择 local，在隔离源码副本执行；未修改机器配置。复用框架候选源 `.tmp/local-feed`，版本 `1.2.3-local.20261109153812`、SDK `10.0.402`，工作区来源断言确认 Leistd 包只取自该候选源。源码快照、节点、步骤结果与清理回执见 `.tmp/otel-telemetry/evidence/runner/summary.json` 及同目录各任务回执。

验收脚本及 Collector 配置均只在 `.tmp/otel-telemetry/`。可复现入口为 `validate.ps1`（代表后端）、`unit-check.ps1`（最终单测）、`final-checks.ps1`（静态与生成）、`package-check.ps1`（最终模板包）、`e2e.ps1`（真实遥测）；对应调度计划为 `submitted-plan.json`、`unit-plan.json`、`final-plan-2.json`、`package-plan.json`、`e2e-plan-3.json`。计划中的隔离输入目录在取证后清理，重跑时用 `snapshot.py` 从本工作树重建；这些脚本不进入模板载荷。

E2E 场景为 `identity-localization` → `Matrix.IdentityLocalization`（本地化开）和 `resource` → `Matrix.Resource`（本地化关）。通过正式工作区入口 `-Sync` 生成，DbMigrator `--apply` 在真实 PostgreSQL 18 初始化两个服务各自的 schema，再以 Kestrel 启动 API。只从现有 OIDC E2E 加载身份准备、HTTP、进程和迁移搭建函数，未执行其四服务环境或协议/到期用例全集。

Identity 的管理员由迁移播种，口令、证书和机器客户端机密均在验收进程内新建，不读取现有凭据。管理员登录后给公共客户端 `orders-web` 走授权码 + PKCE，申请 `openid profile email roles orders-api`；Resource 的机器身份仅授予 `tenant-routing.read account-preferences.read`。对该管理员调用 Resource `/api/v1/auth/me`，读取其 Identity 账户偏好默认表示。验收以目标 `/api/v1/internal/account-preferences/host/{userId}` 的真实 200 Client span 为依据，未将发现文档、取令牌请求或 `/me` 的 200 单独当成回源成功。

Collector 为 `otel/opentelemetry-collector-contrib:0.140.0`，镜像摘要 `sha256:ffc818ee108d0b934fd14207fd87ff247c9b64f344ed349d0b66c166c18a2312`。`collector.yaml` 同时启用 metrics/traces/logs 接收与 file 导出，服务用 HTTP/protobuf 推送，指标间隔 500 ms、追踪批次间隔 100 ms。Hosting startup 只挂载验收逻辑；首个请求在 provider 启动后发射匹配/不匹配前缀的 Counter 和 Activity，未另行登记前缀或导出器。两个 provider 的 `ForceFlush` 均成功，并以 Collector 文件中的条件断言为有上限的等待屏障。

### 验收证据

下表产物路径均相对 `.tmp/otel-telemetry/evidence/`；Collector 原始文件没有改写。

| 验收 | 命令与断言 | 实际结果与原始产物 |
| --- | --- | --- |
| 无端点或空白不登记导出器 | `dotnet test .tmp/dev/resource-capabilities-03/project/backend/tests/Matrix.ResourceCapabilities03.UnitTests -c Release`；SDK 实际创建阶段分别统计指标/追踪的 exporter options，覆盖 null、空字符串、空白；`pwsh .tmp/otel-telemetry/e2e.ps1` 清除继承的全部 `OTEL_*` 后启动真实 API 并请求普通接口 | 通过；缺省/空白两路均 0 个，真实启动与请求成功。`unit-final/unit.trx`、`e2e/assertions.jsonl`、`e2e/idp.log`。不以 Collector 没收到数据单独证明无导出器 |
| 有端点的 HTTP 与运行时指标 | `pwsh .tmp/otel-telemetry/e2e.ps1`；`python3 .tmp/otel-telemetry/assert-collector.py .tmp/otel-e2e` 核对实际数据点 | 通过；收到 `http.server.request.duration`、`http.client.request.duration`，以及 `System.Runtime` 的 `dotnet.process.memory.working_set` 等 18 项运行时指标。`e2e/collector/metrics.jsonl`、`e2e/collector-assertions.json` |
| 入站/出站追踪父子关系 | 同一 `/me` 请求带指定 traceparent；按目标偏好路径选 Client span，沿 parentSpanId 追溯 | 通过；TraceId 为 `610fae32c0aeee05891410390538c928`，Resource Server `c168e551b38e4e60` → Client `52a18b12fc6b95f6` → Identity Server `2a0806c7706edcad`，后两者均为 200。`e2e/collector/traces.jsonl`、`e2e/trace-chain.json` |
| 项目前缀正反例 | 同上；两服务各发射一个 `otel.fixture.counter` 和 `otel.fixture.barrier`，对照 `Unrelated.Acceptance` | 通过；两个生成后的项目前缀均收到，`otel.fixture.unmatched` 指标与 Activity 均未收到。`e2e/collector/metrics.jsonl`、`e2e/collector/traces.jsonl`；单测也验证实际采集选择，见 `unit-final/unit.trx` |
| 健康检查排除 | 普通 `/me` 耗时指标及 `/api/healthcare` span 建立正向对照，再调用 live/ready 并完成 flush 屏障；核对服务端 span 路径与请求耗时路由 | 通过；`/api/healthcare` 正常采集，两个健康路径无 Server span，也无 `http.server.request.duration` 数据点；活动请求数与连接指标不在断言范围。`e2e/assertions.jsonl`、`e2e/collector-assertions.json` 及原始 metrics/traces 文件 |
| 日志保持 Serilog | 注册测试解析所有 `ILoggerProvider`；E2E 发射 Information 日志，同时保留 Collector logs 管道 | 通过；无 OpenTelemetry 日志 provider，Serilog 输出验收日志，Collector `e2e/collector/logs.jsonl` 为 0 字节。`unit-final/unit.trx`、`e2e/idp.log`、`e2e/orders.log` |
| OTEL_SERVICE_NAME | 同上；按 resource.service.name 同时查指标与追踪 | 通过；两服务的两种信号分别为 `otel-idp`、`otel-orders`。`e2e/topology.json` 与 Collector 原始文件 |
| 重复调用幂等 | `dotnet test .tmp/dev/resource-capabilities-03/project/backend/tests/Matrix.ResourceCapabilities03.UnitTests -c Release`；入口调用两次后分别解析两种 provider 并核对实际 exporter options 创建次数 | 通过；两路各 1 个；真实导出数据也无重复 SpanId/指标数据点。`unit-final/unit.trx`、`e2e/collector-assertions.json`。单测注入内存 HTTP handler，释放 provider 时也不访问网络 |

Collector 最终 20 条断言全部通过，取证文件含 32 个 span 和 1521 个指标条目（含多个导出批次）；具体判据与枚举见 `e2e/collector-assertions.json`。原始日志、迁移日志、身份准备状态断言及拓扑均保存在 `e2e/`。

### 构建、测试与仓库检查

| 检查 | 命令与结果 | 产物 |
| --- | --- | --- |
| 代表后端 | `pwsh scripts/template-dev-workspace.ps1 -Scenario resource-capabilities-03 -Sync`；工作区内 `dotnet build .tmp/dev/resource-capabilities-03/project/backend/Matrix.ResourceCapabilities03.sln -c Release`、`dotnet format style .tmp/dev/resource-capabilities-03/project/backend/Matrix.ResourceCapabilities03.sln --verify-no-changes --no-restore --diagnostics IDE0001 IDE0002` | `backend/workspace-resource-api.log`、`backend/backend-build.log`、`backend/backend-format.log`；全后端构建 0 警告、0 错误，风格通过 |
| 单元与集成 | `dotnet test .tmp/dev/resource-capabilities-03/project/backend/tests/Matrix.ResourceCapabilities03.UnitTests -c Release` 最终 134/134；`dotnet test .tmp/dev/resource-capabilities-03/project/backend/tests/Matrix.ResourceCapabilities03.IntegrationTests -c Release --no-build` 真实 PostgreSQL 129/129；另两个 E2E 角色的全后端均构建成功，0 警告、0 错误 | `unit-final/unit.trx`、`backend/integration.trx`、`e2e/build-idp.log`、`e2e/build-orders.log`；最终测试改动仅为禁止单测网络，集成与生产代码未变 |
| 全部静态闸门 | `pwsh scripts/check-all.ps1` 最终 50/50 通过，含条件块、符号、读取成本、引用与 Skill | `static-and-generation/static-all-final.log`；读取成本未调预算，常驻 4467/4700、后端任务 32171/34700 字符 |
| 选择器与实际生成 | `python3 scripts/plan-quality-checks.py --local-scenarios --tier core --base 325735c7ec98641bc586c3923acd1d9f1e1f897b --output .tmp/otel-telemetry/local-scenarios.json`；`pwsh .tmp/otel-telemetry/final-checks.ps1` 调用正式矩阵入口的 `-Scenarios $scenarios -GenerateOnly` | 选择器为 representative（CPM/项目引用属共享输入）；6 个所选场景加 `resource`、`resource-localization` 共 8 个，实际生成、替换、条件裁剪、文档链接/锚点、Compose 结构和 verify 清单均通过。`../local-scenarios.json`、`static-and-generation/generate-only.log` |
| 最终模板包 | `pwsh template-package/test-template-package.ps1` | `package/template-package.log`；1575 个文件与源码一致，新增入口及测试入包，Identity 全能力与纯 API Resource 隔离生成、来源标记反例、UserSecretsId 归一化后重建零差异均通过 |

首次运行遇到环境与夹具问题，均已解决并留在进度台账：调度器的 NuGet 缓存变量覆盖工作区隔离目录；默认 Python 缺 PyYAML；快照主动排除 `.mcp.json`；临时脚本变量遮蔽、误用仅 CI 的预检跳过参数，以及缩减 OIDC 环境时漏配 Token Exchange 和多余 Audience 权限。最终通过为正式入口，不设置伪 CI 环境，也未放宽断言。PyYAML 复用本机既有纯 Python 包，仅在隔离子进程设置 PYTHONPATH；MCP 仅投影已核对的非机密 Spartan 命令与精确版本，未传认证配置。

未执行完整 26 场景的构建/测试矩阵与 1280 形态全集，按 §3 由后续 CI 承担；未重跑不相关的 OIDC/到期全集、前端浏览器测试、Framework 打包或发布。未执行 nuget.org 正式模板发布还原检查，本次只验本地候选与模板包，不发布。所有本轮调度任务的资源回执均为 `cleanupComplete=true`，不保留临时 API、Collector、PostgreSQL 或任务卷；只保留本目录证据及执行器缓存/回执。
