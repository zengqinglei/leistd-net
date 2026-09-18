#if (LocalIdentity)
namespace CompanyName.ProjectName.Domain.Tenants.Connections;

/// <summary>
/// 列出某个租户已登记的连接名（管理面用）。
/// </summary>
/// <remarks>
/// <para><b>为什么不用框架的 <c>ITenantConnectionConfigurationStore</c></b>：那个契约刻意是
/// "按名字问、按名字答，一次只出一条"——远端形态下把整租户的连接都发给某一个资源服务，
/// 等于把别的服务的库口令也发过去了。管理面要的恰好是"这个租户有哪些连接"，
/// 与那条契约的用途相反，因此单独开一个窄口，而不是去拓宽框架接口。</para>
/// <para><b>为什么定义在领域层</b>：实现要读控制库（需要 EF），而应用层不引用 EF、
/// 基础设施层也不引用应用层。与 <c>IPasswordHasher</c>、<c>IVerificationCodeDigest</c> 同形：
/// 抽象落在 Domain，实现落在 Infrastructure。</para>
/// <para><b>只回名字与版本</b>：密文不取出来，明文更不会。连接串只写不读，
/// 要用明文的是资源服务，走已认证的内部接口按名字取。</para>
/// </remarks>
public interface ITenantConnectionDirectory
{
    /// <summary>
    /// 按连接名升序列出该租户的登记；租户不存在或已删除时返回 <see langword="null"/>。
    /// </summary>
    /// <remarks>空列表与 <see langword="null"/> 是两件事：前者是"该租户不单独分库"，后者是"没有这个租户"。</remarks>
    Task<IReadOnlyList<TenantConnectionEntry>?> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}

/// <summary>一条连接登记的管理面视图。</summary>
/// <param name="Name">归一化后的连接名</param>
/// <param name="Version">该行的乐观并发版本</param>
public sealed record TenantConnectionEntry(string Name, long Version);
#endif
