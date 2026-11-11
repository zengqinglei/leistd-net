# 遥测导出关闭时的开销与测试宿主

状态：已完成（Codex 审查两轮） · 风险档：中（遥测注册、设置定义、共享测试工厂） · 基线：develop `c3b3f1d9` · 来源：WP2 对 beta.320 的反馈、D13 演示站实测

## 1. 需求

**反馈要点**

- 模板把三路 OTLP 导出器常驻注册，只在 HTTP 发送层丢弃数据，用来支持在系统设置里随时开关导出。导出关闭时，日志和 span 仍要进入批处理并序列化。
- 业务项目的集成测试会起大量宿主，每个宿主都带着三路导出管道，导致 CI 容器 OOMKilled，耗时敏感的用例超时。
- D13 实测发现：导出被拒（401）时后端没有任何提示。

**核实结果**

- OTel 1.20.1 支持官方开关 `OTEL_SDK_DISABLED`。用环境变量或 IConfiguration 设置都生效，Provider 变成 Noop，导出计数为 0。
- 只在 Provider 构建前读取，之后修改无效。

**验收（Given/When/Then）**

- **部署关闭**：Given 部署配置 `OTEL_SDK_DISABLED=true`；When 宿主启动；Then 不注册 OpenTelemetry 与导出管道，系统设置里没有 `Telemetry.Export.*` 三项，也没有对应的配置绑定。其余日志行为不变。
- **测试宿主**：共享集成测试工厂默认 `OTEL_SDK_DISABLED=true`。遥测自己的用例（导出、敏感路由）显式设为 `false`，行为与现状一致。
- **运行时关闭（追踪）**：Given SDK 启用但导出关闭或地址为空；Then span 由采样器丢弃，不记录也不导出。追踪上下文照常传播，日志里仍有 TraceId。导出打开后，新的 span 照常导出。
- **运行时关闭（日志）**：Given 同上；Then 日志在进入批处理之前被丢弃，不排队、不序列化。导出打开后照常导出。
- **运行时关闭（指标）**：维持在发送层丢弃。指标只做内存聚合，每 60 秒序列化一次，开销有界，文档里说明这一点。
- **导出失败可见**：Given 导出开启、收集器返回非 2xx 或连接失败；Then 记一条 Warning，写明信号、状态码或异常类型，不含认证头。持续失败不重复刷屏，恢复后记一条 Information。

非目标：不改协议与地址规则；不引入新的部署变量（`OTEL_SDK_DISABLED` 是 OpenTelemetry 的标准变量）。

## 2. 方案

- **`TelemetryExportOptions`**：新增 `IsSdkDisabled(IConfiguration)`，按官方语义只认 `true`（不区分大小写）；新增 `IsActive`，表示开关打开且地址不为空。
- **`AddMyProjectTelemetry`**：SDK 关闭时直接返回，并把 Application 的 `TelemetrySettingsOptions.ExportConfigurable` 设为 false。`SettingDefinitionProvider` 据此不登记三项导出设置；不另拆定义提供器，是因为面板顺序取分组首次出现的先后，另一个提供器先登记会把运维面板挪到最前。`HostSettingBindings` 的三条绑定同样只在 SDK 启用时登记。
- **追踪**：采样器 `TelemetryExportSampler`。导出处于活动状态时委托给 `ParentBased(AlwaysOn)`，否则返回 Drop（SDK 对 Drop 的处理是只传播上下文，不记录）。
- **日志**：导出器仍用官方 `AddOtlpExporter`，SDK 注册的日志提供程序换成包着同一实例的 `TelemetryExportLoggerProvider`。导出不活动时 `IsEnabled` 为假、`Log` 不转交，日志到不了 SDK，不排队、不序列化。提供程序别名同为 `OpenTelemetry`，并转发外部作用域。
  - **为什么不自建导出器与处理器**：曾试过，两轮审查各发现一处配置语义丢失。`OTEL_BLRP_*` 批处理参数可以补回来，但宿主配置源里的属性限额补不回来，公开构造函数只读环境变量。
  - **代价**：按 SDK 提供程序完整类型名写的日志过滤规则不再匹配，按别名写的照常生效。
- **失败提示**：由 `TelemetryExportHandler` 在发送后判断，状态翻转时才记日志（失败一次、恢复一次）。日志器延迟解析，避免构建日志工厂时形成环。导出过程中 SDK 会抑制遥测采集，这条日志只进控制台，不会再被导出，不会形成循环。

## 3. 计划

1. 实现上面几项，更新单测：注册、采样器、处理器、失败提示。
2. 集成测试：共享工厂关闭 SDK；遥测用例显式打开；新增"SDK 关闭时没有遥测设置项""导出失败有提示"两例。
3. 更新文档 `docs/deploy/configuration.md`「遥测导出」与 `testing.md`。
4. 在 worker-01 跑两种形态的后端全量测试；送 Codex；提 PR。

## 4. 结果

- **定向**：identity-all-features 后端用 Release 加警告即错误构建通过，遥测单测 40 项通过，Mock setting spec 44 项通过。
- **变异**：以下四处改动都让对应用例失败：
  - 采样器恒采；
  - 提供程序不门控；
  - 改回自建导出器（批处理参数与属性限额两例失败）。
- **全量（worker-01）**：三种形态的后端单测与集成测试全部通过，覆盖 `SettingDefinitionProvider` 的三个构造变体。

  | 形态 | 单测 | 集成测试 |
  | --- | --- | --- |
  | identity-all-features | 322 | 817 |
  | standalone-capabilities-02 | 228 | 428 |
  | resource-capabilities-03 | 171 | 167 |
- **审查**：Codex 两轮各提出一项 P2，都因自建日志导出管道而起：首轮是 `OTEL_BLRP_*` 失效，复审是宿主配置源里的属性限额失效。最终改为官方导出器，门控放在日志提供程序外面，并补了两条回归用例（批处理参数、属性限额）。
- **未做**：
  - 没有用真实 Collector 复测导出失败的提示，失败提示由单测覆盖。
  - 没有直接覆盖"有上游已采样父级"时的传播。采样标志对下游变为未采样，这是采用丢弃方案的取舍。
