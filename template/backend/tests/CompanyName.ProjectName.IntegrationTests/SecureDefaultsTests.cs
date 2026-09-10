#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Users.Options;
using CompanyName.ProjectName.Domain.Users.Passwords;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 安全默认：基础配置不得携带可直接用于生产的凭据
/// </summary>
/// <remarks>
/// <para>钉住的缺陷形态是"能跑的错误配置"：漏配管理员密码若仍能照常启动、照常用公开示例密码
/// 登录，就不会有任何一步提示这件事——直到有人用示例密码登进来。加密密钥同理：
/// 缺配时若用固定种子（如 <c>new Random(42)</c>）派生确定性密钥，所有漏配部署共用同一把、
/// 且可由任何读到源码的人推出，等同于无加密。因此这两项都必须缺配即失败。</para>
/// <para>断言方式是行为而非文本：具体口令字符串会变，而"缺配必须失败""不满足策略必须失败"
/// 这两条不会变。</para>
/// </remarks>
public class SecureDefaultsTests
{
    // 长度不够的口令必须被拒：这条是密码策略与启动校验之间唯一的接线，
    // 断的话缺配之外的弱配置就悄悄放行了。
    [Fact]
    public void Too_short_admin_password_is_rejected()
    {
        var options = new DefaultAdminOptions { Password = new string('a', PasswordPolicy.MinimumLength - 1) };

        Assert.False(options.IsPasswordUsable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_admin_password_is_rejected(string? password)
    {
        Assert.False(new DefaultAdminOptions { Password = password }.IsPasswordUsable);
    }

    /// <summary>
    /// 超级管理员入口曾是最松的那一个
    /// </summary>
    /// <remarks>
    /// 它一度只查非空，于是一字符密码能通过启动校验——而它创建的是超级管理员。
    /// 系统的真实下限等于所有入口里最宽的那条，所以"每个入口各判各的"必然退化到最弱的一档：
    /// 各入口一律调 <c>PasswordPolicy</c>，这几条用例守的就是这条接线没断。
    /// </remarks>
    [Theory]
    [InlineData("a")]
    [InlineData("short")]
    [InlineData("elevenchars")]
    public void A_too_short_admin_password_is_rejected(string password)
    {
        Assert.False(new DefaultAdminOptions { Password = password }.IsPasswordUsable);
    }

    /// <summary>长口令必须被接受：策略选长度而不是复杂度正则</summary>
    [Fact]
    public void A_long_passphrase_is_accepted()
    {
        Assert.True(PasswordPolicy.IsAcceptable("correct horse battery staple and then some"));
    }

    /// <summary>所有服务端入口的下限一致——不存在"某个入口更松"</summary>
    [Fact]
    public void Every_server_side_entrance_shares_one_minimum()
    {
        var justBelow = new string('x', PasswordPolicy.MinimumLength - 1);
        var atMinimum = new string('x', PasswordPolicy.MinimumLength);

        Assert.False(PasswordPolicy.IsAcceptable(justBelow));
        Assert.True(PasswordPolicy.IsAcceptable(atMinimum));
        Assert.False(new DefaultAdminOptions { Password = justBelow }.IsPasswordUsable);
        Assert.True(new DefaultAdminOptions { Password = atMinimum }.IsPasswordUsable);
    }

    [Fact]
    public void A_deployment_supplied_admin_password_is_accepted()
    {
        Assert.True(new DefaultAdminOptions
        {
            Password = ProjectWebApplicationFactory.TestAdminPassword
        }.IsPasswordUsable);
    }
}
#endif
