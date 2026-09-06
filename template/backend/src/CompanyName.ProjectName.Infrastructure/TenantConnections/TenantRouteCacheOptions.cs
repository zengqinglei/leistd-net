#if (!LocalIdentity)
namespace CompanyName.ProjectName.Infrastructure.TenantConnections;

/// <summary>
/// 租户路由缓存配置（配置节 <c>TenantRouting</c>）
/// </summary>
/// <remarks>
/// <para><b>TTL 必须显式配置，缺省即启动失败。</b>改租户数据落点的流程是：停用租户 →
/// 等待排空 → 迁移数据 → 改路由 → 重新启用，排空要等 <c>max(Access Token 有效期, 本 TTL)</c>。
/// TTL 藏在代码默认值里，执行流程的人就无从知道该等多久；写在 <c>appsettings.json</c>
/// 才可见、可审、可按环境覆盖。刻意不做"开发宽松、生产严格"的分环境校验——
/// 那正是"开发能跑、上生产才炸"的来源。</para>
/// <para><b>与 Access Token 有效期彼此独立。</b>令牌决定已停用租户还能被访问多久，
/// TTL 决定已改的路由还会被沿用多久；排空取两者较大者即可，"TTL 不短于令牌有效期"
/// 这类约束只会无谓拉长路由陈旧窗口。</para>
/// </remarks>
internal sealed class TenantRouteCacheOptions
{
    /// <summary>配置节名</summary>
    public const string SectionName = "TenantRouting";

    /// <summary>
    /// 路由缓存生存期。<see langword="null"/> 表示未配置
    /// </summary>
    public TimeSpan? CacheLifetime { get; set; }

    /// <summary>
    /// 上限：超过这个长度的 TTL 会把切换流程的排空等待拖到不可操作
    /// </summary>
    public static readonly TimeSpan MaximumCacheLifetime = TimeSpan.FromHours(1);

    /// <summary>TTL 是否在可接受区间内</summary>
    public bool IsLifetimeUsable =>
        CacheLifetime is { } lifetime && lifetime > TimeSpan.Zero && lifetime <= MaximumCacheLifetime;
}
#endif
