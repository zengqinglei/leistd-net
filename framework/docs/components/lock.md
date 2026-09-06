# 分布式锁与本地锁

协调多个执行者对**同一资源**的并发访问，确保同一时刻只有一个进入临界区。统一的 `ILock` 抽象屏蔽底层实现，内存（单机）与 Redis（分布式）之间切换只改 DI 注册，不改调用代码。

## 何时使用

| 场景 | 推荐实现 |
| --- | --- |
| 多实例部署，需跨进程/跨节点互斥（如分布式定时任务、库存扣减） | `Leistd.Lock.Redis` |
| 单机部署或单元/集成测试，只需进程内互斥 | `Leistd.Lock.Memory` |
| 仅依赖抽象编写业务代码（领域服务、应用服务） | 只引用 `Leistd.Lock.Core` |

> ⚠️ 内存实现仅在**单进程内**有效，多实例部署时**不能**用它做分布式互斥（详见[注意事项](#注意事项)）。

## 设计约定：入口统一，实现由部署决定

**业务代码一律依赖 `IDistributedLock`，不因部署形态分支，也不为个别场景另起一套锁。**

内存实现同时绑定 `IDistributedLock` 是刻意为之，不是疏忽：它让"要跨实例互斥"这个意图在代码里只有一种写法，
把"这套部署到底几个副本"留给配置回答。因此：

- **多副本部署必须配置 Redis**，这是部署侧的责任。没配 Redis 就跑多副本，等于声明了自己不需要跨实例互斥；
- 单副本与集成测试用内存实现，这不是降级，而是那种部署形态下的正确答案；
- **不要因为"内存实现不跨进程"就绕开这个入口**去自建互斥（数据库 advisory lock、状态表抢占、
  另立一套锁抽象等）。那样做的结果是同一件事在系统里有两种表达方式，而部署方仍然要为多副本配 Redis——
  复杂度增加了，问题一个没少。真正的缺口应当反馈到本组件，而不是在调用侧各自绕行。

## 安装

```bash
dotnet add package Leistd.Lock.Core

dotnet add package Leistd.Lock.Redis
dotnet add package Leistd.Lock.Memory
```

## 注册

在 `Program.cs` 注册其中一种实现：

```csharp
builder.Services.AddRedisDistributedLock("localhost:6379", builder.Configuration);

builder.Services.AddMemoryLocalLock();
```

两种注册均把实现绑定到 `ILock`：`AddRedisDistributedLock` 额外绑定 `IDistributedLock`；`AddMemoryLocalLock` 额外绑定 `ILocalLock` 与 `IDistributedLock`。按需注入对应接口即可表达依赖意图。每个实现类型只注册一次，其余接口是别名转发——同一进程内拿到的是同一个实例。

内存实现首次以 `IDistributedLock` 解析时会记录 Warning，提示它不提供跨进程互斥。

## 使用

注入 `ILock`（或语义更明确的 `IDistributedLock` / `ILocalLock`），通过 `await using` 让锁在离开作用域时自动释放：

```csharp
public class OrderService(IDistributedLock distributedLock)
{
    public async Task PlaceOrderAsync(string orderKey)
    {
        await using var handle = await distributedLock.LockAsync($"order:{orderKey}");
    }

    public async Task<bool> TryPlaceOrderAsync(string orderKey)
    {
        await using var handle = await distributedLock.TryLockAsync(
            $"order:{orderKey}", TimeSpan.FromSeconds(3));
        if (handle is null) return false;
        return true;
    }
}
```

`ILockHandle` 实现 `IAsyncDisposable`，`await using` 释放时自动解锁；需要自己控制释放时机时显式 `await handle.DisposeAsync()` 即可，同样带持有者校验。

## 接口参考

`Leistd.Lock.Core` 包的 API 位于 `Leistd.Lock.Abstractions`：

| 成员 | 说明 |
| --- | --- |
| `ILock` | 锁服务统一接口，下列三个方法的定义方 |
| `ILock.LockAsync(key, ct)` | 阻塞加锁直到成功，返回 `ILockHandle`；取消时抛 `OperationCanceledException` |
| `ILock.TryLockAsync(key, timeout, ct)` | 尝试加锁，超时返回 `null`（非异常） |
| `ILockHandle.LockLost` | 持锁资格失效时被取消（租约续期失败）；长临界区应并入自己的 `CancellationToken` |
| `IDistributedLock : ILock` | 标记接口，表达"需要分布式锁"的依赖意图 |
| `ILocalLock : ILock` | 标记接口，表达"需要本地锁"的依赖意图 |
| `ILockHandle : IAsyncDisposable` | 锁句柄，`await using` 离开作用域时自动释放 |

## 实现行为

### Leistd.Lock.Memory（内存本地锁）

- 每个 key 对应一个 `SemaphoreSlim(1,1)`，按 key 互斥；`TryLockAsync` 用信号量超时等待实现。
- 随注册自动启用 `MemoryLockCleanupHostedService`：每 **1 分钟**扫描；entry 在没有持锁者或等待者时才会被原子退休并回收，空闲超过 **5 分钟**后移除，防止 key 无限增长导致内存泄漏。
- 以 Singleton 注册，进程内有效；进程重启后锁状态丢失。

### Leistd.Lock.Redis（分布式锁）

- 基于 StackExchange.Redis 的原子获取与释放操作。每次加锁生成随机 token，由 `ILockHandle` 释放时校验 token 再删除，避免误删他人持有的锁。
- 锁默认过期 **30 秒**（`RedisLockOptions.Expiry`），即使持有者崩溃也会自动释放，避免死锁。
- `LockAsync` / `TryLockAsync` 以 **50ms**（`RedisLockOptions.RetryInterval`）间隔轮询重试获取。
- **释放锁只有一条路径：句柄**（`await using` 或显式 `await handle.DisposeAsync()`），它带持有者校验，只放开自己那一把。组件不提供"按 key 强制解锁"——删掉 key 不等于上一个执行者已经停止，那个操作的后置条件与本组件的核心不变量（同一时刻只有一个执行者在临界区）直接冲突。
- **持有者卡死怎么办**：终止或隔离该实例。续期随之停止，租约到期后锁自然释放，而且这是唯一能真正让旧执行者停下来的手段。强制删 key 只会让新旧两个执行者同时以为自己独占。
- 确有理由直接操作 Redis 键时，注入已注册的 `IConnectionMultiplexer` 自行删除——让调用点如实写着"我在删一个 Redis key"，而不是借锁抽象背书。

## 配置项（`Leistd:Lock:Redis`）

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `KeyPrefix` | 空 | 锁 key 前缀。**多个应用共用一个 Redis 实例时必须设置**：没有前缀时锁 key 就是业务键，两个不相关的应用键名撞上就会互相阻塞，而两边日志各自都正常。推荐 `应用名:环境:` |
| `Expiry` | `00:00:30` | 租约时长。句柄按租约三分之一自动续期，因此本值实际决定的是「持有者进程异常终止后锁最多被占多久」 |
| `RetryInterval` | `00:00:00.05` | 抢锁失败后的重试间隔。本实现按轮询抢锁（不要求宿主开启 `notify-keyspace-events`）；高并发争抢同一把锁时应适当放大 |

`Expiry` 与 `RetryInterval` 在启动期校验；非正租约会破坏互斥，非正重试间隔会退化为忙轮询，因此均阻止宿主启动。`KeyPrefix` 为 `null` 时归一化为空串。

```json
{
  "Leistd": { "Lock": { "Redis": { "KeyPrefix": "acme-shop:prod:", "Expiry": "00:01:00" } } }
}
```

## 超时语义

两个实现对 `TryLockAsync` 的语义完全一致：

- **`TimeSpan.Zero` = 试一次、不等待**。
- 正超时内按 `RetryInterval` 轮询重试，直到拿到锁或超时。
- 计时用单调时钟（`TimeProvider.GetTimestamp/GetElapsedTime`），不受系统时钟被 NTP 步进或手工修改的影响——墙钟往前跳会让等待提前结束，往后跳会让它凭空变长。
- 取消令牌在每次尝试前检查，取消时抛 `OperationCanceledException`（不是返回 `null`——那会把"放弃等待"和"锁被占用"混成一种结果）。

## 注意事项

- `TryLockAsync` 返回 `null` 是约定的"未抢到锁"信号，**不抛异常**，调用方必须判空并处理降级。
- 内存实现也绑定了 `IDistributedLock` 接口，但其互斥范围**仅限单进程**——多实例部署中切勿将其当作分布式锁，否则不同节点会同时进入临界区。
- Redis 实现的过期时长、轮询间隔与 key 前缀由 `RedisLockOptions`（配置节 `Leistd:Lock:Redis`）给出，见下节。句柄会按租约的三分之一周期自动续期（续期时校验 token，不会误续别人的锁），因此临界区超过租约时长不再等于自动失去锁。
- **续期失败 = 失去持锁资格**，此时 `ILockHandle.LockLost` 被取消，且释放时不再删除 key（那把锁已经属于别人）。临界区里做长事务、数据迁移、批量初始化时，应把该令牌与自己的 `CancellationToken` 关联，让后续操作立即中止而不是带着幻觉继续写。进程内实现没有租约，该令牌永不取消。
