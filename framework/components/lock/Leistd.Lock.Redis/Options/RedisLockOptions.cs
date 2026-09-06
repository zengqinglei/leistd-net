namespace Leistd.Lock.Redis.Options;

/// <summary>
/// 配置 Redis 分布式锁。
/// </summary>
/// <remarks>
/// 三个值各自决定生产行为：租约长短影响"实例崩溃后多久别人才能进临界区"，
/// 轮询间隔影响抢锁延迟与 Redis 压力，key 前缀决定共用一个 Redis 的多个应用会不会互相抢锁。
/// </remarks>
public sealed class RedisLockOptions
{
    /// <summary>获取配置节名称 <c>Leistd:Lock:Redis</c>。</summary>
    public const string SectionName = "Leistd:Lock:Redis";

    /// <summary>
    /// 获取或设置锁键前缀。
    /// </summary>
    /// <remarks>
    /// <b>多个应用共用一个 Redis 实例时必须设置。</b>没有前缀时锁 key 就是调用方传入的业务键，
    /// 两个不相关的应用只要键名撞上就会互相阻塞，而两边日志各自都正常。
    /// 推荐取值：应用名或应用名 + 环境，如 <c>acme-shop:prod:</c>；前缀不会自动补分隔符。
    /// <see langword="null"/> 归一化为空串。
    /// </remarks>
    public string KeyPrefix
    {
        get => _keyPrefix;
        set => _keyPrefix = value ?? string.Empty;
    }

    private string _keyPrefix = string.Empty;

    /// <summary>
    /// 获取或设置锁租约时长。
    /// </summary>
    /// <remarks>
    /// 句柄会按租约的三分之一自动续期，因此临界区可以长于本值；本值真正决定的是
    /// <b>持有者进程异常终止后，锁最多被占多久</b>。调小让故障恢复更快，
    /// 但也让续期失败的容错次数变少。
    /// </remarks>
    public TimeSpan Expiry { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 获取或设置获取锁失败后的重试间隔。
    /// </summary>
    /// <remarks>
    /// 本实现按轮询抢锁（不依赖 Redis 键空间通知，避免要求宿主开启
    /// <c>notify-keyspace-events</c> 这一非默认配置）。间隔越小抢锁越快、Redis 请求越多；
    /// 高并发争抢同一把锁时应适当放大。
    /// </remarks>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromMilliseconds(50);
}
