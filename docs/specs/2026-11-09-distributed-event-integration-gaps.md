# 分布式事件组件接入缺口（beta.230 反馈）

状态：进行中 · 风险档：高 · 基线：develop `48e9e62b`（组件自 `v2.0.0-beta.230` 起未改）· 前序：`2026-11-06-distributed-events.md`

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

**取舍：**

- **G8**：每个注册了传输的进程定期把本进程传输的健康结果写入宿主库；管理端点只读这张表，不再在请求里执行健康检查。不用官方的 `IHealthCheckPublisher`：它的 `HealthCheckPublisherOptions.Predicate` 对进程内所有发布器生效，为本组件设置标签过滤会改变宿主其他发布器的行为。状态沿用 `HealthStatus` 的取值，不另造词汇，对应关系写进文档：Healthy 表示已连接；Degraded 表示降级；Unhealthy 表示断开，此时客户端正在自动重连；Unknown 表示没有新鲜上报。"重连中"在现有三种传输里都等于"断开且正在自动恢复"，单列一个状态没有额外的判别信息。
- **G6**：用一个布尔开关决定是否映射载荷路由，不增加载荷专用的授权策略。Manage 策略已经回答了"谁能看"，这里缺的只是"开不开放"。`/status` 同时返回开关值，界面据此隐藏入口。框架与模板默认都开启，保持现有行为，需要"正文不出接口"的宿主显式关闭。
- **G1**：确定性 `EventId` 代表同一个事实。同一工作单元内再次发布 `EventId`、事件名和冻结载荷都相同的事件，视为幂等，直接跳过；`EventId` 相同但内容不同属于调用方缺陷，在发布点立即抛出。跨事务的重复不在发布时查库，仍由唯一约束拒绝；规范写明调用方只在产生新事实（新版本）时发布。理由：查库去重多一次往返且有竞态，跨事务去重本来就由消费端收件箱负责。
- **G3**：组件只截获传输自身没有处理的异常，持久化时只记录异常类型全名，完整异常写进日志。传输返回的 `Error` 由传输负责脱敏，这条契约写进自定义传输指南。
- **G5 不采纳**：下游的范围规则与组件内置规则一致，目前没有调用方需要扩展点。按"新抽象要有真实调用点"的原则，出现第二种过滤需求时再立项。
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

## 3. 计划

| 步 | 内容 | 验证 |
| --- | --- | --- |
| 1 | 框架：G1、G6、G8、G7（前置项，先做） | event-bus 单测、EF 集成测试、端点测试 |
| 2 | 框架：G9、G3 | 同上 |
| 3 | 框架文档与 XML 注释；打包到本地包源，核对包内文档 | 打包检查 |
| 4 | 模板：用本地包原位重建两套 `AddDistributedEvents` 迁移（该迁移还没进入任何正式模板版本，`main` 的 `v1.2.3` 不含此组件）；前端适配状态变化时刻、按载荷开放标记隐藏入口、关键字占位文案；Mock 同步；模板规范补两句 | 受影响的模板生成场景 |
| 5 | 一个 PR 送审（结论须写明"通过"），CI 全绿后合入 develop，发出框架 beta | CI、beta 包 |

所有项放在同一个 PR：G8、G7 都要改实体和迁移，分开提交只会让模板迁移重建多次。

## 4. 结果

（实施后填写）
