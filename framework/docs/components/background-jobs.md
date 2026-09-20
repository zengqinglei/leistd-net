# 后台作业：周期任务与进程内队列

周期任务按对齐到 UTC 的时段执行，登记时显式选择"全集群一份"或"每副本一份"；进程内队列把不该占着请求的工作挪到后台，并带上入队时的上下文。

## 何时使用

| 场景 | 用法 |
| --- | --- |
| 定期清理、归档、扫描共享数据，多副本只该跑一份 | `AddRecurringJob<TJob>(name, schedule, RecurringJobScope.Cluster)` |
| 定期刷新本进程内状态（缓存、配置），每个副本都要跑 | `RecurringJobScope.EveryInstance` |
| 组件自带维护任务 | 组件在自己的 `Add*` 里登记，宿主只注册调度器 |
| 请求里触发、丢了也只是少做一次的工作（非关键通知、缓存预热） | 注入 `IBackgroundTaskQueue` 入队 |
| 必须完成、失败要重试的作业 | 不用本组件的队列：与业务同事务写库，或引入持久化作业调度器 |

## 安装

```bash
# 契约：组件声明任务只需要它
dotnet add package Leistd.BackgroundJobs.Core

# 进程内调度器与队列
dotnet add package Leistd.BackgroundJobs.InProcess

# 多副本共享的完成水位
dotnet add package Leistd.BackgroundJobs.EntityFrameworkCore
```

## 注册

```csharp
builder.Services.AddRedisDistributedLock(redisConnectionString);   // 集群任务需要分布式锁
builder.Services.AddInProcessBackgroundJobs();
builder.Services.AddBackgroundJobsEfCore<AppDbContext>();          // 多副本部署

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ConfigureBackgroundJobs();
}
```

## 使用

```csharp
public sealed class LeadRecycleJob(LeadService leads) : IRecurringJob
{
    public Task ExecuteAsync(RecurringJobContext context, CancellationToken cancellationToken)
        => leads.RecycleStaleAsync(cancellationToken);
}

builder.Services.AddRecurringJob<LeadRecycleJob>(
    "crm.lead-recycle",
    RecurringJobSchedule.DailyAt(new TimeOnly(18, 0)),
    RecurringJobScope.Cluster);
```

排期来自选项时用工厂重载，调度器启动时取一次：

```csharp
builder.Services.AddRecurringJob<LeadRecycleJob>(
    "crm.lead-recycle",
    sp => RecurringJobSchedule.DailyAt(sp.GetRequiredService<IOptions<LeadOptions>>().Value.RecycleAt),
    RecurringJobScope.Cluster);
```

入队工作项拿到的是执行时新建作用域的服务提供器：

```csharp
if (!queue.TryQueue((services, ct) => services.GetRequiredService<WelcomeMailer>().SendAsync(userId, ct)))
{
    logger.LogWarning("The background queue is full; the welcome mail for {UserId} was dropped.", userId);
}
```

## 接口参考

| 成员 | 说明 |
| --- | --- |
| `IRecurringJob.ExecuteAsync(context, ct)` | 执行一次；每次在新作用域里解析实例，令牌在停机或失去集群锁时取消 |
| `RecurringJobContext` | `JobName` 与所属时段起点 `Slot` |
| `RecurringJobSchedule.Every(interval)` / `DailyAt(timeOfDayUtc)` | 排期；时段按 UTC 对齐，间隔不小于 1 秒 |
| `RecurringJobScope` | `Cluster`（锁 + 水位，全集群一份）/ `EveryInstance`（每副本一份），没有默认值 |
| `AddRecurringJob<TJob>(name, schedule \| scheduleFactory, scope)` | Core 包：登记任务；同名同类型幂等，同名不同类型抛出 |
| `IRecurringJobStateStore` | 集群任务的完成水位 |
| `IBackgroundTaskQueue.QueueAsync` / `TryQueue` | 入队；满时等待或返回 `false` |
| `AddInProcessBackgroundJobs(configure?)` | InProcess 包：注册调度器、队列与进程内水位；幂等 |
| `AddBackgroundJobsEfCore<TDbContext>()` / `ConfigureBackgroundJobs(modelBuilder)` | EF 包：共享水位存储，与注册顺序无关地替换进程内实现 |

## 配置项（Leistd:BackgroundJobs）

| 键 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `Enabled` | `bool` | `true` | 本进程是否执行周期任务；一次性进程或只让部分副本跑任务时关掉，队列不受影响 |
| `DisabledJobs` | `string[]` | 空 | 本进程停用的任务名 |
| `InProcess:QueueCapacity` | `int` | `256` | 队列容量；满了就让生产者慢下来 |

## 实现行为

- **时段对齐 UTC。** `Every` 从 UTC 纪元按间隔切分，`DailyAt` 按每天的 UTC 时刻切分。不同副本、重启前后算出的是同一个时段，执行时间也不随重启漂移。
- **集群任务 = 零等待抢锁 + 水位比对。** 抢不到锁即跳过本次；拿到锁后若水位显示本时段已完成也跳过。只加锁挡不住"副本 A 做完、时钟稍慢的副本 B 在同一时段又做一遍"。锁丢失时取消传给任务的令牌。
- **失败不记水位。** 异常只记日志，本轮不重试：同一时段内若还有触发（短周期任务、或另一个尚未试过的副本）会再做一次，否则**最迟在下一个调度时段重做**——每日任务通常就是第二天。因此任务必须幂等，并按截止时间扫描历史积压，而不是只处理"今天到期的那一批"。要分钟级恢复就把该任务排成短周期，不要指望失败重试。
- **启动即校验。** 登记了集群任务却没有 `IDistributedLock` 时宿主启动失败，不静默退化成每副本执行。
- **首次执行。** 按间隔排期的任务启动后在不超过 30 秒（或一个间隔）的随机延迟内先执行一次；按每日时刻排期的只在排定时刻执行。
- **队列上下文。** 入队时经 `IAmbientContext.Capture()` 捕获主体、租户与链路标识，执行时还原；工作单元与请求对象不随行。未注册环境上下文时工作项在空上下文里执行。
- **停机。** 队列先关写入口；正在执行的工作项收到取消令牌，尚未开始的项被丢弃。

## 注意事项

- **多副本部署要注册共享水位**（`AddBackgroundJobsEfCore`）与 Redis 分布式锁；进程内水位与内存锁都只对单副本成立。
- **任务执行时没有请求主体与租户。** 要逐个租户库处理时用多租户组件的 `ITenantDatabaseRunner`。
- **队列不持久。** 进程退出时未执行的工作项丢失，`TryQueue` 的返回值必须处理。
- 任务名同时是锁与水位的键，发布后改名等于让新名字从零开始、旧水位作废。

## 相关

- [分布式锁与本地锁](./lock.md)
- [多租户](./multi-tenancy.md)
