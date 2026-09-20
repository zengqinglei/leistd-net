namespace Leistd.MultiTenancy.ConnectionStrings;

/// <summary>
/// 列出某个租户已登记的连接名与版本（管理面用）。
/// </summary>
/// <remarks>
/// <para>与 <see cref="ITenantConnectionConfigurationStore"/> 分开：那个契约刻意"按名字问、按名字答，一次只出一条"——
/// 远端形态下把整租户的连接都发给某一个资源服务，等于把别的服务的库口令也发过去了。
/// 管理面要的恰好是"这个租户有哪些连接"，因此单独开一个窄口，而不是拓宽那个契约。</para>
/// <para>只回名字与版本，密文不取出来，明文更不会。</para>
/// </remarks>
public interface ITenantConnectionDirectory
{
    /// <summary>
    /// 按连接名升序列出该租户的登记；租户不存在或已删除时返回 <see langword="null"/>。
    /// </summary>
    /// <remarks>空列表与 <see langword="null"/> 是两件事：前者是"该租户不单独分库"，后者是"没有这个租户"。</remarks>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<TenantConnectionEntry>?> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>一条连接登记的管理面视图。</summary>
/// <param name="Name">归一化后的连接名。</param>
/// <param name="Version">该行的乐观并发版本。</param>
public sealed record TenantConnectionEntry(string Name, long Version);
