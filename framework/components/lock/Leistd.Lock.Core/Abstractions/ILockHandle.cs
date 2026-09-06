namespace Leistd.Lock.Abstractions;

/// <summary>
/// 锁句柄，使用完毕后释放锁。
/// </summary>
public interface ILockHandle : IAsyncDisposable
{
    /// <summary>
    /// 持锁资格失效时被取消。
    /// </summary>
    /// <remarks>
    /// 基于租约的实现（如 Redis）无法保证"拿到锁 = 一直持有锁"：租约到期而续期失败时，
    /// 另一个实例可以合法进入临界区，而当前进程仍会继续写。
    /// 临界区里做长事务、数据迁移、批量初始化时，应把本令牌与自己的 <see cref="CancellationToken"/> 关联。
    /// 进程内实现不存在租约，本令牌在释放前永不取消。
    /// </remarks>
    CancellationToken LockLost { get; }
}
