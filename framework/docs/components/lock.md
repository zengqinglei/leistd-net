# 分布式/本地锁（`lock`）
> 统一的加锁抽象 `ILock`，可在内存（单机）与 Redis（分布式）实现间按 DI 注册切换。

## 包

| 包 | 角色 | 何时引用 |
| --- | --- | --- |
| `Leistd.Lock.Core` | 锁抽象接口与句柄定义 | 业务代码与所有实现包都引用 |
| `Leistd.Lock.Memory` | 基于 `SemaphoreSlim` 的内存本地锁实现 | 单机部署、集成测试场景 |
| `Leistd.Lock.Redis` | 基于 StackExchange.Redis 的分布式锁实现 | 多实例部署需要跨进程互斥时 |

## 核心抽象

命名空间均为 `Leistd.Lock.Core`。

```csharp
public interface ILock
```
锁服务统一接口（对应 Java `IDistributedLock` / `ILocalLock`），实现可为分布式或本地，由 DI 注册决定。

```csharp
Task<ILockHandle> LockAsync(string key, CancellationToken cancellationToken = default);
```
阻塞加锁直到成功，返回锁句柄；取消令牌触发时抛出取消异常。

```csharp
Task<ILockHandle?> TryLockAsync(string key, TimeSpan timeout, CancellationToken cancellationToken = default);
```
尝试加锁，超时仍未获取则返回 `null`（非异常，调用方自行降级）。

```csharp
Task UnlockAsync(string key, CancellationToken cancellationToken = default);
```
显式解锁，用于异常兜底；正常情况下应通过 `ILockHandle` 释放，无需手动调用。

```csharp
public interface IDistributedLock : ILock
public interface ILocalLock : ILock
```
两个标记接口，分别表达"需要分布式锁 / 本地锁"的依赖意图，本身不新增成员。

```csharp
public interface ILockHandle : IAsyncDisposable
```
锁句柄，`await using` 离开作用域时自动释放锁（对应 Java `AutoCloseable`）。

## 能力实现

### `Leistd.Lock.Memory`
注册扩展：`services.AddMemoryLocalLock()`。

- 以 Singleton 注册 `MemoryLocalLock`，同时绑定到 `ILocalLock`、`ILock`、`IDistributedLock`（注意：内存实现也满足 `IDistributedLock` 接口，但仅在单进程内有效）。
- 每个 key 对应一个 `SemaphoreSlim(1,1)`，按 key 互斥；`TryLockAsync` 用信号量的超时等待实现。
- 同时注册 `MemoryLockCleanupHostedService`：每 1 分钟扫描，回收空闲超过 5 分钟（`CurrentCount == 1`）的信号量，防止内存泄漏。
- 仅适用于单机/测试，跨进程不提供互斥保证。

### `Leistd.Lock.Redis`
注册扩展：`services.AddRedisDistributedLock(connectionString)`。

- 以 Singleton 注册 `RedisDistributedLock`，绑定到 `IDistributedLock` 与 `ILock`；若未注册 `IConnectionMultiplexer`，会用传入连接串 `TryAddSingleton` 创建一个。
- 基于 StackExchange.Redis 的 `LockTake` / `LockRelease`（内部为 SET NX + Lua 原子校验删除），每次加锁生成随机 token，释放时校验 token 后删除。
- 锁默认过期 30 秒（`DefaultLockExpiry`），`LockAsync` / `TryLockAsync` 以 50ms 轮询间隔重试获取。
- `UnlockAsync` 直接 `KeyDelete`，不校验 token，仅用于异常兜底。

## 最小可用示例

```csharp
using Leistd.Lock.Core;
using Leistd.Lock.Redis;       // 或 Leistd.Lock.Memory
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLogging();

// 分布式锁
services.AddRedisDistributedLock("localhost:6379");
// 单机/测试：services.AddMemoryLocalLock();

var provider = services.BuildServiceProvider();
var @lock = provider.GetRequiredService<IDistributedLock>();

// 阻塞加锁，离开作用域自动释放
await using (await @lock.LockAsync("order:1001"))
{
    // 临界区
}

// 尝试加锁，3 秒拿不到则降级
await using (var handle = await @lock.TryLockAsync("order:1001", TimeSpan.FromSeconds(3)))
{
    if (handle is null) return; // 锁被占用
    // 临界区
}
```

## 依赖
无（仅依赖 .NET 运行时与 `Microsoft.Extensions.*`、StackExchange.Redis 等第三方包，不依赖其它 Leistd 组件）。

## 备注
- 接口注释明确对应 Java 侧的 `IDistributedLock` / `ILocalLock` / `AutoCloseable`。
- `TryLockAsync` 返回 `null` 是约定的"未抢到锁"信号，不会抛异常，调用方必须判空。
- Redis 实现的默认锁过期为 30 秒、轮询间隔 50ms，均为源码硬编码常量，当前未提供 Options 配置。
- 内存实现绑定了 `IDistributedLock`，但其互斥范围仅限单进程，切勿在多实例部署中将其当作分布式锁使用。
