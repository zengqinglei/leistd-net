using Leistd.Lock.Core;
using Leistd.Lock.Memory.Entry;

namespace Leistd.Lock.Memory;

/// <summary>
/// 内存锁句柄，释放时归还信号量
/// </summary>
internal sealed class MemoryLockHandle(
    string key,
    SemaphoreEntry entry,
    MemoryLocalLock owner) : ILockHandle
{
    private int _disposed;

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            owner.Release(key, entry);

        return ValueTask.CompletedTask;
    }
}
