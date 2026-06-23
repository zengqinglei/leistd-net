# 分布式锁与本地锁

分布式锁用于协调多个应用实例对**同一资源**的并发访问，确保同一时刻只有一个执行者进入临界区。典型场景包括：防止定时任务在多副本下重复执行、避免并发下单超卖、保护需要串行化的状态更新等。

Leistd 通过统一的 `ILock` 抽象屏蔽底层实现，使业务代码无需关心锁是基于内存（单机）还是 Redis（分布式）——切换实现只需更换 DI 注册，不改调用代码。

## 何时使用

| 场景 | 推荐实现 |
| --- | --- |
| 多实例部署，需跨进程/跨节点互斥（如分布式定时任务、库存扣减） | `Leistd.Lock.Redis` |
| 单机部署或单元/集成测试，只需进程内互斥 | `Leistd.Lock.Memory` |
| 仅依赖抽象编写业务代码（领域服务、应用服务） | 只引用 `Leistd.Lock.Core` |

> ⚠️ 内存实现仅在**单进程内**有效，多实例部署时**不能**用它做分布式互斥（详见[注意事项](#注意事项)）。

## 安装

```bash
# 抽象（业务代码引用；实现包已传递引用，通常无需单独添加）
dotnet add package Leistd.Lock.Core

# 二选一：分布式（Redis） 或 本地（内存）
dotnet add package Leistd.Lock.Redis
dotnet add package Leistd.Lock.Memory
```

> 本仓库的模板项目通过中央包管理（CPM）统一版本，添加时无需写版本号。

## 配置 Provider

在 `Program.cs` 注册其中一种实现：

```csharp
// 分布式锁（Redis）——传入连接串，内部按需创建 IConnectionMultiplexer
builder.Services.AddRedisDistributedLock("localhost:6379");

// 或：本地内存锁（单机/测试）——同时注册后台清理服务
builder.Services.AddMemoryLocalLock();
```

两种注册均把实现绑定到 `ILock`：`AddRedisDistributedLock` 额外绑定 `IDistributedLock`；`AddMemoryLocalLock` 额外绑定 `ILocalLock` 与 `IDistributedLock`。按需注入对应接口即可表达依赖意图。

## 使用

注入 `ILock`（或语义更明确的 `IDistributedLock` / `ILocalLock`），通过 `await using` 让锁在离开作用域时自动释放：

```csharp
public class OrderService(IDistributedLock distributedLock)
{
    // 阻塞加锁：直到拿到锁为止
    public async Task PlaceOrderAsync(string orderKey)
    {
        await using var handle = await distributedLock.LockAsync($"order:{orderKey}");
        // —— 临界区：同一 key 全局串行 ——
    }

    // 尝试加锁：超时拿不到则返回 null，调用方自行降级（不抛异常）
    public async Task<bool> TryPlaceOrderAsync(string orderKey)
    {
        await using var handle = await distributedLock.TryLockAsync(
            $"order:{orderKey}", TimeSpan.FromSeconds(3));
        if (handle is null) return false; // 锁被占用，降级处理
        // —— 临界区 ——
        return true;
    }
}
```

`ILockHandle` 实现 `IAsyncDisposable`，`await using` 释放时自动解锁，正常路径无需手动调用 `UnlockAsync`。

## 接口参考

`Leistd.Lock.Core` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `ILock` | 锁服务统一接口，下列三个方法的定义方 |
| `ILock.LockAsync(key, ct)` | 阻塞加锁直到成功，返回 `ILockHandle`；取消时抛 `OperationCanceledException` |
| `ILock.TryLockAsync(key, timeout, ct)` | 尝试加锁，超时返回 `null`（非异常） |
| `ILock.UnlockAsync(key, ct)` | 显式解锁，异常兜底用；常态由句柄自动释放 |
| `IDistributedLock : ILock` | 标记接口，表达"需要分布式锁"的依赖意图 |
| `ILocalLock : ILock` | 标记接口，表达"需要本地锁"的依赖意图 |
| `ILockHandle : IAsyncDisposable` | 锁句柄，`await using` 离开作用域时自动释放 |

## 实现行为

### Leistd.Lock.Memory（内存本地锁）

- 每个 key 对应一个 `SemaphoreSlim(1,1)`，按 key 互斥；`TryLockAsync` 用信号量超时等待实现。
- 随注册自动启用 `MemoryLockCleanupHostedService`：每 **1 分钟**扫描，回收空闲超过 **5 分钟**的信号量，防止 key 无限增长导致内存泄漏。
- 以 Singleton 注册，进程内有效；进程重启后锁状态丢失。

### Leistd.Lock.Redis（分布式锁）

- 基于 StackExchange.Redis 的 `LockTake` / `LockRelease`（SET NX + Lua 原子校验删除）。每次加锁生成随机 token，由 `ILockHandle` 释放时校验 token 再删除，避免误删他人持有的锁。
- 锁默认过期 **30 秒**（`DefaultLockExpiry`，硬编码），即使持有者崩溃也会自动释放，避免死锁。
- `LockAsync` / `TryLockAsync` 以 **50ms**（`PollInterval`）间隔轮询重试获取。
- `UnlockAsync` 为异常兜底接口，直接 `KeyDelete` **不校验 token**；正常释放请依赖 `await using` 触发句柄的带 token 校验释放。

## 注意事项

- `TryLockAsync` 返回 `null` 是约定的"未抢到锁"信号，**不抛异常**，调用方必须判空并处理降级。
- 内存实现也绑定了 `IDistributedLock` 接口，但其互斥范围**仅限单进程**——多实例部署中切勿将其当作分布式锁，否则不同节点会同时进入临界区。
- Redis 实现的 30 秒过期、50ms 轮询为源码硬编码常量，当前**未提供 Options 配置**；若临界区耗时可能超过 30 秒，需评估锁过期风险。
- 接口注释标注了对应的 Java 侧 `IDistributedLock` / `ILocalLock` / `AutoCloseable`，便于跨语言对照。

## 相关

- [组件总览](./README.md)
- [依赖注入](./dependency-injection.md)
