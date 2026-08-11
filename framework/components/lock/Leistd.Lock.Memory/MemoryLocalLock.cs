using Leistd.Lock.Core;
using Leistd.Lock.Memory.Entry;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Leistd.Lock.Memory;

/// <summary>
/// 内存本地锁实现
/// 使用 SemaphoreSlim(1,1) per key，适用于单机/测试场景
/// </summary>
public sealed class MemoryLocalLock : ILocalLock, IDistributedLock, IDisposable
{
    private readonly ILogger<MemoryLocalLock> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, SemaphoreEntry> _semaphores = new();

    internal ConcurrentDictionary<string, SemaphoreEntry> Semaphores => _semaphores;

    public MemoryLocalLock(ILogger<MemoryLocalLock> logger)
        : this(logger, TimeProvider.System)
    {
    }

    internal MemoryLocalLock(ILogger<MemoryLocalLock> logger, TimeProvider timeProvider)
    {
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<ILockHandle> LockAsync(string key, CancellationToken cancellationToken = default)
    {
        _logger.LogTrace("开始加锁【{Key}】...", key);
        var entry = AcquireEntryLease(key);
        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            entry.AbandonLease();
            throw;
        }

        _logger.LogTrace("加锁【{Key}】成功", key);
        return new MemoryLockHandle(key, entry, this);
    }

    public async Task<ILockHandle?> TryLockAsync(string key, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        _logger.LogTrace("开始尝试加锁【{Key}】...", key);
        var entry = AcquireEntryLease(key);
        bool acquired;
        try
        {
            acquired = await entry.Semaphore.WaitAsync(timeout, cancellationToken);
        }
        catch
        {
            entry.AbandonLease();
            throw;
        }

        if (!acquired)
        {
            entry.AbandonLease();
            _logger.LogDebug("尝试加锁【{Key}】失败：超时", key);
            return null;
        }

        _logger.LogTrace("尝试加锁【{Key}】成功", key);
        return new MemoryLockHandle(key, entry, this);
    }

    internal void Release(string key, SemaphoreEntry entry)
    {
        if (entry.ReleaseLease(_timeProvider.GetUtcNow()))
            _logger.LogTrace("解锁【{Key}】成功", key);
    }

    internal bool TryRemove(string key, SemaphoreEntry entry)
    {
        if (!_semaphores.TryRemove(new KeyValuePair<string, SemaphoreEntry>(key, entry)))
            return false;

        entry.Dispose();
        return true;
    }

    internal DateTimeOffset GetUtcNow() => _timeProvider.GetUtcNow();

    private SemaphoreEntry AcquireEntryLease(string key)
    {
        while (true)
        {
            var entry = _semaphores.GetOrAdd(key, _ => new SemaphoreEntry(_timeProvider.GetUtcNow()));
            if (entry.TryAcquireLease())
                return entry;

            // A retired entry may still be visible briefly between retirement and dictionary removal.
            TryRemove(key, entry);
        }
    }

    public void Dispose()
    {
        foreach (var (key, entry) in _semaphores)
            TryRemove(key, entry);
    }


}
