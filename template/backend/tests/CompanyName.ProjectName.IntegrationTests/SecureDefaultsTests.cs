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
/// <para>断言方式是行为而非文本：密码是否等于某个具体字符串会随示例值变化，
/// 而"公开发布过的值必须被拒绝""缺配必须失败"这两条不会变。</para>
/// </remarks>
public class SecureDefaultsTests
{
    /// <summary>所有入口共用同一条账号状态策略</summary>
    [Fact]
    public void Publicly_known_admin_passwords_are_rejected()
    {
        foreach (var published in PasswordPolicy.Rejected)
        {
            var options = new DefaultAdminOptions { Password = published };
            Assert.False(options.IsPasswordUsable, $"'{published}' 已公开发布，必须被拒绝");
        }
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
    /// 它只查非空与三个示例值，于是一字符密码能通过启动校验——而它创建的是超级管理员。
    /// 系统的真实下限等于所有入口里最宽的那条，所以"每个入口各判各的"必然退化到最弱的一档。
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
