#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Users.Policies;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 口令策略：所有服务端入口共用一条下限
/// </summary>
/// <remarks>
/// 超级管理员入口曾经只查非空，一字符口令就能创建超级管理员。系统的真实下限等于所有入口里最宽的那条，
/// 所以各入口一律调 <c>PasswordPolicy</c>；管理员初始化经它校验的行为由 <c>DefaultAdminBootstrapTests</c> 覆盖。
/// </remarks>
public class SecureDefaultsTests
{
    /// <summary>长口令必须被接受：策略选长度而不是复杂度正则</summary>
    [Fact]
    public void A_long_passphrase_is_accepted()
    {
        Assert.True(PasswordPolicy.IsAcceptable("correct horse battery staple and then some"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("elevenchars")]
    public void Missing_or_short_passwords_are_rejected(string? password)
    {
        Assert.False(PasswordPolicy.IsAcceptable(password));
    }

    [Fact]
    public void The_minimum_length_is_the_boundary()
    {
        Assert.False(PasswordPolicy.IsAcceptable(new string('x', PasswordPolicy.MinimumLength - 1)));
        Assert.True(PasswordPolicy.IsAcceptable(new string('x', PasswordPolicy.MinimumLength)));
    }
}
#endif
