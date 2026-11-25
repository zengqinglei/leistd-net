# 后台任务规范

请求外的异步工作、跨实例互斥与周期任务，同时遵循 [后端开发规范](./coding-backend.md)。

## 1. 选择与放置

从上到下选择第一项符合的场景。

| 场景 | 用法 | 失败处理 |
| --- | --- | --- |
| 请求内可完成，且需当场反馈 | 直接调用应用服务或领域方法 | 请求失败，由调用方重试 |
| 同一请求、同一进程内解耦副作用 | `ILocalEventBus`，领域事件用 `AddLocalEvent` | `BeforeCommit` 失败回滚，提交后失败不回滚、不重试 |
<!--#if (IncludeDistributedEvents)-->
| 其他服务需要知道已发生的事实 | `IDistributedEventBus`，契约放共享包 | 发件箱与收件箱提供至少一次投递及失败恢复 |
<!--#endif-->
| 请求外且允许丢失 | `IBackgroundTaskQueue` | 随进程退出丢失，失败不重试 |
| 提交后必须完成的服务内工作 | 业务状态 + 提交后领域事件入队 | 低频 `Cluster` 扫描到期记录，业务状态记录重试耗尽 |

聚合将待办与宽限期写入业务事务，用 `AddLocalEvent` 触发提交后入队。两条入口共用应用服务，用并发令牌认领、设租约并校验写回，失败后退避。外部调用带稳定业务操作标识，超时短于租约。兜底按状态与到期时间走索引分批处理（如每 5 分钟）。提交进程无法执行时省去处理器，周期任务按可接受的延迟取 30 秒到 1 分钟。

定期维护用周期任务。默认由 API 宿主调度，拆分后将集中扫描任务移到 Worker。用 `AddRecurringJob` 显式选 `Cluster` 或 `EveryInstance`，用 `isEnabled` 登记任务开关，执行失败直接抛出。跨实例互斥使用 `IDistributedLock`，不另起线程或自造跨实例锁，只保护进程内状态时可用 `lock`。

- 周期任务的执行记录使用 `IRecurringJobRunObserver`，由宿主实现存储，不包装任务类型。实现放 Infrastructure 的 `BackgroundJobs/` 目录，按记录的内容命名 `*Recorder`（如 `JobRunRecorder`），在 Infrastructure 的 `DependencyInjection.cs` 登记。
- 提交后工作的完整写法与观察者契约见 `background-jobs` 组件随包文档（定位见[后端说明](../../backend/README.md#leistd-框架-api)）。
- 周期任务（`IRecurringJob`，`*Job`）放 Application 所属模块的 `BackgroundJobs/`；常驻消费者 `*Worker` 放所属模块的 `Workers/`。不建跨模块的顶层 `Jobs/`。

## 2. 测试

Job 的截止判据与失败传播放单元测试；批量 SQL、租户过滤与 DI 接线放 PostgreSQL 集成测试；调度器自身行为由框架覆盖，不重复测。集成测试固定 `TimeProvider` 后经 DI 直接执行 Job（配置变体复用既有数据库，只在该变体关闭自动调度），用截止前、等于、后三组数据按业务定义核对 `<` 或 `<=`（时刻按[测试规范 §2.2](./testing.md#22-通用要求)对齐微秒、留毫秒间隔），批量 SQL 后换新作用域读取；涉及租户时宿主与租户都验证。不按 Job 数量增加容器、固定等待或独立 E2E，跨进程责任变化时才扩大到真实运行闭环。
