#if (LocalIdentity)
namespace CompanyName.ProjectName.Domain.Users.Options;

/// <summary>首次启动时创建的超级管理员配置。</summary>
/// <remarks>
/// <para>基础配置里没有口令：这个账号是超级管理员，开源模板里的默认口令等于公开凭据。
/// 只在库里还没有超级管理员、需要创建时才读取口令，此时按口令策略校验，缺失或不合格即启动失败；
/// 已有管理员的部署不必再提供。</para>
/// <para>本机开发由 <c>appsettings.Development.json</c> 提供演示口令；部署经环境变量或 user-secrets 注入。</para>
/// </remarks>
public class DefaultAdminOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "DefaultAdmin";

    /// <summary>用户名。</summary>
    public string Username { get; set; } = "admin";

    /// <summary>密码。无默认值，创建管理员时必须由配置提供。</summary>
    public string? Password { get; set; }

    /// <summary>邮箱。</summary>
    public string Email { get; set; } = "admin@companyname-projectname.com";

    /// <summary>显示名称。</summary>
    public string? DisplayName { get; set; }
}
#endif
