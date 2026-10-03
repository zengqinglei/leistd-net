# 合并上游后的时间与定时任务测试审视

当前候选合入 develop `422fa3ae`，merge `ae808f56`。这份评估提出后续候选，尚未开发新增 Job 用例；方案选定后将责任上收到稳定规范并删除此评估。原输入选测实现与历史 CI 数据见[验证报告](../reports/2026-10-03-quality-scenario-selection.md)。

## 上游优化与责任

上游使用官方 `FakeTimeProvider`，让 DDD `IClock` 跟随 DI 的 `TimeProvider`；通知邮件检查从固定等待改为有上限轮询。访问令牌默认寿命仍为 10 分钟，真实 OIDC 到期验证使用 90 秒快速档，不缩短生产默认配置。PR 省略两条真实到期等待，full 传 `-IncludeExpiryWait` 补跑，与本分支的档位和候选选择兼容。

修改令牌寿命、刷新或到期判据时，作者提交前需显式跑真实到期入口，责任已写入[模板质量规范](../template/quality-assurance.md)。合并前 `762bdd61` 的 full 不能证明上游新增到期责任；合并后的候选须有自己的 full，结果在 MR 记录。

## 按层划分

| 层/任务 | 现有测试 | 审视结论 |
| --- | --- | --- |
| Framework 排期 | 显式 UTC 时刻，7 条规则测试 | 已合理，无真实日/分钟等待；保留纯单元测试 |
| Framework 单次执行 | 真实 DI、内存锁、假时钟，5 条执行测试 | 锁、水位、失败不记水位、每副本等已覆盖；不能以业务集成替代 |
| Framework 调度循环 | 缺锁启动失败、缺调度器告警；未执行实际循环 | 优先补禁用、指定任务禁用、非法排期隔离、到点/下一轮、停止后不再执行；无需数据库 |
| Framework EF 水位 | SQLite 内存的 UTC 往返和顺序推进 | 快关系型验证有价值；顺序测试不代表并发写原子性。真实 provider 接线可复用业务宿主，不为每个 Job 新起容器 |
| Framework 通知保留/操作记录归档 | SQLite 共享/独立租户库，固定时刻，直接执行 | 已无保留期等待；保留查询、租户、失败、Options 与 DI 责任 |
| Template 操作记录归档 | `OperationRecordArchiveTests` 已在真实 PostgreSQL 同时造宿主与租户行 | 已证明 provider 与过滤器行为，无需重复补同一责任 |
| Template 通知保留 | 默认登记；未找到 PostgreSQL 保留期用例 | 优先在现有 fixture 补薄层：宿主/租户行、截止时间、过滤器和生产 provider 接线 |
| Template 过期会话清理 | 真实 PostgreSQL，直接执行，保护活跃/共享租户会话 | 精确截止边界需要 SQL/领域判据一致；失败库/未解析租户的传播是编排，可假 `ITenantDatabaseRunner` 单测 |
| Template OpenIddict 清理 | 真实 manager/PG，旧/新/有效数据与批量删除后新 scope；独立协议探针 | 14 天阈值、调用顺序可单测；状态、引用、真实 SQL 保留集成，不能因 Job 名相同认定重复 |
| 宿主设置刷新 | EveryInstance 登记已有断言；实现转调 applier | 不新增只镜像转调的测试；可作为调度循环的典型形态 |
| 生成的业务项目自己的 Job | 按项目实际输入与风险选择 | 截止/排期规则单测，SQL/事务/租户/DI 集成；改变调度接线才补轻量宿主验证，不给每个 Job 跑整套协议 E2E |

调度测试使用假时钟与完成信号，不等待真实分钟。[.NET 10 的 ExecuteAsync 整体在后台执行](https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/backgroundservice-executeasync-task)，必须观察计时器已登记再推进，否则可能按推进后的时间计算下一轮。Daily 排期抖动为 0，适合精确到点断言；间隔排期首次有 `Random.Shared` 抖动，测试必须处理它，不能假定第一次固定在间隔边界。

会话边界选择可被微秒精确表示的固定 UTC 时刻，再用截止点及两侧（例如 ±1ms）对照。±1ms 是测试余量；[PostgreSQL timestamp 精度为 1 微秒](https://www.postgresql.org/docs/current/datatype-datetime.html)，不能称数据库精度为 1ms。

## 等待类逻辑

- `DataFilterTests` 的 20/10ms Sleep、官方客户端的 40ms Delay 用于制造并发重叠。若有偶发失败证据，可改双向完成信号；必须证明仍真实重叠，不能直接删除等待。节省仅几十毫秒，不是 CI 主收益。
- 通知邮件已有最多 5 秒、100ms 间隔的结果轮询，收到即返回。等待的是实际异步完成；信号化可降低抖动，假时钟不替代邮件到达。
- HTTP 超时测试的 5 秒延迟可由取消提前结束，不是每次必等 5 秒。HybridCache 真实 TTL 与应用规则时钟不同，保留一条真实到期验证。内存锁自定义 Timer 用于回调/异步 Dispose 竞态，不能直接换成普通假时钟。

规则时间按[官方 FakeTimeProvider](https://learn.microsoft.com/en-us/dotnet/core/extensions/timeprovider-testing)推进；真实数据库行为遵循[EF Core 的测试策略](https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy)。不能因为已有一条集成测试，就删除职责不同的调度/失败单测；也不为每个纯规则新建数据库宿主。

## 验证与后续候选

合并后本地结果：BackgroundJobs 整项目 31 通过/0 跳过，测试体 789ms；ClockRegistration 3 条、NotificationRetention 4 条、OperationRecordRetention 8 条全部通过/0 跳过，完整入口分别 11.071/8.724/6.948 秒，测试体分别 28ms/约1s/约1s。完整入口包括 restore/build/启动，单机串行单次，不是优化前后成对样本，不计算改善率。首次 Clock 无 restore 因新增依赖未刷新 assets 失败；恢复依赖后通过。

建议优先级与验收：

1. 调度循环假时钟测试。分别注入漏判 Enabled、DisabledJobs 失效、非法排期拖垮其他循环、停止后仍执行等缺陷，测试须失败；正常到点、未到点、下一轮、取消均有确定的信号与超时。
2. 通知保留 PostgreSQL 接线，复用现有 fixture，同测宿主/租户与截止边界，删除过滤器等缺陷须变红。
3. 会话清理精确 SQL 边界及失败传播，后者用单测；不为纯编排新增 PG 启动。
4. 并发窗口与轮询信号化，仅在偶发失败证据支持时做。

前三项是补质量缺口，会增加少量测试体耗时；未开发和成对测量前不承诺具体增量或 CI 提速。独立只读复核在核对范围内指出调度缺口和上游兼容性，以及操作记录 PG 现有覆盖、通知 PG 缺口与时间精度，已核对并采纳；并发窗口的优先级降低。缓存/Timer 抽样不代表完整复核。
