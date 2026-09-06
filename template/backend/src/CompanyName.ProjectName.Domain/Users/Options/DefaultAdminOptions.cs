#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Users.Passwords;
namespace CompanyName.ProjectName.Domain.Users.Options;

/// <summary>
/// 首次启动时创建的超级管理员配置
/// </summary>
/// <remarks>
/// <para><b>密码没有默认值，且启动期校验。</b>这个账号是超级管理员，一旦用公开已知的密码创建出来，
/// 任何拿到部署地址的人都能登进去——而模板是开源的，"默认密码"等于"公开凭据"。
/// 内置默认值最危险的地方在于它能跑：漏配的部署照样启动、照样能登录，
/// 没有任何一步会提示这件事，直到被人用示例密码登进去为止。</para>
/// <para>本地开发用 <c>dotnet user-secrets</c> 或环境变量提供；测试宿主自带测试凭据。
/// 部署清单里必须显式注入，缺失即拒绝启动。</para>
/// </remarks>
public class DefaultAdminOptions
{
    /// <summary>
    /// 配置节名称
    /// </summary>
    public const string SectionName = "DefaultAdmin";

    /// <summary>
    /// 用户名
    /// </summary>
    public string Username { get; set; } = "admin";

    /// <summary>
    /// 密码。无默认值，必须由配置提供
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// 密码是否满足统一的服务端策略
    /// </summary>
    /// <remarks>
    /// 委派给 <see cref="PasswordPolicy"/> 而不是自己判一遍。各入口各判各的时，
    /// 系统的真实下限等于最宽的那一条；而这个入口创建的是超级管理员，本该是最严的。
    /// </remarks>
    public bool IsPasswordUsable => PasswordPolicy.IsAcceptable(Password);

    /// <summary>
    /// 邮箱
    /// </summary>
    public string Email { get; set; } = "admin@companyname-projectname.com";

    /// <summary>
    /// 显示名称
    /// </summary>
    public string? DisplayName { get; set; }
}
#endif
