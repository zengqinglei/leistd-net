#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Domain.Entities.Auditing;
using Leistd.MultiTenancy.Tenancy;

namespace CompanyName.ProjectName.Domain.Auth.Entities;

/// <summary>
/// 外部登录连接实体（GitHub、Google 等第三方身份提供商）
/// </summary>
/// <remarks>
/// 实现 <see cref="IMultiTenant"/>：外部身份的 (Provider, ProviderUserId) 由第三方决定，
/// 只在租户内唯一。不分区的话，同一个 GitHub 账号在租户 A 绑定后，租户 B 的登录会命中 A 的连接，
/// 既泄漏该外部身份已被占用，又让同一账号无法在多个租户各自绑定——那是 SaaS 的正常需求。
/// </remarks>
public class ExternalLoginConnection : DeletionAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>
    /// 所属租户（null 为宿主），由多租户落值拦截器在创建时填充
    /// </summary>
    public Guid? TenantId { get; private set; }

    /// <summary>
    /// 用户 ID
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// 外部身份提供商（GitHub, Google）
    /// </summary>
    public string Provider { get; private set; }

    /// <summary>
    /// 外部身份提供商的用户 ID
    /// </summary>
    public string ProviderUserId { get; private set; }

    /// <summary>
    /// 外部账号在提供商侧的展示标签（GitHub 的句柄、Google 的邮箱）
    /// </summary>
    /// <remarks>
    /// 只用于在"已绑定哪些登录方式"里显示是哪个账号，不是本地用户名，也不是身份标识：
    /// 身份按 <c>Provider</c> + <see cref="ProviderUserId"/> 认，本地用户名由外部登录领域服务生成。
    /// </remarks>
    public string? ProviderAccountLabel { get; private set; }

    /// <summary>
    /// 外部身份提供商的邮箱
    /// </summary>
    public string? ProviderEmail { get; private set; }

    /// <summary>
    /// 外部身份提供商的头像 URL
    /// </summary>
    public string? ProviderAvatarUrl { get; private set; }

    /// <summary>
    /// 最后同步时间
    /// </summary>
    public DateTime? LastSyncTime { get; private set; }

    /// <summary>
    /// 导航属性 - 用户
    /// </summary>
    public User? User { get; private set; }

    private ExternalLoginConnection()
    {
        Provider = null!;
        ProviderUserId = null!;
    }

    public ExternalLoginConnection(
        Guid userId,
        string provider,
        string providerUserId,
        DateTime syncedAt,
        string? providerAccountLabel = null,
        string? providerEmail = null,
        string? providerAvatarUrl = null)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        ProviderUserId = providerUserId ?? throw new ArgumentNullException(nameof(providerUserId));
        ProviderAccountLabel = providerAccountLabel;
        ProviderEmail = providerEmail;
        ProviderAvatarUrl = providerAvatarUrl;
        LastSyncTime = syncedAt;
    }

    public void Update(
        DateTime syncedAt,
        string? providerAccountLabel = null,
        string? providerEmail = null,
        string? providerAvatarUrl = null)
    {
        ProviderAccountLabel = providerAccountLabel;
        ProviderEmail = providerEmail;
        ProviderAvatarUrl = providerAvatarUrl;
        LastSyncTime = syncedAt;
    }

}
#endif
