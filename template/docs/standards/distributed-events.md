# 分布式事件

跨服务的异步事实经分布式事件发布；服务内解耦使用本地事件，立即查询或执行命令使用服务客户端。事件随业务事务写入发件箱，提交后立即发送，持久扫描兜底；接收方先写收件箱再确认传输，处理与标记同事务。

## 发布与消费

契约放在共享包，只引用 `Leistd.EventBus.Core`；继承 `DistributedEvent`，用 `[EventName("order.paid")]` 声明稳定名称。需要同键顺序时实现 `IOrderedDistributedEvent`，键取聚合标识。事件元数据必须能由 JSON 还原，不能在接收时另生成标识。

在业务工作单元里调用 `IDistributedEventBus.PublishAsync`；没有工作单元会抛异常。在本地领域事件处理器里转发时声明 `[UnitOfWorkEventHandler(UnitOfWorkPhase.BeforeCommit)]`，否则本地事件默认提交后执行，已经离开业务事务。

消费者实现 `IEventHandler<T>`，经 `AddDistributedEventHandler<THandler>(consumerName)` 注册。消费者名参与去重；重命名处理器时显式保持原消费者名。处理器写本服务数据库与收件箱 Processed 标记同事务；调用外部系统的副作用仍须按 EventId 幂等。

## 顺序、重试与部署

保序范围为同租户、同事件名、同排序键；发送与处理都检查尚未完成的前序。队首退避时后续不越过；死信和 Failed 是终态，后续可以继续，业务版本水位需容忍跳号。故障重投可能迟到，不承诺全局或跨主题顺序。

默认单进程同时写入、发送、接收与处理。拆出 Worker 时，写入进程关闭 `Outbox:DispatchEnabled`、`Inbox:ReceiveEnabled`、`Inbox:ProcessingEnabled`，Worker 开启并注册传输；关闭的进程不抢兜底任务锁。不要通过是否注册传输推断进程角色。

默认使用 RabbitMQ 的持久单活队列。改用 Kafka 时，将 `AddRabbitMqEventTransport` 换成 `AddKafkaEventTransport`，健康检查同步换成 Kafka 入口，配置 `ConnectionStrings:Kafka` 并在 compose 登记 Kafka 服务、移除不再使用的 RabbitMQ。每个服务一个消费组，主题按事件名，键为排序键或 EventId；持久接收成功后才提交位点。

保留期由 EF 的 `AddDistributedEventRetention` 独立绑定，默认终态保留十四天，Pending 不删除。去重窗口等于保留期；超出窗口的外部重投应由业务约束兜底。多副本部署需使用共享分布式锁。

## 排障与管理

| 现象 | 检查与动作 |
| --- | --- |
| 待发积压 | 查看最旧时长、传输健康与连接凭据。通道故障是瞬态，修复后自动重试；超过默认一天的窗口进入死信。 |
| 死信 | 查看稳定错误码和载荷；修复消息大小、schema 或路由后单条、勾选或按当前筛选重投。EventId 不变。 |
| 处理失败 | 修复消费者后重放。重放重新获得五次处理预算；重放 Processed 会再次执行处理器。 |
| 拒收 | 仅宿主查看集中拒收，修复传输解码、租户或载荷后重新解码；不可恢复时丢弃。临时连接或注册中心故障应重投，不记成坏消息。 |
| 事件静默 | 对照事件目录的最近发送、接收、处理时刻和订阅消费者；无活动本身不代表失败，阈值由业务判断。 |

只读权限 `App.DistributedEvents` 可看状态、目录与概要；管理权限 `App.DistributedEvents.Manage` 才能读取载荷、恢复、丢弃或发送测试事件。宿主按物理库查看，租户只看自己的发件箱与收件箱。按筛选恢复带首次加载时间上限，后续新记录不卷入；操作记录逐个成功批次记录条数和筛选。

发送内置测试事件应看到同一 EventId 的 Sent 发件箱与 Processed 收件箱。外部传输不允许自发自收时关闭 `TestEvent:Enabled`，页面随之隐藏测试入口。链路使用 traceparent 与 leistd-correlation-id，可在详情消息头中核对。
