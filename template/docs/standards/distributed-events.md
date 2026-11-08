# 分布式事件

跨服务的异步事实经分布式事件发布；服务内解耦使用本地事件，立即查询或执行命令使用服务客户端。事件随业务事务写入发件箱，提交后立即发送，持久扫描兜底；接收方先写收件箱再确认传输，处理与标记同事务。

## 发布与消费

契约放在共享包，只引用 `Leistd.EventBus.Core`；继承 `DistributedEvent`，用 `[EventName("order.paid")]` 声明稳定名称。需要同键顺序时实现 `IOrderedDistributedEvent`，键取聚合标识。事件元数据必须能由 JSON 还原，不能在接收时另生成标识。

在业务工作单元里调用 `IDistributedEventBus.PublishAsync`；没有工作单元会抛异常。在本地领域事件处理器里转发时声明 `[UnitOfWorkEventHandler(UnitOfWorkPhase.BeforeCommit)]`，否则本地事件默认提交后执行，已经离开业务事务。

确定性 EventId 只代表同一事实；同一工作单元内相同 EventId、事件名和冻结载荷幂等，内容冲突立即报错，跨事务仍由唯一约束拒绝，因此只在产生新事实（新版本）时发布。

消费者实现 `IEventHandler<T>`，经 `AddDistributedEventHandler<THandler>(consumerName)` 注册。消费者名参与去重；重命名处理器时显式保持原消费者名。处理器写本服务数据库与收件箱 Processed 标记同事务；调用外部系统的副作用仍须按 EventId 幂等。

## 顺序、重试与部署

保序范围为同租户、同事件名、同排序键；发送与处理都检查尚未完成的前序。队首退避时后续不越过；死信和 Failed 是终态，后续可以继续，业务版本水位需容忍跳号。故障重投可能迟到，不承诺全局或跨主题顺序。

跨事件名不保序；多个事件共享版本序列且新状态完整覆盖旧状态时，可按租户与聚合维护共同水位忽略旧版本，增量事实不能仅凭该水位丢弃。完整契约见随包事件总线文档的“分布式一致性与顺序”。

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

## 连接与部署

默认组合 RabbitMQ，生产连接来自 `ConnectionStrings:RabbitMQ`（compose 的 `RABBITMQ_CONNECTION_STRING`），主机名为 `rabbitmq`；broker 初始账号来自 `RABBITMQ_USER`、`RABBITMQ_PASSWORD`，URI 账号与之保持一致，特殊字符须进行 URI 编码。已存在的数据卷不会因修改初始账号变量而更改 broker 账号。生产不使用 guest 账号；本机开发档的公开默认值只用于环回地址。

开发依赖清单同时启动 RabbitMQ，管理页为 `http://localhost:15672`。持久队列使用 RabbitMQ 数据卷，重建容器不应删除该卷；消费方必须先启动以声明队列，此前发布且没有目标队列的消息不会保留。readiness 使用本进程传输健康检查，运维快照读取宿主库的新鲜健康上报，broker 不可达会使就绪检查失败。迁移作业只创建事件表，不连接 broker。

改用 Kafka 时，在 Api 的服务与健康检查两处将 `AddRabbitMqEventTransport` 换为 `AddKafkaEventTransport`，替换包引用与连接配置，并独立准备 broker；不要同时注册两个传输。`Leistd:EventBus:Distributed` 与 `Leistd:EventBus:Distributed:Retention` 的调优项沿用组件默认值，确有部署需要时再覆盖。

恢复操作逐个已提交批次留痕，目标名称显示条数与选择条件摘要；超过操作记录名称长度的完整筛选或实际 ID 列表通过相同关联标识的结构化日志回查。失败尝试由端点动作元数据补记。
