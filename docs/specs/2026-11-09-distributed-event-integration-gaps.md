# 分布式事件组件接入缺口（beta.230 反馈）

状态：进行中（本地验收完成，待追加审查、CI 与 beta 验证） · 风险档：高 · 基线：develop `48e9e62b`（组件自 `v2.0.0-beta.230` 起未改）· 前序：`2026-11-06-distributed-events.md`

## 1. 需求

下游把自建消息机制一次性迁到组件，并基于 `IDistributedEventTransport` 编写自定义传输。按推荐部署（Api 只写发件箱，Worker 注册传输并负责收发）接入时，报告了九处缺口。逐项对照源码核对后，结论如下：

| # | 缺口（已核对源码） | 结论 | 落位 |
| --- | --- | --- | --- |
| G8 | `/status` 的传输状态只在当前进程执行带 `event-bus` 标签的健康检查（`DistributedEventCatalog.StatusAsync`）；端点在 Api、传输在 Worker 时恒为 `unknown` | 采纳 | 框架 |
| G6 | 三个 `/payload` 路由随 `MapDistributedEvents` 固定映射；宿主要求"正文不出接口"时无法关闭 | 采纳 | 框架 |
| G1 | `PublishAsync` 只调用 `Add`：同一事务内重复发布同一 `EventId`，要到提交时才撞唯一索引 `(TenantId, EventId)`，整笔业务事务失败，而且报错不指向发布点 | 采纳：同一工作单元内幂等，跨事务由调用方保证 | 框架、模板规范 |
| G4 | `DistributedEventSendResult` 没有坐标，发件箱存不下中枢返回的消息标识、分区或位点 | 本轮不采纳（下游 2026-11-09 撤回该诉求） | — |
| G9 | 4101、4103 日志缺 `EventName`；4105 缺 `Transport`、`Reason` 和坐标 | 采纳 | 框架 |
| G7 | 链路只存在 `Headers.traceparent` 里，关键字只匹配 `EventId`/`OrderingKey`，无法按 traceId 检索 | 采纳 | 框架、模板界面 |
| G3 | 传输抛异常时，`LastError` 直接取 `exception.Message`（发件箱处理器） | 采纳：组件只存异常类型名，并写明传输契约 | 框架 |
| G5 | 管理查询没有过滤扩展点 | 不采纳 | — |
| G2 | 只保证同租户、同事件名、同键的顺序 | 行为不改，补文档 | 框架文档、模板规范 |
| G10 | 发件处理器不从持久化的 `Headers` 恢复 W3C 父上下文，也没有 `event.send` span；发布、发送、消费三段的 TraceId 接不上 | 采纳 | 框架 |
| G11 | 关闭三项执行开关、不注册传输的仅发布宿主启动失败：发件处理器、接收器在构造时强制依赖 `IDistributedEventTransport`，且无论开关如何都会注册，`ValidateOnBuild` 不通过；重新解码依赖 `transport.DecodeAsync`，这类宿主无法执行 | 采纳 | 框架、模板 |
| G12 | 恢复与丢弃的审计事件 `DistributedEventManagedEvent` 在目标租户作用域内登记，处理器运行在目标租户上下文；事件不带目标租户，宿主跨库操作无法归属到调用方 | 采纳 | 框架 |
| G13 | 收件处理器失败时 `LastError` 直接存 `failure.Message`，与 G3 的出站约定不一致 | 采纳：与 G3 统一 | 框架 |

**取舍：**

- **G8**：每个注册了传输的进程定期把本进程传输的健康结果写入宿主库；管理端点只读这张表，不再在请求里执行健康检查。不用官方的 `IHealthCheckPublisher`：它的 `HealthCheckPublisherOptions.Predicate` 对进程内所有发布器生效，为本组件设置标签过滤会改变宿主其他发布器的行为。状态沿用 `HealthStatus` 的取值，不另造词汇，对应关系写进文档：Healthy 表示已连接；Degraded 表示降级；Unhealthy 表示断开，此时客户端正在自动重连；Unknown 表示没有新鲜上报。"重连中"在现有三种传输里都等于"断开且正在自动恢复"，单列一个状态没有额外的判别信息。
- **G6**：用一个布尔开关决定是否映射载荷路由，不增加载荷专用的授权策略。Manage 策略已经回答了"谁能看"，这里缺的只是"开不开放"。`/status` 同时返回开关值，界面据此隐藏入口。框架与模板默认都开启，保持现有行为，需要"正文不出接口"的宿主显式关闭。
- **G1**：确定性 `EventId` 代表同一个事实。同一工作单元内再次发布 `EventId`、事件名和冻结载荷都相同的事件，视为幂等，直接跳过；`EventId` 相同但内容不同属于调用方缺陷，在发布点立即抛出。跨事务的重复不在发布时查库，仍由唯一约束拒绝；规范写明调用方只在产生新事实（新版本）时发布。理由：查库去重多一次往返且有竞态，跨事务去重本来就由消费端收件箱负责。
- **G3**：组件只截获传输自身没有处理的异常，持久化时只记录异常类型全名，完整异常写进日志。传输返回的 `Error` 由传输负责脱敏，这条契约写进自定义传输指南。
- **G5 不采纳**：下游的范围规则与组件内置规则一致，目前没有调用方需要扩展点。按"新抽象要有真实调用点"的原则，出现第二种过滤需求时再立项。
- **G10**：发件处理器按收件处理器的做法，从行的 `traceparent`/`tracestate` 恢复父上下文，围绕 `SendAsync` 开启 `event.send`（`ActivityKind.Producer`），并按 `leistd-correlation-id` 恢复关联标识作用域。提交后立即发送与扫描重试走同一个 `ProcessAsync`，两条路径都覆盖。消息头不改写，消费端的父级仍是 `event.publish`；三段 span 同属一个 TraceId。
- **G11**：传输是可选依赖，模式由三项显式开关决定（校验器须保证三项开关任一打开就必须注册传输）。不注册传输时，宿主必须能通过 `ValidateOnBuild` 和 `ValidateScopes` 启动，并且能发布、查询、重投、重放、丢弃。重新解码需要传输的解码能力，所以在没有传输的宿主上改为**转交**：把拒收行标为"待重新解码"后立即返回，由开启接收的宿主（Worker）用集群周期任务执行，结果仍写回拒收行（成功转入 Inbox，失败回到待处理并记录原因）。本地有传输时保持同步执行。理由：仅发布宿主是推荐部署，要求它注册传输，等于把 SDK 连接和健康上报带进 Api；而把解码契约从传输里拆出来，会改动三种传输的公共接口，收益只是省掉这一次转交。
- **G12**：审计事件在每批提交之后、回到调用方上下文再发布，处理器因此在调用方的租户与用户下运行。事件新增目标租户字段（随按租户分组的批次填写）。目标批次退出后，在调用方租户与用户上下文中开启独立 `requiresNew` 工作单元，登记并完成该批审计，不复用调用方已有单元。每个实际提交且有改动的租户分组批次一条；即使外层之后回滚，已完成的审计也保留。跨库数据更新与本地审计不具备分布式原子性；审计失败向上传播，但不撤回已提交批次。
- **G13**：收件处理器失败时，`LastError` 只记异常类型全名，每次未耗尽的失败以 4106 Warning 保留完整异常，耗尽由 4103 Error 保留，与 G3 一致。解码异常的说明由传输提供，按 G3 的契约由传输负责脱敏，保持现状。
- **G2**：跨事件名不保序，这是有意的边界。多个事件共享版本序列、并且较新事件的状态能完整覆盖较旧事件时，消费方可以按租户与聚合维护共同的版本水位，忽略旧版本。增量事实不能只凭共同水位丢弃，必要的顺序或补偿由业务保证。

### 验收（Given/When/Then）

| # | 验收 | 证据 |
| --- | --- | --- |
| A1 | **Given** 进程 W 注册了传输与 `event-bus` 健康检查，进程 A 只映射端点、没有注册传输，两者共用宿主库；**When** W 的检查从 Healthy 变为 Unhealthy 后，A 调用 `/status`；**Then** A 返回 W 的传输名、`unhealthy`、状态变化时刻与最后上报时刻 | EF 集成测试（两个宿主共用一个库） |
| A2 | **Given** 唯一的上报实例停止上报，超过过期时限；**When** 调用 `/status`；**Then** 返回 `unknown`，变化时刻为空 | 单测（推进时钟） |
| A3 | **Given** 两个实例都在新鲜上报，一个 Healthy、一个 Degraded；**Then** 聚合结果为 `degraded`（取最差），变化时刻取最差状态实例的变化时刻 | 单测 |
| A4 | **Given** 关闭载荷开关；**When** 请求三个 `/payload` 路由；**Then** 返回 404（路由未映射），`/status` 的载荷开放标记为 false；默认开启时行为不变 | 端点测试 |
| A5 | **Given** 同一工作单元内两次发布 `EventId` 与内容都相同的事件，包括中途调用过 `SaveChanges`、参与同一事务的嵌套单元；**Then** 提交成功，发件箱只有一行，提交后只发送一次。内容不同时，第二次 `PublishAsync` 抛 `InvalidOperationException`，消息含 EventId 与事件名；异常传播时业务写入不提交。独立新工作单元重复仍由唯一约束拒绝，即使该单元已跟踪上一事务的发件箱行 | EF 集成测试 |
| A7 | 4101、4103 的结构化参数含 `EventName`；4105 含 `Transport`、`Reason`、`EventName`、`EventId` 与坐标摘要 | 日志捕获单测 |
| A8 | **Given** 发件箱、收件箱、拒收记录各带 traceparent；**When** 以 32 位十六进制 traceId 作关键字查询；**Then** 三类列表都能命中；traceparent 非法或缺失时 TraceId 为空；关联标识与 TraceId 不同时，用关联标识作关键字也能命中三类列表 | EF 集成测试 |
| A9 | **Given** 传输的 `SendAsync` 抛出消息中带敏感文本的异常；**Then** `LastError` 等于异常类型全名，不含异常消息 | 单测 |
| A10 | 模板以新实体重建 `AddDistributedEvents` 迁移；全功能场景的构建、后端测试、前端测试与 E2E 通过 | 模板生成矩阵 |
| A11 | **Given** 发布处带 Activity；**When** 立即发送和扫描重试各发送一次，再由消费端处理；**Then** `event.publish`、`event.send`、`event.process` 三段 TraceId 相同，`event.send` 的父级是 `event.publish`，发送时关联标识等于发布时的值 | 单测（ActivityListener） |
| A12 | **Given** 宿主不注册传输、三项执行开关关闭，开启 `ValidateOnBuild` 与 `ValidateScopes`；**Then** 能构建并启动，能发布、查询、重投、重放、丢弃。在该宿主上重新解码，行进入"待重新解码"；另一个注册了传输、开启接收的宿主执行周期任务后，行转为已重新解码并在 Inbox 出现；解码失败时回到待处理并记录原因。打开任一执行开关但不注册传输时，启动报错并指出配置键 | EF 集成测试（两个宿主共用一个库） |
| A13 | **Given** 宿主用户跨库恢复某租户库的记录；**Then** 审计处理器观察到的当前租户是宿主（调用方），事件带目标租户与库键；每个已提交批次一条；调用方有活动工作单元时，外层提交或回滚都不改变已分发审计的上下文与数量；事件带目标租户字段 | EF 集成测试 |
| A14 | **Given** 处理器抛出带敏感文本的异常；**Then** 收件箱的 `LastError` 等于异常类型全名，未耗尽的每次失败有带完整异常的 4106 Warning，耗尽时 4103 Error 含完整异常 | 单测 |

## 2. 设计

### 2.1 传输状态跨进程（G8）

- **新实体 `DistributedEventTransportState`**：放在宿主库，与拒收表一致，因为传输属于进程，不属于租户。字段如下：
  - `Instance`：主键，默认 `{ApplicationName}@{MachineName}:{ProcessId}`，可通过选项覆盖；显式值须由宿主保证每个并发实例唯一；
  - `TransportName`；
  - `Status`：`HealthStatus` 的名称；
  - `StatusChangedAt`：只在状态变化时更新；
  - `ReportedAt`：每次上报都更新。
- **上报**：EF 包注册一个后台服务。本进程注册了传输并且存在 `event-bus` 标签的健康检查时，按上报间隔执行检查，并 upsert 本实例这一行；否则不做任何事。上报失败只记警告日志，下一轮重试，不影响收发。
- **选项** `DistributedEventOptions.TransportStatus`：`ReportInterval` 默认 15 秒，`StaleAfter` 默认 1 分钟，`Instance` 默认为空（空值按上面的规则生成）。默认值在 `AddXxx` 里登记；校验器保证 `StaleAfter > ReportInterval`。这些调优项不进部署变量。
- **快照**：`/status` 从宿主库读取 `ReportedAt >= now - StaleAfter` 的行，取最差状态；当前租户与所选业务库不改变传输状态的来源。最差状态并列时，变化时刻取这些行中最晚的 `StatusChangedAt`；最近上报时刻取所有新鲜实例的最大 `ReportedAt`。`TransportName` 优先取本进程注册的传输名，否则按最差状态、变化时刻降序、上报时刻降序、`Instance` 升序选取。没有新鲜行时为 `unknown`，两个时间均为空。删除原来在请求里执行健康检查的代码。
- **清理**：保留期任务删除 `ReportedAt` 早于保留期的实例行。

### 2.2 载荷端点开关（G6）

端点选项新增载荷开关，默认开启；关闭时不映射三个 `/payload` 路由。`/status` 的载荷开放标记由端点处理器按端点选项填写；直接调用管理服务时该字段为空。Distributed 包不感知端点。

### 2.3 同一工作单元内发布幂等（G1）

`PublishAsync` 冻结载荷之后，在当前工作单元的 `ServiceProvider` 中取得内部 scoped 发布登记，按 `(TenantId, EventId)` 用字典查找本工作单元已发布的冻结事实：

- 找到，且 `EventName` 与 `Payload` 都相同：直接返回，不再登记发送；
- 找到，但内容不同：抛 `InvalidOperationException`，消息含 EventId 与事件名；
- 没找到：按原逻辑 `Add`，成功后登记冻结的事件名与载荷，再登记提交后发送。

登记与实际工作单元作用域同寿命，参与父事务的子单元复用它；中途 `SaveChanges` 不清空，独立新单元获得新登记。不将从数据库加载的旧行当作本单元已发布，不查询数据库去重，不增加公共接口或跨事务缓存。代价是每个工作单元多一份随作用域释放的字典。

### 2.4 发送坐标（G4，本轮不做）

下游撤回了这项诉求，目前没有调用方需要发送坐标。它要新增公共契约成员和一列迁移，按"新扩展点要有真实调用点"的原则本轮不做；出现需要按中枢消息标识排查出站的调用方时再立项，届时按收件箱、拒收现有的坐标字典设计。

### 2.5 日志字段（G9）

- 4101、4103 增加 `{EventName}`；
- 4104、4105 统一带 `{Transport}`、`{Reason}`、`{EventName}`、`{EventId}`、`{Coordinates}`，坐标格式化为 `k=v;k=v` 并截断到 256 个字符；
- 事件编号与名称不变；组件文档的"管理与排障"一节补一张日志事件表，作为稳定的告警键。

### 2.6 按 traceId 检索（G7）

- 发件箱、收件箱、拒收各增加 `TraceId` 列（长度 32，可空，建索引）。写入时从 `traceparent` 头按 W3C 格式解析，非法或缺失时留空，统一存小写。
- 同时增加 `CorrelationId` 列（长度取 `CorrelationIdConstants.MaxLength`，可空，建索引），取自 `leistd-correlation-id` 头，只有符合 `CorrelationIdConstants.IsWellFormed` 时才写入该列，否则留空；原始消息头不改。发布时这个头来自关联标识组件的 `ICorrelationIdProvider`，消费时组件也按它恢复关联标识作用域，所以存的就是日志里的关联标识。关联标识默认等于 TraceId，但在以下两种情况下两者不同：调用方通过请求头指定了关联标识，或代码显式切换过；没有 `Activity` 的入口（后台作业、Worker）只有关联标识，没有 traceparent。只存 TraceId 时，从日志拿到关联标识就查不到事件。
- 关键字精确匹配 `TraceId` 或 `CorrelationId`（是 32 位十六进制时先转成小写再匹配 `TraceId`），不做模糊匹配。
- 模板界面在列表关键字的占位文案里补上 traceId。

### 2.7 传输异常（G3）

发件箱处理器截获传输异常时，`LastError` 记异常类型全名，`LastErrorCode` 仍为 `transport-exception`，警告日志保留完整异常。自定义传输指南写明：`Error` 不得包含凭据、连接串或载荷内容；SDK 异常应转成稳定错误码，并附固定格式的说明。回环传输按同一契约改造。

### 2.8 文档

- `framework/docs/components/event-bus.md`：跨进程状态、选项与 `HealthStatus` 语义对应（G8）；载荷开关（G6）；发布幂等与跨事务约定（G1）；日志事件表（G9）；traceId 检索（G7）；传输的脱敏契约（G3）；顺序边界与共同版本水位的适用条件（G2）。
- `template/docs/standards/` 中的分布式事件规范：写明 G1 的确定性 `EventId` 约定和 G2 的水位做法，各一两句，其余指向组件文档。

### 2.9 追加项 G10–G13

规则见 §1 的取舍。补充约束：
- G11 的转交不新增公共选项；周期任务沿用 `Inbox:ReceiveEnabled` 开关、`DispatchInterval` 周期，每批 100 行，仅读宿主拒收表中与当前传输 `Name` 精确一致的待重新解码行，不扫描租户库。无传输或接收关闭时不执行。本地传输名称不同也转交给匹配的 Worker，不用错误解码器尝试。
- 新状态 `RedecodePending` 追加到拒收枚举，不改已有数值；这只是已有整数列的新值，EF 模型无差异时无需重建迁移。待重新解码是非终态，保留期任务不得清理；成功转入 Inbox 后标为 Redecoded，解码／接收被拒绝则回 Pending 并记录脱敏原因；逐行捕获非取消的执行故障，4107 Warning 留下完整异常，该行保留待办，继续处理后续行；整轮结束后汇总失败让任务失败，供下一轮重试。Pending 与 RedecodePending 都允许丢弃；按筛选丢弃可明确限定状态，避免扩及未显示的另一类未解决记录。重复执行沿用 Inbox 唯一约束去重，不重投 Broker，RabbitMQ／Kafka／Loopback 都使用原字节／头／入站坐标。
- 恢复输出增加 `DeferredCount`（默认为 0，属于 AffectedCount 的子集），模板显示"待重新解码"，按该计数区分已转交和同步完成。转交请求按 `RedecodeRequested` 管理动作审计，真正完成不冒充请求用户再次发管理审计；Worker 只执行已登记的请求。状态计数将待重新解码计入未解决拒收。
- `DistributedEventManagementAction.RedecodeRequested` 与 `DistributedEventRejectionStatus.RedecodePending` 是新增枚举成员，宿主穷举分支须处理；共库多种传输的 Worker 分别只领取同名行，集群任务按时段轮转执行，可能延迟。
- G11 需要改迁移时，与本 PR 已重建的 `AddDistributedEvents` 一起原位重建。
- 组件文档补"仅发布宿主"的接线示例：`AddDistributedEventBusEfCore<TDbContext>()`，三项开关关闭，不注册传输，照常 `MapDistributedEvents`；Worker 注册传输并开启三项开关。另写明重新解码的转交语义。
- 模板：Api 与 Worker 的分工按上面的示例落地。如果模板现有组合已经是单进程收发，只补文档，不改拓扑。

## 3. 计划

| 步 | 内容 | 验证 |
| --- | --- | --- |
| 1 | 框架：G1、G6、G8、G7（前置项，先做） | event-bus 单测、EF 集成测试、端点测试 |
| 2 | 框架：G9、G3 | 同上 |
| 3 | 框架文档与 XML 注释；打包到本地包源，核对包内文档 | 打包检查 |
| 4 | 模板：用本地包原位重建两套 `AddDistributedEvents` 迁移（该迁移还没进入任何正式模板版本，`main` 的 `v1.2.3` 不含此组件）；前端适配状态变化时刻、按载荷开放标记隐藏入口、关键字占位文案；Mock 同步；模板规范补两句 | 受影响的模板生成场景 |
| 5 | 一个 PR 送审（结论须写明"通过"），CI 全绿后合入 develop，发出框架 beta | CI、beta 包 |
| 6 | 追加 G10–G13（同一 PR #249，同一个 beta）：先写修复前会失败的测试，再实施；迁移有变化时原位重建；增量送审 | 目标测试、受影响的模板场景、CI |

所有项放在同一个 PR：G8、G7 都要改实体和迁移，分开提交只会让模板迁移重建多次。

## 4. 结果

交付：[PR #249](https://github.com/zengqinglei/leistd-net/pull/249)。框架、模板、测试及常驻文档同批交付。

- 框架已实现 G1、G6、G7、G8、G9、G3，G2 补齐共同版本水位的适用条件。按用户调整，G7 前置并同时存储 TraceId 与独立 CorrelationId；三处关联标识索引列按 IsWellFormed 校验，原始消息头保留。G4 按撤回范围移出。追加 G10–G13 已实现：立即发送／扫描重试恢复发布父上下文与关联标识；无传输且三项职责关闭的宿主可严格构建、启动和管理；拒收可转交同名 Worker 重新解码；恢复审计独立归属调用方，带 TargetTenantId。收件失败只持久异常类型，4106／4103 完整留痕；待办逐行隔离故障、可丢弃且筛选不越界。
- Identity／Resource 两套 AddDistributedEvents 通过 EF 10.0.14 从前一模型重新生成，原位更新迁移与条件快照；界面显示两种状态时间、按载荷开放标记隐藏正文入口，Mock、精确 E2E 定位文案、载荷关闭指引同步。
- L1：选择器要求的 32 个框架项目共 2,790 例通过、0 失败，35 例因本机没有 Broker／Redis 而跳过；81 个框架包重新打包，四个受影响包完成隔离消费；12 个选中模板场景的生成、结构与文档检查通过。追加候选 1.2.3-local.20261108201923 的 Identity／Resource 迁移快照 2／1 通过，两套 IntegrationTests 项目编译通过；追加只扩展整数状态值与管理 DTO，EF 模型没有新差异。
- 定向验收：G1／G3 先验证修复前失败；审查 R1 的可编译生产基线 8 例中 4 例预期失败，修复后 24 例通过。Identity／Resource 无本地化前端组件目标 36／35、Mock 目标 14／13 通过，两形态生产 build、lint 通过；中英文四宽度有头浏览器核对无页面横向溢出。R2 的文档与文案增量通过引用和一致性检查，复用运行代码的 L1 证据。

追加定向验收：G11／G13 的严格构建与敏感异常回归在修复前 3 例全失败，追加框架 25／25 通过；R4 修复前 5 例中 4 例失败，修复后受影响目标 33／33 通过。模板页面／详情／Mock 目标 30／29、两形态生产 build 与 lint 通过；R5 在 Identity／Resource 实际生成物各复现一条种子缺原始消息的失败，修复后 Mock／页面目标 26／25 与 lint 通过。中英文四宽度、待办详情入口及按状态筛选丢弃的浏览器流程通过。真实模板 HTTP／PostgreSQL 与 Broker 用例由最终候选 CI 执行，不把本地编译当作已执行。

已接受边界：跨库数据提交与调用方本地审计不是分布式原子事务；Worker 已写入 Inbox 后与丢弃交错时，消息仍可能处理而拒收显示已丢弃，不新增跨库取消协议。新增 RedecodeRequested／RedecodePending 枚举成员需要宿主穷举分支覆盖；同库多传输的集群时段领取可能延迟。

CI 同时暴露了既有 Webhook 身份测试过早读取提交前状态的竞态；用受控提交顺序在 develop 原测试稳定复现，等待持久状态后在相同顺序与未插桩目标类均通过，仅调整该测试的 I/O 等待。

公共契约已上收到 `framework/docs/components/event-bus.md`；模板发布幂等、顺序水位、载荷关闭入口已上收到 `template/docs/standards/distributed-events.md`，索引同步 `template/docs/README.md`。完整模板阶段与真实 PostgreSQL／Broker／Redis 验证以同候选 CI 为准；合入 develop 后以 Release 全量和实际 beta 核验发布结果。
