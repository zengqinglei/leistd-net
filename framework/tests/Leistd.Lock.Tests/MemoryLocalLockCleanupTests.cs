using Leistd.Lock.Memory;
using Leistd.Lock.Memory.HostedServices;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Leistd.Lock.Tests;

public sealed class MemoryLocalLockCleanupTests
{
    private const string Key = "cleanup-race";

    [Fact]
    public async Task CleanupDoesNotRemoveEntryWhileItHasAnActiveLease()
    {
        var timeProvider = new ManualTimeProvider();
        using var memoryLock = CreateLock(timeProvider);
        using var cleanup = CreateCleanup(memoryLock);

        await using (await memoryLock.LockAsync(Key))
        {
        }

        var originalEntry = memoryLock.Semaphores[Key];
        timeProvider.Advance(TimeSpan.FromMinutes(6));

        await using var heldLock = await memoryLock.LockAsync(Key);

        Assert.Equal(0, cleanup.CleanupOnce());
        Assert.Same(originalEntry, memoryLock.Semaphores[Key]);
        Assert.Null(await memoryLock.TryLockAsync(Key, TimeSpan.Zero));
    }

    [Fact]
    public async Task AcquisitionRetriesWhenRetiredEntryIsStillVisible()
    {
        var timeProvider = new ManualTimeProvider();
        using var memoryLock = CreateLock(timeProvider);

        await using (await memoryLock.LockAsync(Key))
        {
        }

        var retiredEntry = memoryLock.Semaphores[Key];
        timeProvider.Advance(TimeSpan.FromMinutes(6));
        Assert.True(retiredEntry.TryRetire(timeProvider.GetUtcNow(), TimeSpan.FromMinutes(5)));

        await using var handle = await memoryLock.LockAsync(Key);

        Assert.NotSame(retiredEntry, memoryLock.Semaphores[Key]);
        Assert.Null(await memoryLock.TryLockAsync(Key, TimeSpan.Zero));
    }

    [Fact]
    public async Task OldHandleDoesNotReleaseReplacementEntryAndIsIdempotent()
    {
        var timeProvider = new ManualTimeProvider();
        using var memoryLock = CreateLock(timeProvider);

        var oldHandle = await memoryLock.LockAsync(Key);
        var oldEntry = memoryLock.Semaphores[Key];
        Assert.True(memoryLock.Semaphores.TryRemove(
            new KeyValuePair<string, Leistd.Lock.Memory.Entry.SemaphoreEntry>(Key, oldEntry)));

        await using var replacementHandle = await memoryLock.LockAsync(Key);

        await oldHandle.DisposeAsync();
        await oldHandle.DisposeAsync();

        Assert.Null(await memoryLock.TryLockAsync(Key, TimeSpan.Zero));
        oldEntry.Dispose();
    }

    [Fact]
    public async Task CleanupRemovesEntryOnlyAfterItBecomesIdleAgain()
    {
        var timeProvider = new ManualTimeProvider();
        using var memoryLock = CreateLock(timeProvider);
        using var cleanup = CreateCleanup(memoryLock);

        var handle = await memoryLock.LockAsync(Key);
        timeProvider.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(0, cleanup.CleanupOnce());

        await handle.DisposeAsync();
        Assert.Equal(0, cleanup.CleanupOnce());

        timeProvider.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(1, cleanup.CleanupOnce());
        Assert.False(memoryLock.Semaphores.ContainsKey(Key));
    }

    [Fact]
    public async Task TimedOutAndCanceledWaitersReleaseTheirEntryLeases()
    {
        var timeProvider = new ManualTimeProvider();
        using var memoryLock = CreateLock(timeProvider);
        using var cleanup = CreateCleanup(memoryLock);
        var heldLock = await memoryLock.LockAsync(Key);

        Assert.Null(await memoryLock.TryLockAsync(Key, TimeSpan.Zero));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => memoryLock.LockAsync(Key, new CancellationToken(canceled: true)));

        await heldLock.DisposeAsync();
        timeProvider.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(1, cleanup.CleanupOnce());
        Assert.False(memoryLock.Semaphores.ContainsKey(Key));
    }

    private static MemoryLocalLock CreateLock(TimeProvider timeProvider) =>
        new(NullLogger<MemoryLocalLock>.Instance, timeProvider);

    private static MemoryLockCleanupHostedService CreateCleanup(MemoryLocalLock memoryLock) =>
        new(memoryLock, NullLogger<MemoryLockCleanupHostedService>.Instance);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        internal void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
