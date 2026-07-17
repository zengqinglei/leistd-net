using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Leistd.Lock.Memory.HostedServices;

/// <summary>
/// 内存锁清理后台服务
/// 定期清理长时间空闲的 Semaphore 资源，防止内存泄漏
/// </summary>
public sealed class MemoryLockCleanupHostedService : IHostedService, IDisposable
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxIdleTime = TimeSpan.FromMinutes(5);

    private readonly MemoryLocalLock _memoryLock;
    private readonly ILogger<MemoryLockCleanupHostedService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly object _lifecycleLock = new();

    private ITimer? _cleanupTimer;
    private Task? _stopTask;
    private bool _disposed;
    private int _stopping;

    public MemoryLockCleanupHostedService(
        MemoryLocalLock memoryLock,
        ILogger<MemoryLockCleanupHostedService> logger)
        : this(memoryLock, logger, TimeProvider.System)
    {
    }

    internal MemoryLockCleanupHostedService(
        MemoryLocalLock memoryLock,
        ILogger<MemoryLockCleanupHostedService> logger,
        TimeProvider timeProvider)
    {
        _memoryLock = memoryLock;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopTask is not null)
                throw new InvalidOperationException("The memory lock cleanup service cannot be restarted after it has stopped.");

            _cleanupTimer ??= _timeProvider.CreateTimer(
                static state => ((MemoryLockCleanupHostedService)state!).RunCleanup(),
                this,
                CleanupInterval,
                CleanupInterval);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => StopCore();

    internal int CleanupOnce()
    {
        if (Volatile.Read(ref _stopping) != 0) return 0;

        _logger.LogTrace("开始清理超时 Semaphore 锁...");
        var removedCount = 0;
        var now = _memoryLock.GetUtcNow();
        foreach (var (key, entry) in _memoryLock.Semaphores)
        {
            if (Volatile.Read(ref _stopping) != 0) break;

            if (entry.TryRetire(now, MaxIdleTime) && _memoryLock.TryRemove(key, entry))
            {
                removedCount++;
                _logger.LogTrace("清理超时 Semaphore 锁【{Key}】", key);
            }
        }
        _logger.LogTrace("清理超时 Semaphore 锁完成");
        return removedCount;
    }

    private void RunCleanup()
    {
        try
        {
            CleanupOnce();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "清理超时 Semaphore 锁失败");
        }
    }

    private Task StopCore()
    {
        Interlocked.Exchange(ref _stopping, 1);

        lock (_lifecycleLock)
        {
            if (_stopTask is not null) return _stopTask;

            var timer = _cleanupTimer;
            _cleanupTimer = null;
            _stopTask = timer is null
                ? Task.CompletedTask
                : timer.DisposeAsync().AsTask();
            return _stopTask;
        }
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            _disposed = true;
        }

        // System timers run callbacks on the thread pool, so synchronously waiting here cannot
        // capture a request synchronization context and ensures dependent singletons stay alive.
        StopCore().GetAwaiter().GetResult();
    }
}
