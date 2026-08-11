namespace Leistd.Lock.Core;

/// <summary>
/// 锁句柄，使用完毕后释放锁（对应 Java ILock AutoCloseable）
/// </summary>
public interface ILockHandle : IAsyncDisposable
{
    /// <summary>
    /// 持锁资格失效时被取消。
    /// </summary>
    /// <remarks>
    /// 基于租约的实现（如 Redis）无法保证"拿到锁 = 一直持有锁"：租约到期而续期又失败时，
    /// 另一个实例可以合法进入临界区，而当前进程对此一无所知，仍会继续写。
    /// 临界区里做长事务、数据迁移、批量初始化这类操作时，应把本令牌与自己的
    /// <see cref="CancellationToken"/> 关联，使失去锁之后的操作立即中止而不是带着幻觉写下去。
    ///
    /// 进程内实现不存在租约，本令牌在释放前永不取消。
    /// </remarks>
    CancellationToken LockLost { get; }
}
