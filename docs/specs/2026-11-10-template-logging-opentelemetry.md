# 模板日志改用 ILogger 内置提供程序与 OpenTelemetry，遥测导出可在系统设置中配置

状态：实施中（第 6 版方案经 Codex 三轮复审通过） · 风险档：高（改宿主日志管道、设置取值域、已存数据与两处框架行为） · 基线：develop `94fcd836`（含 PR #258 遥测入口） · 实施与端到端验收：Claude（用户 2026-11-10 指定） · 代码审查：Codex

## 1. 需求

PR #258 让指标与追踪走 OpenTelemetry，日志仍是 Serilog，两套管道并存，日志不能在观测平台里与追踪互跳。按"最佳实践终局"，日志改为 .NET 官方默认组合：代码写 `ILogger`，宿主用内置提供程序输出控制台，OpenTelemetry 日志提供程序经 OTLP 导出，三路信号一条通道。leistd 侧用户 2026-11-10 确认走这条路线。

依据：
- Aspire ServiceDefaults 与 .NET 可观测性文档都是 `ILogger` 加 OpenTelemetry；OpenTelemetry 日志记录自动带 `Activity.Current` 的 TraceId/SpanId。
- 模板只用到 Serilog 的四件事，都有官方对应：
  - 控制台：内置 Console 提供程序；
  - 按类别覆盖级别：`Logging:LogLevel`；
  - 运行期改级别：内置日志过滤随可重载配置即时生效；
  - 请求完成日志：内置 HTTP 日志 `CombineLogs` 加拦截器。

遥测导出配置（leistd 侧用户 2026-11-10 追加）：
- 观测平台的 Key 要定期轮换，用户要求在系统设置里配置，不重新部署、不发版；机密值经设置组件的 Data Protection 加密。模板设置规范已有"管理员运行期维护的第三方集成客户端凭据做成机密设置"的例外，与此一致。
- OpenTelemetry .NET SDK 目前不支持运行期重配导出器（截至 2026-11-10，最新稳定版 1.20.1）：issue #7629 打开，PR #7842（为已存在的导出器换客户端，不含增删导出器）未合并。官方扩展点 `OtlpExporterOptions.HttpClientFactory` 在 `http/protobuf` 下用于全部导出请求。
- 启动顺序决定了数据库设置赶不上导出器登记：`Build()` 解析日志时就构建了 OTel 日志 provider，`UseHostSettings()` 只挂配置源，数据库值到 `HostSettingStartupService.StartAsync()` 才加载（Codex 第 4 版审查核对源码）。因此不按设置决定"登记与否"，改为导出器常驻、发送层按当时的设置决定"发不发、发到哪、带什么头"（用户 2026-11-10 选定）。
- 服务名不另设配置：工作负载身份 `Leistd:ServiceAuth:ClientId` 已是"这个服务是谁"的唯一定义，下游项目已按每个工作负载各配一个（如 `orders-service`、`orders-worker`、`identity-service`）。

非目标：
- 不改业务代码的 `ILogger` 用法与结构化消息模板规范。
- 不引入文件日志。
- 不改指标与追踪的已定行为。
- 不改下游三仓（回执给同步步骤）。
- 不重写历史规格与历史验收记录。
- 不引入自定义日志工厂、formatter、富化器或脱敏体系。

验收：
- **依赖**：生成项目不再引用任何 Serilog 包；宿主与测试不出现 `Serilog` 命名空间。
- **控制台**：
  - Simple（默认）与 JSON（`Logging:Console:FormatterName=json`）都能读到 `leistd.correlationId`，切到租户后读到 `leistd.tenantId`：Simple 是可读的 `键:值`，JSON 是结构化键值。
  - 真实 stdout 取证。
- **级别**：
  - 用一个没有类别覆盖的业务类别：`Logging:LogLevel:Default` 生效；系统设置"最小日志级别"改为 `Warning` 后不重启即过滤，清除后回到部署配置。
  - 显式类别覆盖不受该设置影响。
- **请求完成日志**：
  - 每个请求一条合并日志，级别固定为官方的 Information；含方法、路径（无查询串）、状态码、耗时；带 `leistd.correlationId` 与本请求的 `leistd.tenantId`。
  - 开关关闭时，200、有响应体的 4xx、无响应体的 204 记 0 条，500 记 1 条（按该请求精确计数）。
  - 租户字段覆盖：普通租户成功请求、租户 204、租户请求里业务异常转 5xx、宿主请求（值为 null）。
- **OTLP 日志**：
  - 有效配置启用导出时，Collector logs 管道收到业务日志：其 TraceId/SpanId 与同一业务请求的 Server span 一致，两个作用域键为属性，`service.name` 与指标、追踪相同。
  - 日志提供程序与三路导出器常驻，各只有一个；重复调用入口仍各一个。
  - 有效配置未启用（部署端点为 null、空串、空白且系统未设置，或系统显式关闭）时，不产生任何 OTLP 网络流量。
- **操作记录日志**（未启用操作记录库的形态）：一个含两个失败参数名的真实失败操作，JSON 控制台的 `State.OperationFailureDataKeys` 与 OTLP 属性都是可读的两个键名。
- **数据迁移**：真实 PostgreSQL 上从上一条迁移建旧库、写入旧值后升级，全部映射正确；降级到上一条再升级正确。
- **设置页**：请求日志开关在 Mock 或真实设置页完成显示、切换、刷新、清除回落，本地化开/关各一次。
- **遥测导出设置**（三项都即时生效，不需要重启）：
  - 部署配置未启用、只在系统设置打开并填地址：不重启，三路随后到达 Collector。
  - 部署配置已启用（`OTEL_EXPORTER_OTLP_ENDPOINT` 有值）、数据库里已保存关闭：**从进程启动起**接收端计数为 0，包括启动期间的告警日志与主动 flush。定向测试阻塞首次设置读取并触发导出，证明就绪前不发送、就绪后遵守数据库值。
  - 只配 `OTEL_EXPORTER_OTLP_ENDPOINT`、数据库没有覆盖：设置接口与页面的开关、地址继承值与发送层行为一致；写入覆盖再清除后仍一致。
  - 改导出地址：后续导出发到新地址，路径为 `/v1/logs`、`/v1/metrics`、`/v1/traces`。
  - 认证头：A→B、去掉其中一个头、清除系统值回落部署值，三种情形下三路请求里每个认证头只有当前的一份。
  - 公共部署值、按信号部署值、系统覆盖同时存在时，最终协议是 `http/protobuf`，地址与认证头取系统覆盖；清除后回到部署配置。
  - 开关关闭时子项不显示，打开后缩进显示；认证头只写不回显，库里是密文。
- **服务名**：`service.name` 依次取 `OTEL_SERVICE_NAME`、`Leistd:ServiceAuth:ClientId`、程序集名（三种情形各核对一次），三路信号一致。
- **测试**：日志相关集成测试改用 `Microsoft.Extensions.Diagnostics.Testing` 的 `AddFakeLogging` / `FakeLogCollector`，不再依赖 Serilog sink。

## 2. 方案

### 2.1 宿主日志组合

- `Api/Hosting/LoggingExtensions.cs` 的 `AddMyProjectLogging`：
  - 去掉 `AddSerilog`，保留宿主默认提供程序；
  - 注册 `AddHttpLogging` 与拦截器（§2.3）；
  - `AddCorrelationId()` 保留。
- `Program.cs`：
  - 去掉启动期静态 Serilog logger 与包住宿主的 `try/catch/Log.Fatal/CloseAndFlush`。启动失败照常非零退出：宿主构建前的异常由运行时输出到标准错误，构建后由宿主日志记录。
  - 原写租户的 `IDiagnosticContext` 中间件改为 §2.3 的 `Items` 写入。
- `appsettings.json`：`Serilog` 节改为 `Logging` 节。
  - `LogLevel`：`Default=Information`；保留现有覆盖 `Microsoft.AspNetCore=Warning`、`Microsoft.EntityFrameworkCore=Warning`、`System.Net.Http.HttpClient=Warning`；未启用操作记录库时 `Leistd.OperationRecords=Information`；新增 `Microsoft.AspNetCore.HttpLogging=Information`，否则请求日志被 `Microsoft.AspNetCore=Warning` 滤掉。
  - 控制台：`Console:FormatterName=simple` 与 `Console:FormatterOptions:IncludeScopes=true`，Simple 与 JSON 共用 `FormatterOptions`，不用旧的 `Console:IncludeScopes`。
    - `FormatterName=simple` 必须显式写出并保留：宿主默认 `AddConsole()` 不设名称，名称为空时 .NET 10 走旧选项兼容分支，旧 `IncludeScopes=false` 会覆盖新节的 true。
    - 部署要结构化输出时只覆盖 `Console:FormatterName=json`；代码里不调用 `AddSimpleConsole` 固定格式。
- Api 的 Development、Production 配置当前没有日志节，不新增；DbMigrator 用 Generic Host、无 Serilog，不改、不接 OTel。
- `backend/Directory.Packages.props` 删除 `Serilog.AspNetCore` 的中央版本，Api 删除包引用。测试日志包已存在，不重复引入。

### 2.2 OpenTelemetry 日志与导出管道

- `AddMyProjectTelemetry` 加日志信号：OpenTelemetry 日志提供程序选项 `IncludeScopes = true`、`IncludeFormattedMessage = true`。
- **导出器常驻**：日志、指标、追踪各用一次逐信号 `AddOtlpExporter()`（不用 `UseOtlpExporter()`：发送层已完全接管地址与认证头，不需要它的按信号环境变量），无论是否启用都登记。
  - 在各自的选项配置里固定 `Protocol = HttpProtobuf`，清空 SDK 的 `Headers`（不让它读 `OTEL_EXPORTER_OTLP_HEADERS` 再在请求里加一份），并设 `HttpClientFactory` 为 §2.7 的发送层客户端。
  - 显式写入的选项在 SDK 读环境变量之后生效，协议因此不会被 `OTEL_EXPORTER_OTLP_PROTOCOL` 覆盖。
- **代价（已知并接受）**：未启用导出时，采集、批处理与序列化照常运行，只在发送层丢弃，CPU 有少量开销；文档写明。
- 服务名：在共用的 `ConfigureResource` 末尾 `AddService`，取值依次为 `OTEL_SERVICE_NAME` → `Leistd:ServiceAuth:ClientId` → 宿主应用名（与 `service-info` 一致），从宿主 `IConfiguration` 取第一个非空白值；保持默认资源构建器，不在其后再追加环境检测器（否则 `OTEL_RESOURCE_ATTRIBUTES` 里的 `service.name` 会反过来覆盖）。`service.version` 取程序集版本。
- 幂等沿用现有私有标记，不加第二份状态。
- `TelemetryRegistrationTests` 改为：三路各一个导出器与一个日志提供程序（启用与否都如此）；重复注册各一个；协议恒为 `HttpProtobuf`；服务名三种来源，外加一个冲突的 `OTEL_RESOURCE_ATTRIBUTES` 证明优先级。
- `deploy/configuration.md` 遥测一节改写：
  - 日志也走 OTLP；协议固定 `http/protobuf`；
  - 导出开关、地址、认证头可在系统设置运行期修改，部署配置 `OTEL_EXPORTER_OTLP_ENDPOINT` / `OTEL_EXPORTER_OTLP_HEADERS` 是兜底；
  - 只支持"一个基地址，按信号追加 `/v1/{signal}`"，各信号地址完全不同的平台经 Collector 接入；
  - 未启用时仍采集；
  - 推荐有 Collector 的部署把平台 Key 放在 Collector；
  - 删除"不要使用会启用日志的入口"。
  - 生成项目文档只写当前行为，不写本仓规格链接与未落地路线图（分发边界）。

### 2.3 请求完成日志

- 内置 HTTP 日志替换 `UseSerilogRequestLogging`：
  - `AddHttpLogging`：字段 `RequestMethod | RequestPath | ResponseStatusCode | Duration`，`CombineLogs = true`；不记头、正文、查询串（官方提示可能记录个人信息，取最小字段）。
  - `UseHttpLogging()` 放在 `UseCorrelationId()` 之内，日志落在关联标识作用域里。
- 租户：
  - 租户解析后的那段小管道把本请求解析出的租户 Id 存入 `HttpContext.Items`（宿主或未进入租户管道时为 null）。
  - 拦截器从 `Items` 读取并 `AddParameter(TenantLogKeys.TenantId, …)`；不在完成阶段读 `ICurrentTenant`，因为租户中间件退出后已恢复，204 与外层异常转换都在那之后。
  - 不扩展租户组件的公共 API。
- `RequestLoggingInterceptor`（`IHttpLoggingInterceptor`，`AddHttpLoggingInterceptor<T>()` 注册）：
  - `OnResponseAsync`：请求日志关闭且状态码 < 500 时，设 `LoggingFields = None`、调用 `Parameters.Clear()` 并返回（官方中间件在末尾按参数是否为空决定输出，ASP.NET Core 10 源码可证）；否则补租户参数。
  - 每次读 `IOptionsMonitor<RequestLoggingOptions>.CurrentValue`，不把请求级服务捕获进单例。
  - 放 `Api/Hosting/`，同步在 `project-structure.md` 写明 Hosting 目录也承载宿主日志适配。
- 已知边界写入文档：
  - 请求完成日志固定 Information。
  - 默认配置下全局级别调到 Warning 时 5xx 仍有完成日志。
  - 部署若显式把 `Microsoft.AspNetCore.HttpLogging` 调到 Warning 或 None，官方中间件整体不记，不承诺无条件记录 5xx。
  - 不另加 Error 级请求记录器。

### 2.4 设置与取值域

- `Logging.MinimumLevel`（宿主级，设置名不变）：
  - 绑定从 `Serilog:MinimumLevel`（及 `:Default`）改为 `Logging:LogLevel:Default`，只影响 Default，不覆盖显式类别。
  - 候选值为 `LogLevel` 名称 `Trace`、`Debug`、`Information`、`Warning`、`Error`、`Critical`，不含 `None`。
  - `SettingConstant.Logging.Levels`、前端 `LOG_LEVEL_CHOICES` 与 `LOG_LEVEL_DESCRIPTIONS`、`core/settings/setting.constants.ts`、`public/i18n/settings/{en,zh-CN}.json` 的级别提示、Mock 同步；旧提示里"记录每条 SQL"一类承诺按类别覆盖后的实际行为改写。
- `Logging.RequestLevel` → `Logging.RequestLogging`（宿主级布尔）：
  - 定义用 `AsBoolean()`，默认值由 `RequestLoggingOptions.Enabled = true` 提供，定义不重复写死；绑定 `BindOption` 到 `RequestLogging:Enabled`。
  - 删除 `setting-section.ts` 里请求级别的专门分支与提示，按现有布尔路径渲染。
  - 词条与显示名中英同步（`Setting:Logging.RequestLogging`）。
- **数据迁移**：
  - **位置**：在现有 `Migrations/Identity`、`Migrations/Resource` 业务上下文迁移集各加一条（Standalone 用 Identity 分支）；Control、OpenIddict 不持有这些设置，不加。数据变化不改模型快照、不重写基线。
  - **范围**：只改 `SettingRecord` 里宿主级行，即 `ScopeKey = 'host:t'` 且 `TenantId`、`UserId` 都为空；不按 `TenantId IS NULL` 选（会带上宿主用户偏好）。
  - **`Up`**：
    - `Logging.MinimumLevel`：`Verbose→Trace`、`Debug→Debug`、`Information→Information`、`Warning→Warning`、`Error→Error`、`Fatal→Critical`。
    - `Logging.RequestLevel` 改名 `Logging.RequestLogging`：`Verbose→false`、`Debug→false`，`Information`、`Warning`、`Error`、`Fatal→true`。
    - 旧键不存在不补行；非目标行不动；重跑依赖 EF 迁移历史，不加自制标记。
  - **`Down`**：最小级别 `Trace→Verbose`、`Critical→Fatal`，其余同名；请求日志 `false→Verbose`、`true→Information` 并改回旧名，明确不恢复原档位。
  - **已知语义变化**（写进文档与回执）：`Verbose/Debug→false` 是以默认 Information 基线做的产品映射。旧全局级别为 Debug 时，原 Debug 请求会输出；旧全局为 Error 时，原 Information 请求不会输出。改为开关后两者都按开关处理。
- 值域在设置写入入口校验一次，拦截器不再校验字符串或兼容旧键；代码里不保留旧取值域。

### 2.5 框架行为（两处，随框架 beta 发布）

- **作用域状态可读**：`CurrentTenant.Change` 与 `CorrelationIdMiddleware`、`CorrelationIdAmbientContextContributor` 现在把普通字典交给 `BeginScope`，Simple formatter 只打印类型名。
  - 改为各组件内部一个最小作用域状态类型：实现 `IReadOnlyList<KeyValuePair<string, object?>>`，只含原键（`leistd.tenantId` / `leistd.correlationId`），`ToString()` 返回 `键:值`。
  - JSON formatter、OpenTelemetry 照常取键值；Simple 输出可读。
  - 键名与语义不变；补组件单测：Simple 渲染、键值枚举。
- **操作记录日志字段**：`OperationRecordLogEmitter` 的 `OperationFailureDataKeys` 从 `string[]` 改为逗号连接的字符串，内置 JSON formatter 与 OTLP 都完整可读。
  - 同步 `framework/docs/components/operation-records.md` 的字段说明。
  - 日志字段类型变化影响按数组解析的日志消费方，按 `docs/framework/versioning.md` 记为破坏性变更（`BREAKING CHANGE:` 脚注）。
- **框架文档**：
  - `Leistd.Settings.Hosting` 注册入口的 XML 示例与 `settings.md` 的 `.Bind("Logging.MinimumLevel", ...)` 改为 `Logging:LogLevel:Default`；
  - `tracing.md`、`multi-tenancy.md` 区分"提供程序打开 `IncludeScopes` 采集结构化作用域"与"Simple 输出 `键:值`"。
- **验证**：受影响组件单测、打包与候选包消费（只针对改动的包），文档 API 漂移检查。

### 2.6 模板测试与维护资产

- **集成测试**：`ProjectWebApplicationFactory` 注册 `AddFakeLogging`，保留真实 LoggerFactory 的配置过滤，不给 Fake 提供程序强行放开全部级别。
- **改写的用例**：
  - `OperationRecordLogCapture`、`RequestLoggingTests`、`SettingsContractTests`、`PagedQueryContractTests`；
  - `AuthorizationAndAuditingTests`、`ExternalLoginSettingsTests` 里的旧键与旧值；
  - Email、Notification、Resource 投影、部署、签名密钥等测试里为 Serilog 替换 `ILoggerFactory` 的绕行逻辑：复核后去掉已无必要的部分。
  - 断言按 `FakeLogRecord` 的公开契约（结构化值为字符串）。
- **新增用例**：
  - 请求日志字段与精确计数（§1 的开关矩阵、租户四种情形）；
  - 最小级别写入即生效、清除回落、类别覆盖不受影响；
  - 三路注册；
  - 迁移映射在集成测试层的可测部分。
- **生成检查脚本**：`scripts/test-template-generation.py` 里识别 Serilog 配置根与类别检查的逻辑，迁到 `Logging:LogLevel` 形状并更新正反例，保留 `Default` 等特殊键的语义，不能静默失效。
- **模板文档**：
  - `deploy/configuration.md`：可调键表 `RequestLogging:Enabled`；日志配置与 JSON 切换；HTTP 日志边界；遥测一节。
  - `standards/coding-backend.md`：§1 技术栈去掉 Serilog；§7 作用域键与请求日志。
  - `standards/settings.md`：库直接订阅的键示例改为 `Logging:LogLevel`。
  - `standards/tech-stack.md`、`project-structure.md`（Hosting 职责）。
  - 改后跑读取成本闸门，不上调预算。

### 2.7 遥测导出设置（系统设置）

- **设置定义**（宿主级，归入运维分组；若新建遥测分组则承接现有宿主子权限、前端常量、面板说明与 Mock 的规范，不另造机制）：
  - `Telemetry.Export.Enabled`：布尔，`AsBoolean()`。
  - `Telemetry.Export.Endpoint`：文本，绝对 http/https 基地址，值域在现有设置校验入口校验一次。
  - `Telemetry.Export.Headers`：机密设置（`IsEncrypted`），只写不回显，格式同官方 `OTEL_EXPORTER_OTLP_HEADERS`（`key1=value1,key2=value2`），格式在写入入口校验一次。
  - 词条与显示名中英同步；Mock 同步。
- **绑定与兜底**（一个定义源，设置页与运行期一致）：
  - `Telemetry.Export.Endpoint`：`HostSettingBindings` 用 `Bind` 直接绑到 SDK 原本就读的配置键 `OTEL_EXPORTER_OTLP_ENDPOINT`。部署值即设置定义的默认值（设置组件按绑定键查部署基线的现有规则），系统覆盖作为更高优先级的配置源写在同一个键上，清除即回落。
  - `Telemetry.Export.Headers`：`Bind` 到 `OTEL_EXPORTER_OTLP_HEADERS`。机密设置没有下发的默认值，页面不回显部署头，部署兜底只从接收端验证。
  - `Telemetry.Export.Enabled`：`Bind` 到 `Telemetry:Export:Enabled`，兜底值为"部署配置的 `OTEL_EXPORTER_OTLP_ENDPOINT` 非空白"。由 Api 的一个静态函数从 `IConfiguration` 算出，在注册宿主设置时作为 `fallback` 传入；运行期该键缺失时，`TelemetryExportOptions` 也用这同一个函数推导。只配公共端点时默认启用；清除回到该推导值。
  - `TelemetryExportOptions`（`Enabled`、`Endpoint`、`Headers`）用 `AddOptions<TelemetryExportOptions>().Configure<IConfiguration>(...)` 从上述三个键读取，并登记配置变更令牌源，使 `IOptionsMonitor` 随宿主设置配置源的重载更新。不用 `BindOption`：它只绑定 `{节}:{属性}`，接不上 `OTEL_*` 部署基线。
- **发送层**：`Api/Hosting/` 下一个 `DelegatingHandler`，作为三路导出器 `HttpClientFactory` 返回的客户端的处理器。同时覆盖 `Send` 与 `SendAsync`：SDK 在 .NET 10 下走同步 `HttpClient.Send`。每次请求从 `IOptionsMonitor<TelemetryExportOptions>` 读当前有效值：
  - **首次发送边界**：宿主启动完成（`IHostApplicationLifetime.ApplicationStarted`）之前一律按未启用处理。设置组件在宿主启动阶段（`HostSettingStartupService.StartAsync()`）加载数据库值，`ApplicationStarted` 在所有托管服务启动之后才触发，所以首次发送已遵守数据库值；首次加载失败按设置组件现有逻辑回落部署配置。启动前产生的遥测在发送层丢弃，控制台日志不受影响，文档写明。
  - 未启用或地址为空白：不发网络请求，直接返回空的 200 响应；
  - 已启用：把请求地址替换为 `{Endpoint 去掉末尾斜杠}/v1/{logs|metrics|traces}`，信号取自 SDK 原请求路径末段；
  - 认证头：处理器独占写入，按当前有效 `Headers` 解析后完整替换同名头，SDK 侧已清空，因此每个头只有一份；保留 SDK 必需的协议头。
  - 处理器只解析与应用，不重复校验格式，不加锁或缓存；设置提交之后发起的新导出即用新值，其他副本按宿主设置既有周期刷新。
- **前端：开关下缩进子项**（通用命名约定）：
  - 名为 `{前缀}.Enabled` 的布尔设置，与同前缀的其他设置组成一组，开关的**有效值**为真时子项缩进一级显示，否则不显示。有效值含部署继承值与清除回落，`"false"` 字符串按假处理。
  - 处理的是专属布局分流之后的 `GroupView.rows`：外部登录已按提供方分节，不再参与，诊断信息不受影响。
  - 现有设置受影响的是注册分组：`Registration.Enabled` 下的验证码、邮箱验证等改为缩进显示，注册关闭时隐藏（用户 2026-11-10 确认）。
  - 不另建组件体系，不需要后端 UI 元数据。三项都即时生效，不加"重启后生效"提示，沿用现有宿主设置的保存与生效文案。
- **不做**：不提供协议、服务名、采样率等设置项；不做导出状态只读面板；不做 OpAMP；不支持各信号完全不同的地址（经 Collector）。
- **维护待办**（只记在本仓规格，不进模板载荷）：OpenTelemetry .NET 发布正式的选项重载能力后，按届时实际发布的 API 评估能否用 SDK 自身的重载替换发送层改写地址与认证头；增删导出器（开关）不在 #7842 范围内，届时单独判断。

## 3. 计划

分工：Claude 实施代码、测试与端到端验收（按质量与 token 额度拆给子 agent）；Codex 按规范与原则做代码审查。

实施要求（Codex 第 6 版复审承接项）：
- `Enabled` 的兜底只看部署基线：设置绑定的 fallback 与 Options 缺键时共用同一份部署值，不得在配置重载后用已含系统地址覆盖的有效 `IConfiguration` 重算。
- 首次发送门槛按发送时刻判断：`ApplicationStarted` 前的发送就地丢弃，不按事件生成时间筛；正常停机时照常冲刷已启用的管道。

规范：
- 模板：前后端开发规范、目录与分层规范、文件命名规范（`template/docs/standards/coding-common.md`、`coding-backend.md` §1/§2/§4/§5/§7、`coding-frontend.md`、`project-structure.md`、`testing.md`、`settings.md`），模板维护 `docs/template/development-guide.md`、`quality-assurance.md`。
- 框架：`docs/framework/development-guide.md`（§4 文档注释、§7 测试）与 `versioning.md`。

步骤：
1. 按 §2 实施。
2. 定向验证：
   - 选定代表场景（含 Identity 本地化开、Resource 本地化关、未启用操作记录库的形态）做后端构建、单测、真实 PostgreSQL 集成测试、前端设置页单测与 lint；
   - 其余条件形态用选择器 `-GenerateOnly` 检查，完整矩阵交 CI；
   - 框架受影响组件单测与候选包消费。
3. 端到端验收（真实 Kestrel、PostgreSQL、Collector），在 `.tmp/logging-otel/` 从 #258 夹具派生，保留原证据：
   - 同一业务请求经真实关联、租户管道：比较业务日志与 Server span 的 TraceId/SpanId，核对两个作用域属性与请求完成日志的租户值；
   - 真实 stdout 分别验证 Simple 与 JSON：Simple 按模板默认配置启动，验收环境不额外注入 formatter 名称；再仅覆盖 `FormatterName=json` 验另一侧；
   - 运行期改级别、清除回落、类别覆盖；
   - 请求日志开关矩阵与租户四种情形；
   - 有效配置未启用（部署端点 null、空串、空白；以及部署启用但系统关闭）时真实运行无 OTLP 网络流量；
   - 启用时三路各一个导出器、`service.name` 一致；
   - 正常退出后日志批量导出的完成屏障，Collector 端有上限条件等待；
   - 操作记录失败事件的两个键名（JSON 与 OTLP）；
   - 旧库迁移升级、降级、再升级；
   - 设置页开关的界面验证（本地化开/关）；
   - 遥测导出设置：§1 遥测导出验收逐条（含"仅系统设置启用"与"部署启用、系统关闭"两个启动反例、地址热切换、认证头 A→B/去头/清除回落、冲突配置一组），接收端核对三路请求的地址路径与认证头份数；
   - 设置页：遥测开关下子项缩进、注册分组联动、外部登录专属区保留，保存后/刷新后/清除后显示正确（本地化开/关沿用代表场景）；
   - 服务名三种来源。
   - 构建、测试与临时环境按 `remote-project-runner` 调度。
4. 仓库静态检查（`scripts/check-all.ps1` 全部闸门）、模板读取成本与条件块、模板包测试；框架文档 API 漂移检查。
5. 本地提交（不推送），交 Codex 代码审查。审查通过后由 Claude 推送、开 PR、处理 CI 并合并；框架改动随合并发 beta。

## 4. 结果

实施与验证（2026-11-10，Codex 代码审查前）：

- **框架**：MultiTenancy 438、Tracing 44、OperationRecords 257、Settings 209 项单测通过；候选包 `1.2.3-local.20261110042021` 隔离消费通过。
- **模板**：三个代表场景 Release 构建（警告即错误）与单测、真实 PostgreSQL 集成测试全部通过；最后一轮修改后重跑了其中两个场景：

  | 场景 | 单测 | 集成测试 |
  | --- | --- | --- |
  | `identity-capabilities-05` | 263 | 627 |
  | `resource-capabilities-03` | 154 | 159 |
  | `identity-all-features` | 304 | 808 |

  `identity-all-features` 是改 Data Protection 条件之前的结果，之后只重新构建。
- **前端**：本地化开 377 项、关 290 项 spec 通过，lint、构建与 i18n 检查通过。
- **静态检查**：`check-all.ps1` 50 道闸门通过；PR 选择器选出 6 个代表场景，`-GenerateOnly` 通过；模板包测试通过。
- **端到端**：真实 Kestrel、PostgreSQL、Collector，Collector 前加一个记录路径与认证头的抓包代理。§1 逐条通过，另含 §3 的正常退出完成屏障：SIGTERM 后 0.1 秒退出，屏障前没有 `/v1/logs` 请求（反例），退出时冲刷出标记日志并被 Collector 接收。

实施中偏离方案或补充的三处：

- **Data Protection 改为所有形态注册**：机密的 OTLP 认证头在每种形态都存在，纯 API 且不含 Webhook 的 Resource 形态原先不注册持久密钥环，容器重建后密文解不开。
- **发送门槛不依赖宿主生命周期**：导出器配置回调原先解析 `IHostApplicationLifetime`，而导出器在构建日志工厂时就创建，依赖注入重入后生成了第二个 `ApplicationLifetime`，进程收到 SIGTERM 后不退出（develop 基线正常）。改为无依赖的门闩单例，由托管服务在宿主启动时挂到 `ApplicationStarted` 上打开。集成测试替换了生命周期、端到端此前用强制结束，所以都没发现，是退出屏障验收暴露的。
- **服务名的兜底**取 Api 程序集名，与 service-info 一致。

证据见 `.tmp/logging-otel/e2e/evidence/`，不随仓库提交。
