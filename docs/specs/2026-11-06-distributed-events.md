# 分布式事件组件

状态：进行中（W0 方案评审通过） · 风险档：高（新增公共 API 与包、数据模型与迁移、权限、新增模板参数） · 关联：—

设计稿（链路图、端到端接入、私有中枢接入、三版 UI 方案与对比）：[design.html](./2026-11-06-distributed-events/design.html)。

## 1. 需求

### 问题与背景

- 框架只有进程内事件总线。`framework/docs/components/event-bus.md` 写明跨进程、跨服务投递"当前分组未提供，需另选分布式总线"。
- 业务项目只能自建。已有一个下游项目自建了发件箱、收件箱、按 key 保序、逐库扫描和运维查询，不含测试约 1,700 行。它的模式与 Webhook 组件基本一致，但有三处缺陷：
  - 保序只在单轮扫描内成立：队首失败退避后，同 key 的后续记录会在下一轮先发出。
  - 入站在收到时同步执行处理器，失败不自动重试。
  - 收件箱不存载荷，重放只能依赖发布方的发件箱。
- 该下游要对接公司私有的事件中枢，由业务自己实现传输。中枢有以下特点：
  - 基于 Kafka，外壳为 Avro；
  - 信封里没有租户和事件类型，事件 ID 由 SDK 生成；
  - 外壳解码失败时，SDK 照常提交位点；
  - 鉴权失败后进入降级，不再重连。

  只有这些特点在契约里都有着落，组件才算真正"与传输无关"。
- 2026-11-04 评估认同纳入框架。2026-11-06 用户决定现在立项，基于 develop，依据是"框架能力先立项设计"：业界通用能力先立项，再按业务反馈改进。
- 2026-11-07 用户要求：私有中枢接入暴露的七个缺口一并按最佳实践的终局形态补齐，不留中间态。

### 业界做法（调研结论）

| 方面 | ASP.NET Boilerplate 的后继框架（开源版，源码 2026-11-06） | CAP | MassTransit | 下游自建 |
| --- | --- | --- | --- | --- |
| 发布 | 完成工作单元前写发件箱，与业务同事务 | 同事务写表，提交后立即发送 | 同事务写表，提交后通知投递服务 | 同事务写发件箱 |
| 及时性 | 只轮询，周期 2 秒 | 立即发送，另有约 60 秒的重试扫描兜底 | 通知 + 轮询 | 只轮询，周期 2 秒 |
| 失败 | 无重试计数、无退避、无死信；毒消息会卡住整个发件箱 | 重试 50 次后标 Failed | 重试后进 `_error` 队列，可整体移回 | 退避 8 次（约 4 分钟）后进死信 |
| 保序 | 尽力而为 | 不保证 | 不保证 | 单轮内按 key，跨轮会乱序 |
| 收件箱幂等 | 先查后插，非唯一索引，去重窗口 2 小时 | 主要靠业务自己幂等 | 按 (消息, 消费者) 去重 | 按 (EventId, Consumer) 建唯一索引 |
| 处理与标记 | 同事务 | — | 同事务 | 收到即同步处理，失败不重试 |
| 坏消息 | 无 | 无 | 反序列化失败进 `_skipped` / `_error` 队列 | 无 |
| 外部格式 | provider 各自反序列化 | — | raw JSON 反序列化器，专门用来接非本框架的发布方 | — |
| 按进程分工 | 每个发件箱、收件箱有 `IsSendingEnabled` / `IsProcessingEnabled` | — | 按端点配置 | 写入方与 Worker 分别注册 |
| 租户 | 最新版用 `X-Tenant-Id` 消息头，不支持分库扫描 | 不支持分库 | 不支持分库 | 消息头传租户，逐库扫描 |
| 管理界面 | 无 | Dashboard，支持批量重新入队 | 无（靠 broker 自带工具） | 自建事件页 |
| 许可 | 开源 | MIT | v9 起商业许可 | — |

主链路照各家共识做：同事务写发件箱 → 提交后发送 → 失败重试 → 收件箱去重 → 处理与标记同事务。

下面几项现有方案都缺，由框架补上：
- 按 key 跨轮保序；
- 永久失败直接进死信；
- 拒收存储与重新解码；
- 每租户一个物理库的逐库扫描，以及宿主按库查看；
- 两级权限的管理端点与按条件批量恢复。

调研中见到的缺陷都要避开：
- 处理失败也提交位点；
- 无限重新入队；
- 2 小时去重窗口；
- 未使用发件箱时，在提交前就把消息发出。

### 目标

- 业务在工作单元内发布分布式事件，事件随业务数据一起提交，回滚时一起回滚。
- 正常情况下提交后立即发送，失败后持久重试；进程崩溃后，由集群周期任务逐个物理库扫描兜底。
- 同租户、同一事件名、同一排序键的事件按发件箱提交后可见的 Id 顺序发送；消费侧按传输接收后持久化的 Id 顺序处理。永久失败直接进死信；死信和处理失败都算终态，不阻塞后续事件。
- 消费侧按 `(eventId, consumer)` 去重，处理器的写入与"已处理"标记同事务提交，对数据库的副作用只生效一次。
- 传输可替换。首批附带进程内回环、RabbitMQ 与 Kafka 三种传输：前两者代表"逐条确认"模型，Kafka 代表"提交位点"模型，两种主流确认模型都由真实实现检验契约。业务能用同一契约接入私有中枢：
  - 外部格式由传输映射；
  - 传输层已判定的坏消息可以直接拒收；
  - 整条通道故障与单条消息故障分开处理。
- 一个服务的多个进程可以分工：写入方只写发件箱，Worker 负责发送、接收与处理。
- 管理员可以：
  - 查看状态快照、事件目录、发件箱、收件箱和拒收消息；
  - 宿主可以选择物理库，查看库内全部租户；
  - 单条或按条件批量重投、重放、重新解码，也可以丢弃拒收消息；
  - 发测试事件；传输不支持时可以关掉。
- 模板用条件参数 `IncludeDistributedEvents` 整体裁剪这项能力，包括后台页面。

### 非目标

| 不做 | 原因 |
| --- | --- |
| 模板按参数切换传输 | 模板默认用 RabbitMQ（单容器、开发成本低）；改用 Kafka 只需换一行注册加一个 compose 服务，写进生成项目规范，不为此新增枚举参数让有效形态再翻倍 |
| 恰好一次投递、跨服务全局有序 | 业界都不保证。框架只承诺保留期去重窗口内"对数据库的副作用只生效一次"和"单键有序" |
| 不经发件箱直接发送 | 提交前就发出会在回滚时多发；分布式事件一律经发件箱 |
| 发布、消费观察点（`I*Observer`） | 页面读表，告警用稳定日志事件，目前没有调用方 |
| Meter 指标与模板接入 OpenTelemetry | 模板目前没有 OTel 管线；首批只提供 `ActivitySource` 链路续接和稳定日志事件 |
| 跨物理库合并成一张列表 | 跨库合并分页的代价大；宿主改为选择一个物理库查看。这已覆盖逐库运维 |
| 设置页可调的退避与保留期 | 属于协议参数与部署配置（`framework/docs/components/settings.md` 的划界）。保留期如需在后台调整，模板照操作记录的先例加一行 `HostSettingBindings` 映射 |
| 保留期过后只清载荷、保留台账行 | 整行删除更简单，去重窗口就等于保留期（默认 14 天），远长于任何传输的重投窗口；要做长期对账应查业务数据，不该查消息台账 |
| 中枢自身的 resync、tombstone、环境区分、能力登记 | 这些是具体中枢 SDK 的能力，由业务直接调用 SDK，不进框架契约 |
| 同时启用多个传输 | 一个服务只用一个传输；需要按事件分流时由传输内部路由 |
| 把 Webhook 组件改成基于本组件 | 语义不同；按框架规范 §6.4，不为相似流程抽通用状态机 |

### 功能要求与验收标准

| # | Given / When / Then | 覆盖 |
| --- | --- | --- |
| D1 | **Given** 服务 A 发布 `order.paid`，服务 B 登记了处理器，两者走回环；**When** A 在工作单元内发布并提交；**Then** B 的处理器数秒内执行一次，收件箱有一行 Processed，处理器收到的 EventId、OccurredOn 与发布值一致，TraceId 与 A 的请求相同 | 框架入口测试（唯一一条端到端用例） |
| D2 | **Given** 同上；**When** 工作单元回滚；**Then** 发件箱没有记录，B 收不到事件。没有活动工作单元时发布，抛 `InvalidOperationException` | 框架单测 |
| D3 | **Given** 同一排序键依次提交 e1、e2，e1 首次发送瞬态失败；**When** e1 在退避期内、兜底扫描运行多轮；**Then** e2 一直不发，e1 成功后才发；e1 进死信后 e2 照常发出 | 框架单测（跨轮） |
| D4 | **Given** 传输返回永久失败；**When** 发送；**Then** 直接进死信，记录错误码，不消耗重试；记 `DistributedEventDeadLettered` | 框架单测 |
| D5 | **Given** 瞬态失败持续；**When** 距首次失败超过 `DeadLetterAfter`；**Then** 进死信。此前按指数退避，封顶 `RetryMaxDelay` | 框架单测 |
| D6 | **Given** 同一事件被投递两次；**When** 接收；**Then** 每个消费者只有一行，`DuplicateCount = 1`，处理器只执行一次 | 框架单测 |
| D7 | **Given** 处理器抛出；**When** 处理；**Then** 写入回滚，退避重试，`MaxAttempts` 次后标 Failed；重放成功后标 Processed | 框架单测 |
| D8 | **Given** 立即处理与兜底扫描同时处理同一行；**When** 两边都提交；**Then** 只有一边成功，处理器的数据库写入只生效一次 | 框架关系型数据库测试（SQLite） |
| D9 | **Given** 解码失败、租户不存在或载荷无法反序列化；**When** 接收；**Then** 写入宿主库拒收表，传输照常确认。修复后重新解码，成功则写入对应租户库的收件箱，拒收记录标 Redecoded | 框架单测 |
| D10 | **Given** 共享库里有宿主和两个租户、另有一个专属库，都有待发与待处理记录；**When** 立即路径全部丢失，兜底扫描运行；**Then** 四方的记录都被发送和处理，各在自己的租户上下文里；某个库失败时任务记为失败 | 框架关系型数据库测试 |
| D11 | **Given** 本机 RabbitMQ 或 Kafka；**When** 发布 → 接收；**Then** 写库成功后才确认（Ack / 提交位点）；写库失败时重新投递；传输层坏消息走拒收后确认；断开时健康检查报 Unhealthy | 两个传输共用一套契约测试（无 broker 时跳过，CI 提供两个服务） |
| D12 | **Given** 只有读策略；**When** 调用各端点；**Then** 列表不含载荷；读载荷与所有写端点返回 403；租户上下文传 `database` 返回 403；映射时漏配策略即抛出 | 框架最小宿主测试 |
| D13 | **Given** 测试传输以外部格式投递：载荷里自带业务 ID 与租户，没有框架消息头；**When** 接收；**Then** 传输在 `DecodeAsync` 里异步映射成框架信封，正常写入收件箱 | 框架单测（测试替身传输） |
| D14 | **Given** 传输自己已判定一条消息无法解码；**When** 调用 `RejectAsync`；**Then** 写入拒收表，原因码取传输给出的值；修复传输后重新解码，经 `DecodeAsync` 转为收件箱记录。写库失败时记 `DistributedEventRejectPersistFailed` 再抛出 | 框架单测 |
| D15 | **Given** 写入进程配置 `Outbox:DispatchEnabled=false`、`Inbox:ReceiveEnabled=false`、`Inbox:ProcessingEnabled=false`，且未注册传输；Worker 进程全部开启；**When** 写入进程发布并提交；**Then** 写入进程不发送也不抢扫描锁，由 Worker 的兜底任务发出。若开启了发送或接收却没有注册传输，启动时报错并指明配置键 | 框架单测 |
| D16 | **Given** 某错误码下有 1,200 条死信；**When** 按"状态 = 死信、错误码 = X"批量重投；**Then** 1,200 条全部回到待发送，分批提交，返回条数；之后由兜底扫描发出。收件箱的按条件重放、拒收的按条件重新解码同样成立 | 框架单测 |
| D17 | **Given** 本服务订阅了 `order.paid`，发件箱里有 `invoice.issued`；**When** 读取事件目录；**Then** 两个事件名都列出，各自给出方向、消费者、最近发出、接收与处理时刻、待处理数、异常数和最近错误码 | 框架单测 |
| D18 | **Given** 宿主上下文，共享库里有宿主和两个租户的记录；**When** 选择该库查看并重投其中一个租户的死信；**Then** 列表含三方记录并带租户列，重投在该行所属租户的上下文里执行；`GET /databases` 列出全部物理库 | 框架关系型数据库测试 |
| D19 | **Given** `TestEvent:Enabled=false`；**When** 读取状态并调用发测试事件；**Then** 状态里 `testEventEnabled=false`，本服务不订阅 `event-bus.test`，调用返回业务错误 `DistributedEvents:TestEventDisabled` | 框架单测 |
| D20 | **Given** 模板 `IncludeDistributedEvents=true` 并启动 compose；**When** 管理员点"发送测试事件"；**Then** 发件箱出现一条 Sent，收件箱随后出现对应的 Processed，详情抽屉显示消息头与链路 | 模板 e2e（人工验收一次） |
| D21 | **Given** 发件箱有死信；**When** 点"死信"卡，再点"按当前筛选重投"并确认；**Then** 记录回到待发送并很快变为已发送，写入操作记录 `distributed-event.redelivered`（带条数与筛选摘要，完整选择条件经关联日志回查） | 模板前端 spec + 后端集成测试 |
| D22 | **Given** 只有 `App.DistributedEvents`；**When** 打开页面；**Then** 能看快照、事件目录、列表与详情概要，看不到任何操作入口和"显示载荷"；租户用户看不到库选择、拒收卡与拒收视图 | 模板前端 spec |
| D23 | **Given** 模板 `IncludeDistributedEvents=false`；**When** 生成项目；**Then** 不留下任何包引用、表、迁移、权限、路由、菜单、词条、compose 服务与文档 | 模板生成检查（`ForbiddenTokens`） |

### 非功能要求

- **克隆即可运行**：模板开发 compose 自带 RabbitMQ；框架测试用回环传输，不依赖 broker。
- **默认值可直接上线**：退避、死信窗口、处理次数、扫描间隔和保留期都有默认值，单进程部署三个分工开关全开；必填项只有管理端点的两个策略名。
- **首发延迟**：正常路径是提交后立即发送、写库后立即处理；兜底路径最迟一个扫描间隔加 1 分钟宽限期。

### 已确认的决策与待定问题

| 事项 | 结论 | 状态 |
| --- | --- | --- |
| 是否立项 | 现在立项，基于 develop | 用户 2026-11-06 确认 |
| 私有中枢接入的七个缺口 | 全部按终局形态纳入首批：异步解码与外部格式映射、传输拒收入口、进程分工开关、按条件批量恢复与错误分类指南、事件目录、宿主按库查看、测试事件开关 | 用户 2026-11-07 确认 |
| 首批传输 | 回环 + RabbitMQ + Kafka，三者同批交付 | 按终局策略决策（用户 2026-11-07 授权） |
| 模板接入 | 新增布尔参数 `IncludeDistributedEvents`，默认 `false`，全部 `ServiceRole` 适用；带后台页面、compose 里的 RabbitMQ 与内置测试事件 | 按终局策略决策（用户 2026-11-07 授权） |
| UI 方案 | 方案 A | 用户 2026-11-07 确认 |
| 交付节奏 | 一个 PR 交付框架与模板：模板经本地候选包消费同树框架，验证分层各跑一次（§3） | 按终局策略决策（用户 2026-11-07 授权） |
| 协作分工 | Codex：方案评审、实施、端到端验收闭环；Claude：代码审查、UI 与原型核对、进度跟踪与汇报 | 用户 2026-11-07 确认 |
| 载荷可见性 | 列表与详情概要不含载荷；载荷走单独端点，归 Manage 策略；宿主要更严时，按端点名追加授权元数据 | 本规格提议 |

## 2. 方案

### 现状事实

| 事实 | 证据 |
| --- | --- |
| 本地事件在工作单元内推迟，按处理器声明的阶段分发；`BeforeCommit` 仍在事务内 | `framework/components/unit-of-work/Leistd.UnitOfWork.Core/Events/UnitOfWorkPhase.cs` |
| Webhook 组件已走通"工作单元内入库 → 提交后进程内队列立即处理 → 集群任务逐库兜底" | `framework/components/webhooks/Leistd.Webhooks.EntityFrameworkCore/Dispatching/` |
| 按物理库遍历用 `ITenantDatabaseRunner`；物理库清单用 `ITenantDatabaseEnumerator` | `framework/components/multi-tenancy/Leistd.MultiTenancy.Core/ConnectionStrings/` |
| 周期任务的开关登记给调度器，在抢锁前求值：开关关闭的进程不抢锁、不记水位，由其他进程执行 | `framework/docs/components/background-jobs.md`（`AddRecurringJob` 的 `isEnabled`） |
| 后台作业已有根 Activity 的写法 | `framework/components/background-jobs/Leistd.BackgroundJobs.InProcess/Diagnostics/BackgroundJobDiagnostics.cs` |
| 没有 HTTP 入口的场景用 `IAmbientContext.Begin(principal, correlationId)` 建立上下文 | `framework/docs/components/tracing.md` |
| 真实 Redis 契约用例在没有 Redis 时跳过，CI 提供服务 | `framework/Directory.Packages.props`（`Xunit.SkippableFact`） |
| 模板 readiness 健康检查按 `ready` 标签聚合 | `template/backend/src/CompanyName.ProjectName.Api/Hosting/WebHostExtensions.cs` |
| 后台列表页的写法：统计卡 → line 标签 → 工具栏 → TanStack 表格 → 右侧详情抽屉 | `template/frontend/src/app/features/platform/components/webhooks/` |
| 界面列表只用偏移分页，固定排序，并按首次加载时刻设上限 | 用户 2026-11-05 确认 |

### 包与目录

```
framework/components/event-bus/
├─ Leistd.EventBus.Core                  （扩充，业务代码只引用这个包）
│  ├─ Abstractions/   + IDistributedEventBus : IEventBus
│  └─ Events/         + IDistributedEvent、DistributedEvent、EventNameAttribute、IOrderedDistributedEvent
├─ Leistd.EventBus.Local                 （不变）
├─ Leistd.EventBus.Distributed           （新增，相当于纵向切片的 Core）
│  ├─ Subscriptions/  订阅表（事件名 → 事件类型 → 消费者）
│  ├─ Transport/      信封、消息头、原始消息、传输与接收契约、结果分类
│  ├─ Loopback/       进程内回环传输
│  ├─ Management/     IDistributedEventManagementService 与内部实现
│  ├─ Dtos/           按发件箱、收件箱、拒收、恢复、状态五个主题合并
│  ├─ Stores/         管理存储契约与三类记录状态
│  ├─ DependencyInjection.cs  AddDistributedEventBus、AddDistributedEventHandler
│  └─ Events/  EventHandlers/  Options/  Errors/  ExceptionMappings/  Resources/  Diagnostics/
├─ Leistd.EventBus.EntityFrameworkCore   （新增）
│  ├─ Entities/  EntityConfigurations/  Options/（独立保留期选项）
│  ├─ Publishing/     事务发件箱发布实现
│  ├─ Receiving/      持久接收、拒收与元数据校验
│  ├─ Dispatching/    立即队列、处理器、扫描与周期任务
│  ├─ Stores/         按库管理、目录、恢复与作用域
│  └─ Retention/      保留期任务 event-bus.retention
├─ Leistd.EventBus.AspNetCore            （新增）
│  └─ Endpoints/      MapDistributedEvents、DistributedEventEndpoints.*Name
├─ Leistd.EventBus.RabbitMQ              （新增）
│  └─ 包根传输、消费循环与健康检查；Options/
└─ Leistd.EventBus.Kafka                 （新增）
   └─ 包根传输、消费循环与健康检查；Options/
```

- `Leistd.EventBus.Core` 只加契约。领域层引用它是为了本地事件，不能让管理面、选项与 DTO 顺着传递引用进入领域层。
- `Leistd.EventBus.Distributed` 与 `Leistd.EventBus.Local` 并列，承担本家族分布式部分的 Core 角色，依赖只限 `Microsoft.Extensions.*` 与 `*.Abstractions`。
- 回环只有一个调用点，不单独建包。
- RabbitMQ 与 Kafka 是可替换的中间件，按框架规范 §5 各自成包，分别依赖官方客户端 `RabbitMQ.Client` 7.x 与 `Confluent.Kafka`。

### 公共 API

| 入口 | 包 | 说明 |
| --- | --- | --- |
| `IDistributedEventBus.PublishAsync(@event, ct)` | Core | 业务唯一的发布入口：在当前工作单元内写发件箱，没有活动工作单元时抛 `InvalidOperationException`。在本地事件处理器里调用时，处理器须声明 `[UnitOfWorkEventHandler(UnitOfWorkPhase.BeforeCommit)]` |
| `DistributedEvent`、`IDistributedEvent` | Core | 事件基类，继承 `BaseEvent`，自带 `EventId` 与 `OccurredOn`；`BaseEvent` 两个元数据属性增加 init，以便接收时还原原始标识与发生时刻，不重新生成 |
| `[EventName("order.paid")]` | Core | 跨服务契约名。不标时取类型全名；文档要求跨服务事件都标上 |
| `IOrderedDistributedEvent.OrderingKey` | Core | 需要保序的事件实现它；同一键串行发送和处理 |
| `IEventHandler<TEvent>` | Core（现有） | 消费方沿用本地处理器接口 |
| `AddDistributedEventBus(configure?, configSectionPath = "Leistd:EventBus:Distributed")` | Distributed | 选项绑定 + `ValidateOnStart`；登记错误码、内置测试事件及其消费者 |
| `AddDistributedEventHandler<THandler>(consumerName?)` | Distributed | 登记处理器并声明订阅。消费者名默认取处理器类型全名；处理器改名时显式指定，否则幂等记录失效 |
| `AddLoopbackEventTransport()` | Distributed | 进程内回环，用于测试和单进程开发 |
| `IDistributedEventTransport` | Distributed | 替换点，见下文"传输契约" |
| `IDistributedEventReceiver` | Distributed | 传输调用的入站入口：`ReceiveAsync` 与 `RejectAsync` |
| `IDistributedEventSubscriptions` | Distributed | 本服务的订阅表：事件名、事件类型、消费者。传输据此绑定自己的路由，如队列绑定或主题订阅 |
| `IDistributedEventManagementService` | Distributed（EF 实现存储） | 快照、事件目录、物理库清单、三张表的列表与详情、读载荷、单条或按条件重投、重放、重新解码、丢弃、发测试事件 |
| `DistributedEventManagedEvent` | Distributed | 重投、重放、重新解码、丢弃成功后，经可选的 `ILocalEventBus` 发布，提交后分发。带动作、条数、筛选条件或 id 列表；宿主据此写操作记录 |
| `AddDistributedEventBusEfCore<TDbContext>()`、`modelBuilder.ConfigureDistributedEventBus()` | EF | 存储、发布实现、接收入口；提交后立即发送与处理；登记集群任务 `event-bus.outbox`、`event-bus.inbox`，开关取自分工选项 |
| `AddDistributedEventRetention<TDbContext>(configure?, configSectionPath)` | EF | 保留期任务，默认开启 |
| `MapDistributedEvents(options)` | AspNetCore | `ReadPolicy`、`ManagePolicy` 必填；端点名前缀 `Leistd.EventBus.Distributed.` |
| `AddRabbitMqEventTransport(configure?, configSectionPath = "Leistd:EventBus:RabbitMQ")`、`IHealthChecksBuilder.AddRabbitMqEventTransport(tags)` | RabbitMQ | 传输与健康检查，健康检查由宿主挂上 |
| `AddKafkaEventTransport(configure?, configSectionPath = "Leistd:EventBus:Kafka")`、`IHealthChecksBuilder.AddKafkaEventTransport(tags)` | Kafka | 同上 |

### 传输契约

```csharp
public interface IDistributedEventTransport
{
    string Name { get; }

    // 成功 / 瞬态失败 / 永久失败(稳定错误码)；抛出异常按瞬态失败处理
    Task<DistributedEventSendResult> SendAsync(OutgoingDistributedEvent message, CancellationToken cancellationToken);

    // 原始消息 → 框架信封。外部格式在这里映射；无法解码时抛 DistributedEventDecodeException(reasonCode, message)
    ValueTask<IncomingDistributedEvent> DecodeAsync(RawDistributedEvent raw, CancellationToken cancellationToken);
}

public interface IDistributedEventReceiver
{
    // 传输收到消息后调用：解码 → 写收件箱或拒收表 → 返回。暂时性解码依赖故障、存储故障与取消向上传播，不确认，由传输重投
    Task ReceiveAsync(RawDistributedEvent raw, CancellationToken cancellationToken);

    // 传输自己已判定无法处理（如外壳解码失败且位点已由 SDK 提交）：直接写拒收表。
    // 写库失败时记 DistributedEventRejectPersistFailed 再抛出；传输能重投就重投，不能就到此为止
    Task RejectAsync(RawDistributedEvent raw, string reasonCode, string error, CancellationToken cancellationToken);
}
```

| 类型 | 字段 |
| --- | --- |
| `OutgoingDistributedEvent` | `EventId`、`EventName`、`OccurredAt`、`OrderingKey`、`TenantId`、`Payload`（UTF-8 JSON）、`Headers`（traceparent、correlation id） |
| `RawDistributedEvent` | `Body`（字节）、`Headers`、`Coordinates`（不透明键值，如 topic/partition/offset、SDK 生成的事件 ID）、`ReceivedAt` |
| `IncomingDistributedEvent` | 与 Outgoing 同构，另带 `Coordinates` |

自定义 `IDistributedEvent` 类型须通过 init 或 JSON 构造函数支持还原 `EventId` 与 `OccurredOn`，接收时校验其与信封一致。

只有不可恢复的单条消息格式错误抛 `DistributedEventDecodeException` 并入拒收；注册中心不可用、连接故障等暂时性依赖故障向上传播，不将可恢复消息确认丢弃。调用方取消始终传播，不记失败尝试。

传输负责在框架信封与自己的线上格式之间双向映射。框架只规定信封字段，不规定线上格式，所以：
- 信封里没有消息头的中枢，可以把租户、链路放进载荷公共头；
- 外部系统发来的载荷，可以按通道从载荷里取业务 ID 与租户，取不到时退回中枢自己的事件 ID；
- Avro、Protobuf 等外壳在 `DecodeAsync` 里异步解析，需要查注册中心时也不阻塞。

### 错误分类（传输实现指南）

| 类别 | 判定 | 例子 | 结果 |
| --- | --- | --- | --- |
| 瞬态 | **整条通道**暂时不可用，修好后原样重发即可成功 | 连接断开、超时、broker 不可用、限流、鉴权失败或凭据过期、SDK 进入降级 | 退避重试，最长 `DeadLetterAfter`；期间积压告警 |
| 永久 | **这一条消息**本身发不出去，原样重发永远失败 | 消息过大、载荷不符合通道 schema、目标通道不存在或不属于本应用 | 直接进死信；修复后按条件批量重投 |
| 结果未知 | 已发出但没有确认 | 发布超时 | 按瞬态重发；消费端按 `EventId` 去重吸收重复 |

鉴权失败要判瞬态：判成永久的话，故障期间的每条事件都会进死信，恢复要靠大批量重投；判成瞬态时只是积压，凭据修好后自动恢复。

### 消息约定

| 字段 | 位置 | 说明 |
| --- | --- | --- |
| `EventId` | 信封 | 发布时生成，重投时不变；跨服务幂等用它 |
| `EventName` | 信封 | 契约名 |
| `OccurredAt` | 信封 | 业务发生时刻 |
| `OrderingKey` | 信封 | 可选 |
| `TenantId` | 信封 | 发布时的租户；宿主为空 |
| `Payload` | 信封 | System.Text.Json Web 默认（camelCase），发布时冻结 |
| `traceparent` | 消息头 | W3C 链路，消费时据此续接 Activity |
| `leistd-correlation-id` | 消息头 | 关联标识，消费时据此还原 |

### 进程分工

| 选项 | 默认 | 关闭后的效果 |
| --- | --- | --- |
| `Outbox:DispatchEnabled` | `true` | 本进程不做提交后立即发送，`event-bus.outbox` 在本进程不抢锁；发件箱照常写入 |
| `Inbox:ReceiveEnabled` | `true` | 传输在本进程不启动消费循环。框架提供的传输都遵守这一条，自定义传输按指南读同一个选项 |
| `Inbox:ProcessingEnabled` | `true` | 本进程不做提交后立即处理，`event-bus.inbox` 在本进程不抢锁 |

- 开关由宿主显式配置，不按"是否注册了传输"推断。
- 发送或接收开启却没有注册传输时，启动期报错并指明配置键。
- 典型分工：写入进程三个开关全关、不注册传输；Worker 三个开关全开。单进程部署用默认值即可。

### 发布与发件箱

1. **写入**：`PublishAsync` 校验事件实现了 `IDistributedEvent`，序列化后在当前工作单元的 DbContext 里加一行发件箱记录，`Status = Pending`、`NextAttemptAt = 现在 + 1 分钟`。这 1 分钟是留给立即发送的宽限期。
2. **立即发送**（`DispatchEnabled`）：提交后把本次写入的 Id 送进进程内队列（`IBackgroundTaskQueue`，带租户上下文）。队列满或进程退出时不处理，交给兜底。
3. **兜底**：集群任务 `event-bus.outbox` 默认每 5 秒运行一次，写法同 `webhooks.dispatch`：
   - 逐个物理库进入，库内放开租户过滤，取到期的待发记录；
   - 每条在所属租户的上下文与独立作用域里处理，有界并发；
   - 某个库失败时汇总抛出。
4. **保序取数**：带排序键的记录，只有同租户同事件名同键下不存在更早的 Pending 记录时才发。两个入口在发送前都检查这一条。
5. **结果**（按序判断）：

   | 顺序 | 条件 | 处理 |
   | --- | --- | --- |
   | 1 | 成功 | `Sent`，记 `SentAt` |
   | 2 | 永久失败 | `DeadLettered`，记错误码，记 Error 日志 `DistributedEventDeadLettered` |
   | 3 | 瞬态失败，距 `FirstFailureAt` 已超过 `DeadLetterAfter` | `DeadLettered`，错误码 `retry-window-exceeded` |
   | 4 | 瞬态失败，未超窗口 | 退避 `RetryBaseDelay × 2^(n-1)`，封顶 `RetryMaxDelay`，±10% 抖动 |

6. **积压告警**：每轮扫描时，若最旧待发记录超过 `BacklogWarningAfter`，记 Warning 日志 `DistributedEventOutboxBacklog`，带库、条数与最旧时长。
7. **重投**：只对死信开放。`Status = Pending`、`NextAttemptAt = 现在`、清空 `FirstFailureAt`、`RedeliverCount + 1`，`EventId` 不变。单条重投在提交后立即入队；按条件重投见"批量恢复"。

### 接收、收件箱与拒收

1. **接收**（`ReceiveEnabled`，由传输的消费循环调用 `ReceiveAsync`）：
   1. 调传输的 `DecodeAsync`；
   2. 切换到信封里的租户；
   3. 按事件名查订阅表，用各消费者登记的事件类型试反序列化载荷；反序列化前以信封的 `EventId`、`OccurredAt` 填入载荷的 `eventId`、`occurredOn`，信封元数据为权威值，外部业务载荷不必重复携带；
   4. 在新的工作单元里为每个消费者写一行收件箱记录；
   5. 提交后返回。

   `(TenantId, EventId, Consumer)` 已存在时只把 `DuplicateCount` 加 1。没有消费者的事件名记 Debug 后直接返回。
2. **拒收**：以下情况写宿主库拒收表，不抛出：
   - `DecodeAsync` 抛出解码异常；
   - 租户不存在；
   - 载荷无法反序列化。

   传输自己判定失败的消息走 `RejectAsync`，原因码由传输给出。拒收不阻塞传输，毒消息不会被无限重投。
3. **立即处理**（`ProcessingEnabled`）：接收提交后把收件箱 Id 送进进程内队列；兜底任务 `event-bus.inbox` 的写法同发件箱。收件箱也按排序键保序：同租户、同事件名、同消费者、同键下没有更早的 Pending 记录才处理。
4. **处理**：每行在以下上下文里执行：所属租户、系统身份、消息头还原出的关联标识、续接的 Activity。处理器的写入与"标记 Processed"在同一个事务工作单元里提交；收件箱以 `AttemptCount` 作并发令牌，每次成功或失败都递增；成功递增与业务写入、Processed 标记同事务。并发冲突整笔回滚后退出，不另写一次失败覆盖胜方（D8）。
5. **失败**：回滚后，在独立工作单元里记失败并按退避排期；`MaxAttempts` 次后标 Failed，记 Error 日志 `DistributedEventConsumeFailed`。
6. **重放**：对终态（Processed、Failed）开放。`Status = Pending`、`AttemptCount = 0`、`NextAttemptAt = 现在`、清空错误与 `ProcessedAt`、`ReplayCount + 1`。每轮重放重新获得 `MaxAttempts` 次预算。重放 Processed 会让处理器再执行一次，界面确认框里写明这一点。
7. **重新解码**：对 Pending 的拒收记录，用当时保存的原始消息重新走"接收"整条流程（含传输的 `DecodeAsync`）。
   - 成功：写入对应租户库的收件箱，拒收记录标 Redecoded。
   - 失败：更新错误，状态不变。

   先写收件箱（有去重保护），再标记拒收记录。两个库不追求原子。

### 批量恢复

| 动作 | 单条 / 勾选 | 按条件 |
| --- | --- | --- |
| 重投（发件箱） | `ids`，最多 100 | 状态固定为死信，可加事件名、错误码、创建时间上限 |
| 重放（收件箱） | `ids`，最多 100 | 状态取 Failed 或 Processed，可加事件名、消费者、错误码、接收时间上限 |
| 重新解码（拒收） | `ids`，最多 100 | 状态固定为 Pending，可加原因码、接收时间上限 |
| 丢弃（拒收） | `ids`，最多 100 | 同上 |

- 按条件的动作在当前查看范围内执行：租户，或宿主选中的物理库。服务端按 Id 每 500 条一个事务分批更新，返回受影响条数。
- 批量改回 Pending 的记录 `NextAttemptAt = 现在`，交给兜底任务发送或处理，不灌进进程内队列，避免冲垮进程。
- 按条件执行必须带上"时间上限"（界面取首次加载时刻），这样之后新产生的同类记录不会被意外卷入。

### 宿主按库查看

- `GET /databases`（仅宿主）列出宿主库和登记的其他物理库；返回代表租户 Id、该库租户 Id 清单与数量。数据来自 `ITenantDatabaseEnumerator`，不虚构该接口没有的名称、启用状态与宿主库租户数。以 `host` 或代表租户 Id 作为选择键，不暴露连接串或连接指纹。
- 读写端点都接受 `database` 参数：
  - 宿主上下文：进入该库，放开租户过滤，返回该库全部行并带 `TenantId`；单条动作在行所属租户的上下文里执行。不传时默认宿主库。
  - 租户上下文：始终只看自己的数据；传 `database` 返回 403，错误码 `DistributedEvents:HostOnly`。

- 拒收无法可靠确定租户，集中在宿主库，仅宿主可读写（包括详情和载荷）；租户调用返回 `DistributedEvents:HostOnly`。宿主看任何物理库时拒收视图仍显示宿主库集中拒收，界面明确标注范围；租户快照的拒收数为 null，不显示对应卡与视图。
- 测试事件写入当前宿主或租户上下文；不能借物理库选择切换业务租户。在宿主选择非宿主库时隐藏测试按钮，带非宿主 database 的测试请求返回字段验证错误。

界面页签的计数为需要关注的异常数：发件箱用 `DeadLettered`、收件箱用 `FailedInbox`、拒收用 `PendingRejections`，不是当前列表总条数。租户列在本地身份形态且当前用户有租户查看权限时，经既有租户 API 解析显示名，保留 ID 提示；未开放目录 API、无目录权限或租户已删除时显示原 ID，不为消息只读权限扩大租户目录权限。

### 事件目录

`GET /catalog` 返回当前查看范围内的事件名，取并集：本服务已订阅的、发件箱与收件箱在保留期内出现过的。每个事件名给出：

| 字段 | 来源 |
| --- | --- |
| 方向 | 发出（发件箱出现过）/ 接收（已订阅） |
| 消费者 | 订阅表 |
| 最近发出 / 最近接收 / 最近处理 | 各表按事件名分组取最大时刻 |
| 待发、待处理 | Pending 计数 |
| 死信、处理失败 | 终态失败计数 |
| 最近错误码 | 最近一条失败记录 |

用途：判断某类事件是否静默、某个通道是否被拒，例如错误码"通道不属于本应用"。

### 幂等与一致性承诺（写进组件文档）

| 承诺 | 由谁保证 |
| --- | --- |
| 发布随业务事务提交或回滚 | 发件箱与业务数据同在一个工作单元 |
| 至少一次送达 | 发件箱持久重试，加上传输"写库成功后才确认" |
| 处理器对本服务数据库的写入只生效一次 | 收件箱唯一约束、处理与标记同事务、并发令牌 |
| 处理器的外部副作用 | **至少一次**，处理器自己按 `EventId` 幂等 |
| 单键有序 | 发送侧与处理侧都按键阻塞。死信与 Failed 不再阻塞，所以消费方要能容忍版本跳号 |
| 同一键上的并发事务谁先谁后 | 不保证，按提交后可见的插入顺序。业务对同一聚合的并发写本来就由并发令牌串行化 |
| 去重窗口 | 等于保留期（默认 14 天） |

### 数据模型

| 表 | 库 | 关键字段 |
| --- | --- | --- |
| `DistributedEventOutbox` | 业务所在库 | `Id`（long 自增，保序的全序依据）、`EventId`、`TenantId`、`EventName`、`OrderingKey`、`OccurredAt`、`Payload`、`Headers`、`Status`（Pending / Sent / DeadLettered）、`AttemptCount`、`NextAttemptAt`、`FirstFailureAt`、`LastErrorCode`、`LastError`（截断到 512）、`RedeliverCount`、`CreationTime`、`SentAt` |
| `DistributedEventInbox` | 消息所属租户的库 | `Id`（long 自增）、`EventId`、`TenantId`、`Consumer`、`EventName`、`OrderingKey`、`Payload`、`Headers`、`OccurredAt`、`Coordinates`、`Status`（Pending / Processed / Failed）、`AttemptCount`（并发令牌）、`NextAttemptAt`、`LastErrorCode`、`LastError`、`DuplicateCount`、`ReplayCount`、`ReceivedAt`、`ProcessedAt` |
| `DistributedEventRejection` | 宿主库 | `Id`、`Transport`、`Reason`（框架原因码或传输原因码）、`Error`、`EventName` 与 `EventId`（解得出时才有）、`RawBody`、`Headers`、`Coordinates`、`Status`（Pending / Redecoded / Discarded）、`RedecodeCount`、`ReceivedAt`、`ResolvedAt` |

- 发件箱、收件箱实现 `IMultiTenant`；拒收表不分租户。三表都有 `LastModificationTime`，每次状态变更或恢复时更新，保留期只删除该时刻早于期限的终态行。Pending 永不清理。
- 唯一约束：发件箱 `(TenantId, EventId)`，收件箱 `(TenantId, EventId, Consumer)`。宿主行与租户行各一条带 `HasFilter` 的唯一索引。
- 查询索引：发件箱与收件箱各有 `(Status, NextAttemptAt, Id)`、`(TenantId, EventName, OrderingKey, Id)`、`(EventName, Status)`。
- 表名沿用 EF Core 默认约定。保留期删除用"加载 + RemoveRange"，不用 `ExecuteDelete`。

### 配置项

`Leistd:EventBus:Distributed`：

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `RetryBaseDelay` / `RetryMaxDelay` | `00:00:02` / `00:05:00` | 发件箱与收件箱共用的指数退避 |
| `DeadLetterAfter` | `1.00:00:00` | 发件箱瞬态失败的最长重试窗口，覆盖一天以内的通道中断 |
| `MaxAttempts` | `5` | 收件箱处理次数，用完标 Failed |
| `DispatchInterval` | `00:00:05` | 两个兜底任务的扫描间隔 |
| `BacklogWarningAfter` | `00:05:00` | 积压告警阈值 |
| `Outbox:DispatchEnabled`、`Inbox:ReceiveEnabled`、`Inbox:ProcessingEnabled` | `true` | 进程分工，见上文 |
| `TestEvent:Enabled` | `true` | 内置测试事件。中枢不允许服务自发自收时关闭：本服务不订阅 `event-bus.test`，快照里报 `testEventEnabled=false`，界面隐藏按钮 |

`Leistd:EventBus:Distributed:Retention`（EF 包的 `DistributedEventRetentionOptions`，由 `AddDistributedEventRetention` 独立绑定；不嵌入 `DistributedEventOptions`，自定义配置节时仍只有这一个值源）：

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `Enabled` / `Days` | `true` / `14` | 删除最后变更早于期限的终态记录 |

`Leistd:EventBus:RabbitMQ`：

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `ConnectionStringName` | `RabbitMQ` | 取 `ConnectionStrings:RabbitMQ`（`amqp://` URI） |
| `Exchange` | `leistd.events` | topic 交换机，路由键为事件名 |
| `QueueName` | `IHostEnvironment.ApplicationName` | 每个服务一个持久队列，副本竞争消费 |
| `PrefetchCount` | `32` | 消费预取 |

`Leistd:EventBus:Kafka`：

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `BootstrapServersConnectionStringName` | `Kafka` | 取 `ConnectionStrings:Kafka`（逗号分隔的 bootstrap servers） |
| `TopicPrefix` | 空 | 主题名 = 前缀 + 事件名 |
| `GroupId` | `IHostEnvironment.ApplicationName` | 每个服务一个消费组 |
| `AutoCreateTopics` | `true` | 开发便利；生产由运维建主题时关闭 |

这些都是部署配置，不进设置页。

### 传输映射

| 框架约定 | 回环 | RabbitMQ | Kafka | 私有中枢（业务实现） |
| --- | --- | --- | --- | --- |
| 发送成功 | 写入内存 Channel | publisher confirm | `Persisted` | SDK 发布返回 |
| 永久失败 | — | 消息过大等协议错误 | 消息过大、主题授权失败 | schema 不符、通道不属于本应用 |
| 瞬态失败 | — | 连接断开、确认超时 | broker 不可用、超时 | SDK 重试后抛出、鉴权失败、降级 |
| 路由 | 事件名 | 交换机 + 路由键 = 事件名；队列启用 Single Active Consumer，接收串行持久化 | 主题 = 事件名（或映射），键 = `OrderingKey ?? EventId` | 订阅表 → 中枢通道映射；键写入通道的分区键字段 |
| 消息头 | 原样 | AMQP headers | Kafka headers | 没有头时写入载荷公共头 |
| 确认时机 | 接收入口返回后 | `ReceiveAsync` 返回后 Ack，故障时保留当前消息重试或重建通道重投，不越过未持久化消息 | 禁用自动位点存储与自动提交；当前分区持久化成功才提交 offset+1；失败不得提交该分区后续位点 | handler 返回即提交，抛出则重投 |
| 传输层坏消息 | — | 无法解析的消息 → `RejectAsync` 后 Ack | 反序列化失败 → `RejectAsync` | SDK 失败回调 → `RejectAsync`（位点已提交） |
| 坐标 | — | exchange、routingKey、deliveryTag | topic、partition、offset | topic、partition、offset、中枢事件 ID |
| 连接状态 | 恒为 Healthy | 健康检查读连接与通道 | 健康检查读消费者 | 中枢 SDK 的健康检查加上 `event-bus` 标签 |

RabbitMQ 的单活队列与串行接收保证首次交付的入库次序；Kafka 同一键使用同一分区，按分区顺序入库。传输重投、进程切换可能产生迟到重复，不承诺跨故障重新排序或跨事件主题有序；这些重复由收件箱吸收。自定义传输须保留其支持范围内的接收次序。

### 管理端点（`MapDistributedEvents`）

| 方法与路由 | 策略 | 说明 |
| --- | --- | --- |
| `GET /status` | Read | 快照：待发数与最旧时长、死信、待处理、处理失败、待处理拒收、近 24 小时发出与处理数、传输名与状态（标签为 `event-bus` 的健康检查，未登记时为 Unknown）、`testEventEnabled` |
| `GET /databases` | Read（仅宿主） | 物理库清单 |
| `GET /catalog` | Read | 事件目录 |
| `GET /outbox`、`GET /outbox/{id}` | Read | 偏移分页，按 `Id` 倒序，按首次加载时刻设上限；可按状态、事件名、错误码、关键字（事件 ID 或排序键，精确匹配）筛选。不含载荷 |
| `GET /inbox`、`GET /inbox/{id}` | Read | 同上，另可按消费者筛选 |
| `GET /rejections`、`GET /rejections/{id}` | Read（仅宿主） | 同样分页、按事件名和原因筛选；关键字只精确匹配事件 ID，不匹配排序键 |
| `GET /{outbox\|inbox\|rejections}/{id}/payload` | Manage | 载荷与原始消息 |
| `POST /outbox/redeliver` | Manage | 请求体为 `ids`（最多 100）或筛选条件，二选一；返回受影响条数，以及被跳过的 id 与原因 |
| `POST /inbox/replay` | Manage | 同上 |
| `POST /rejections/redecode`、`POST /rejections/discard` | Manage | 同上 |
| `POST /test` | Manage | 发布一条 `event-bus.test`；`TestEvent:Enabled=false` 时返回业务错误 |

以上端点除物理库清单外都接受 `database` 参数；拒收集中在宿主库且仅宿主可访问，测试事件只允许当前上下文或宿主库，规则见"宿主按库查看"。

### HTTP 与管理 DTO 约定

- 三张表的自增 Id 在 JSON 中输出十进制字符串；路径参数仍为十进制 long，前端不转为 number。勾选请求的 ids 接受十进制字符串。
- 拒收列表含诊断用 `coordinates`，解码失败无事件名／标识时，界面回退队列或主题及消息标识；不携带消息头或原始字节。
- 列表返回 `PagedResult<T>`；详情概要为 `record`（列表同形）加 `headers`，收件箱与拒收另有 `coordinates`。载荷只从独立 Manage 端点返回：发件箱、收件箱返回 `DistributedEventPayloadOutputDto`，其 `payload`（JSON 文本）必填；拒收返回 `DistributedEventRawMessageOutputDto`，其 `rawBody`（Base64）必填。
- 查询的 `before` 为 UTC 时刻，按创建/接收时间含上限；筛选恢复必须提供，不能因翻页改为当前时刻。按当前筛选包含关键字的精确匹配（拒收只匹配事件 ID）；事件名、关键字、错误码、消费者与拒收原因均最多 256 个字符，列表与恢复筛选一致。
- 业务错误：`DistributedEvents:RecordNotFound`、`DistributedEvents:DatabaseNotFound` 默认 404，`DistributedEvents:HostOnly` 默认 403，`DistributedEvents:TestEventDisabled` 默认 400。单项跳过原因用 `not-found`、`invalid-state`、`redecode-failed`，不把部分跳过变成整批异常。
- 列表用例为 `GetOutboxListAsync`、`GetInboxListAsync`、`GetRejectionsAsync`，详情用例为 `GetOutboxAsync`、`GetInboxAsync`、`GetRejectionAsync`；映射端点时检查管理用例与内部管理存储均已登记，避免缺存储的宿主运行到请求阶段才报错。
- 跳过原因常量归 `DistributedEventRecoverySkipReasons`；框架重试窗口失败码归 `DistributedEventOutboxFailureCodes`，与允许传输自定义的失败码并存。测试事件名与消费者名归 `DistributedEventTestEvent`，不散落字符串。前端跳过原因与传输健康状态用同值的联合类型。
- 模板操作记录目标名称保存条数与选择摘要，完整筛选或实际 ID 列表写入同关联标识的结构化日志，沿用通用操作记录的长度边界。
- 每个成功提交的恢复批次发布一条 `DistributedEventManagedEvent`，带本批实际改动条数和原筛选（含时间上限）或实际改动的 ids；部分批次失败时已经提交的动作仍有准确留痕。

### 可观测性

| 层 | 首批提供 | 不提供 |
| --- | --- | --- |
| 框架 | `ActivitySource "Leistd.EventBus"`：发布时写 `traceparent`，处理时续接。稳定日志事件：`DistributedEventDeadLettered`、`DistributedEventOutboxBacklog`、`DistributedEventConsumeFailed`、`DistributedEventRejected`、`DistributedEventRejectPersistFailed`。事件目录（静默检测）、传输健康检查 | Meter 指标、观察点回调 |
| 模板 | 后台页面（快照卡、事件目录、三张表、详情抽屉、操作）；readiness 挂传输健康检查；操作记录 | OTel 管线、仪表盘的"需关注"卡片 |

页面属于操作层，回答"现在有没有积压、哪条失败了、哪类事件静默了、要不要重投"。趋势与告警归观测平台，靠稳定日志事件接入。

### 模板集成与条件裁剪

`IncludeDistributedEvents` 是可以独立取舍的产品边界，按 `docs/template/development-guide.md#31-加开关的两条规则` 定为布尔参数，默认 `false`，适用于全部 `ServiceRole`。为 `true` 时生成：

| 层 | 内容 |
| --- | --- |
| Api | 组合 `AddDistributedEventBus` 与 `AddRabbitMqEventTransport`，readiness 挂传输健康检查；`ComponentEndpoints` 映射 `distributed-events` 组；权限 `App.DistributedEvents`、`App.DistributedEvents.Manage`（开发者组，宿主与租户两侧）。重投、重放、重新解码、丢弃写操作记录：成功的由 `DistributedEventManagedEvent` 处理器在提交后写，被拒的由端点动作码补记 |
| Infrastructure | DbContext 调 `ConfigureDistributedEventBus()`；`AddDistributedEventBusEfCore`、`AddDistributedEventRetention`；Identity 与 Resource 两套增量迁移（整文件守卫，并登记生成排除），快照加条件块 |
| 部署 | 开发与生产 compose 加 `rabbitmq:4-management-alpine`，生产账号经 `.env` 提供；连接串 `ConnectionStrings__RabbitMQ`。模板是单进程部署，分工开关用默认值 |
| 前端 | `platform/distributed-events` 路由与页面；侧栏开发者组入口，组条件改为 `OpenIddictServer \|\| IncludeWebhooks \|\| IncludeDistributedEvents`；权限常量、DTO 与服务、词条（en / zh-CN，`distributed-events` scope）、Mock 数据、spec |
| 文档 | 生成项目 `docs/standards/distributed-events.md`，内容包括：何时用分布式事件、何时用本地事件或 ServiceClient；契约、发布与消费；幂等与保序；写入进程与 Worker 分工；故障处理手册（积压、死信、处理失败、拒收、静默）。`docs/README.md` 按任务读取表加一行 |

业务示例不随模板生成：内置的 `event-bus.test` 已能验证整条链路。

代价：
- 有效形态从 640 组翻倍到 1,280 组；
- 场景矩阵新增 `distributed-events`（core 档）与 `resource-api-distributed-events`（full 档）；
- compose 多一个约 100 MB 内存的容器。

### 业务项目怎么用（端到端）

| 场景 | 用法 |
| --- | --- |
| 领域事件要通知其他服务 | `BeforeCommit` 本地处理器里转成分布式事件发布；契约类放共享契约包，标 `[EventName]` |
| 应用服务直接发 | `[UnitOfWork]` 方法里调 `IDistributedEventBus.PublishAsync` |
| 消费方维护本地投影 | 实现 `IEventHandler<T>` 并用 `AddDistributedEventHandler` 登记；直接写库，框架保证只生效一次；按版本号覆盖，容忍跳号 |
| 消费方调用外部系统 | 处理器按 `EventId` 幂等 |
| 同一实体要求有序 | 事件实现 `IOrderedDistributedEvent`，键取实体 Id |
| 写入方与 Worker 分进程部署 | 写入进程把三个分工开关全关、不注册传输；Worker 全开并注册传输 |
| 对接私有事件中枢 | 实现 `IDistributedEventTransport`（发送、错误分类、`DecodeAsync` 映射）和消费循环（`ReceiveAsync` / `RejectAsync`、遵守 `ReceiveEnabled`），登记带 `event-bus` 标签的健康检查；中枢不允许自发自收时关掉测试事件；resync、tombstone 等中枢专有能力直接调用 SDK |
| 接收外部系统发布的事件 | 契约类按对方载荷建模、标对方的事件名；传输在 `DecodeAsync` 里从载荷取业务 ID 与租户 |
| 服务内同步查询与命令 | 继续用 ServiceClient |
| 进程内解耦 | 继续用本地事件总线 |

### 备选与取舍

| 备选 | 结论 |
| --- | --- |
| 封装 CAP、MassTransit、Wolverine | 不采用。CAP 与工作单元平行且不支持逐库扫描；MassTransit 9 起商业许可；Wolverine 要换整套运行时 |
| 照 ASP.NET Boilerplate 后继框架的 provider 形态：继承总线基类，实现发布、订阅与序列化 | 不采用。窄契约（发送、解码、推送给接收入口）让一个传输只需几百行，也更接近 MassTransit、CAP 的"传输与消费分离" |
| 首批只做 RabbitMQ，Kafka 以后再做 | 不采用。只用一种确认模型检验契约，容易被它塑形；"提交位点"模型正是私有中枢的语义，同批交付才是终局 |
| 拆成框架、模板两个 PR | 不采用。两轮 CI、两轮审查、两轮端到端；同树候选包让一个 PR 的验证只跑一次 |
| 把分布式契约、用例与 DTO 都放进 `Leistd.EventBus.Core` | 不采用。领域层会顺着引用拿到管理面 |
| 只轮询，不做立即发送 | 不采用。首发延迟会退化为一个扫描间隔 |
| 收到消息即同步执行处理器 | 不采用。失败无法自动重试，处理时长会拖慢传输确认，阻塞型失败会卡住整个分区 |
| 按"是否注册了传输"推断进程角色 | 不采用。可选依赖不能当模式开关，漏注册会静默变成"只写不发" |
| 批量恢复只支持 id 列表 | 不采用。通道故障后的死信可能上千条，按条件恢复才是终态 |
| 宿主跨库合并成一张列表 | 不采用。跨库合并分页代价大；按库选择已覆盖运维 |
| 发件箱加租约、行级抢占 | 不采用。扫描单副本运行，立即发送与扫描之间有宽限期，重复由收件箱去重 |
| 瞬态失败无限重试 | 不采用。被误判为瞬态的毒消息会永久阻塞它的键；一天的窗口足以覆盖通道中断 |
| 收件箱不存载荷 | 不采用。重放与排查都需要载荷；载荷端点归 Manage 策略 |

### 兼容与回滚

- 框架新增包；Core 新增分布式契约，`BaseEvent.EventId` 与 `OccurredOn` 增加 init，现有 getter 与构造行为不变。
- 模板新增参数，默认关闭，不影响已生成项目。
- 回滚即撤掉对应 PR。

## 3. 计划

### 节奏：一个 PR，契约先行，每层验证只跑一次

框架与模板放在同一个 PR。模板工作区经 `pack-local-feed` 消费同树候选包，PR CI 也从同一候选包源验证模板场景（`docs/framework/ci-release-gates.md#同候选输入计划`）。所以不必等框架 beta 发布，审查、CI 与端到端各只走一轮。合入 develop 后自动发 beta，下游随即可以迁移。

| 波次 | 内容 | 门禁 |
| --- | --- | --- |
| W0 方案评审 | Codex 评审本规格与设计稿 | Codex 写明"通过" |
| W1 契约冻结 | 公共契约：接口、信封、DTO、选项、错误码、端点路由与 DTO；前端 TS DTO 与 Mock 数据 | Claude 审查契约差异（小，一次）；之后各路只依赖冻结的契约 |
| W2 三路并行 | A：框架实现与测试（任务 1–5）；B：模板前端接 Mock（任务 7）；C：模板文档、`template.json` 参数与矩阵登记（任务 8 的非代码部分） | B 完成后 Claude 按设计稿核对 UI（Mock 站点四种宽度截图），偏差在接后端之前改掉 |
| W3 模板后端 | 任务 6：打本地候选包、`distributed-events` 场景工作区、组合、迁移、compose；前端改接真实后端 | 依赖 A |
| W4 汇总验证（L1 一次） | 框架选择器给出的测试项目（含 SQLite 与两种 broker 的契约测试，本机起一次 RabbitMQ 与 Kafka 容器）、打包与隔离消费、模板选择器场景 `-GenerateOnly`、工作区 `verify -Fast` 与新增 e2e spec | 之后提交、同步 develop、推送、开 PR |
| W5 审查与 CI 并行 | PR CI（L2）与 Claude 全量代码审查同时进行；不通过只修失败点，只重跑失败点及直接受影响的检查 | CI 全绿；Claude 写明"通过" |
| W6 端到端验收 | Codex 在最终 head 上生成项目，compose 起 RabbitMQ，用浏览器走完 D20、D21、D22；Claude 同时用真实后端截图做第二次 UI 核对 | Codex 验收报告"通过"；Claude UI 核对"通过" |
| W7 合入与收尾 | CI 全绿且两项"通过"覆盖最终 head 后合入；任务 9 | — |

省时的关键：
- 契约在 W1 冻结并先审，W2 三路互不等待，后期不会因契约返工而重跑测试。
- UI 在接后端之前先按原型核对，界面偏差不会拖到端到端阶段。
- 实施期间只跑目标测试类；整套测试、打包、模板生成、浏览器各只在 W4 和 W6 各跑一次。
- 两种 broker 的契约测试共用一套用例，本机只起一次容器。
- 审查与 CI 并行；返工只重跑受影响的部分。

### 任务

| # | 任务 | 产物 | 验证 |
| --- | --- | --- | --- |
| 1 | Core 扩充与 Distributed 包 | 契约、订阅表、传输契约与错误分类、回环、管理用例（含批量恢复与目录）、DTO、选项（含分工与测试事件开关）与校验、错误码与译文、Activity 与日志事件 | 单测：选项校验报出配置键；D15、D19 |
| 2 | EntityFrameworkCore | 三张表与映射；发布与接收；立即发送与处理；两个兜底任务（开关登记给调度器）；保序；退避；死信；拒收与重新解码；批量恢复；事件目录；宿主按库作用域；保留期 | 入口测试 D1；SQLite 测试 D8、D10、D18；单测 D2–D7、D9、D13、D14、D16、D17 |
| 3 | AspNetCore | `MapDistributedEvents` 与端点名常量 | 最小宿主测试 D12 |
| 4 | RabbitMQ 与 Kafka | 两个传输：消费循环（确认时机、遵守 `ReceiveEnabled`、坏消息走拒收）、健康检查、选项 | 共用契约测试 D11（`SkippableFact`）；CI 增加两个服务 |
| 5 | 框架文档与包 | `framework/docs/components/event-bus.md` 扩写分布式部分：承诺、进程分工、错误分类与传输实现指南、批量恢复、故障处理；两个传输段与组件索引 | 打包到 `.tmp/local-feed`，用 `framework/build/test-package-consumption.ps1` 编译示例 |
| 6 | 模板后端与部署 | 参数、组合、权限、端点、操作记录、两套增量迁移、compose 与 `.env.example` | 场景工作区后端目标测试（D21 后端部分） |
| 7 | 模板前端 | 方案 A 页面、路由、菜单、词条、Mock、spec、一个 e2e spec（发送测试事件 → 发件箱 Sent → 收件箱 Processed） | 目标 spec 与 lint（D21、D22）；Mock 站点四种宽度截图 |
| 8 | 模板文档与矩阵 | `template.json` 参数、生成项目规范（含改用 Kafka 的说明）、读取表、两个矩阵场景、`ForbiddenTokens` | 选择器场景 `-GenerateOnly`（D23）；W6 人工验收 D20 |
| 9 | 收尾 | 本规格 §4；仍然成立的规则上收到常驻文档；给下游写通知（附传输实现指南要点与迁移清单） | — |

### 测试组织

`framework/tests/components/event-bus/Leistd.EventBus.Tests/` 按被测包组织：`Local/`、`Core/`、`Distributed/`、`EntityFrameworkCore/`、`AspNetCore/`、`RabbitMQ/`、`Kafka/`。两种 broker 共用套件归 `Contracts/`，专用替身归 `TestDoubles/`。元数据用例归 `Core/`；只为已有用例创建目录，不预建空目录。

### 协作分工

| 角色 | 负责 |
| --- | --- |
| Codex（本工作树一个会话） | W0 方案评审；W1–W4 实施与验证；提交、推送、开 PR、处理 CI；W6 端到端验收；W7 合入。上下文变大时按波次开新会话，委托书与台账承接状态 |
| Claude（本会话） | W1 契约审查；W2 UI 核对（Mock）；W5 全量代码审查；W6 UI 核对（真实后端）；任务调度、进度监控与汇报 |

评审与实施依据统一写进委托书（`.tmp/distributed-events/`，不提交）：
- 框架开发规范、模板开发规范、前后端编码规范、设计原则 §5；
- 终局但不过度设计；
- 不为安全、少见竞态与假想扩展点加复杂度；
- 契约成员须能被回环、RabbitMQ、Kafka 同时解释，不得只为某一个中枢存在。
