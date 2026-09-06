#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Users.Passwords;
using Leistd.ExceptionHandling;

namespace CompanyName.ProjectName.UnitTests.Domain;

/// <summary>
/// 密码策略：服务端唯一权威，不建宿主就能验的典型领域规则。
/// </summary>
/// <remarks>
/// 这是单元测试该覆盖的形状——纯函数、没有依赖、分支全部可枚举。
/// 同样的规则若放进集成测试，每条分支都要付一次宿主构建的钱。
/// </remarks>
public class PasswordPolicyTests
{
    [Theory]
    [InlineData("aVeryLongPassphrase")]
    [InlineData("123456789012345")]                 // 只看长度，不要求复杂度
    [InlineData("            x")]                    // 空格计入长度
    public void Long_enough_passwords_are_accepted(string password)
    {
        Assert.True(PasswordPolicy.IsAcceptable(password));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_password_is_rejected(string? password)
    {
        Assert.False(PasswordPolicy.IsAcceptable(password));
    }

    // 边界差一位就是策略被绕过：长度下限必须精确到字符。
    [Fact]
    public void Minimum_length_boundary_is_exact()
    {
        Assert.False(PasswordPolicy.IsAcceptable(new string('a', PasswordPolicy.MinimumLength - 1)));
        Assert.True(PasswordPolicy.IsAcceptable(new string('a', PasswordPolicy.MinimumLength)));
    }

    // 上限只为防御哈希开销型拒绝服务，同样要精确。
    [Fact]
    public void Maximum_length_boundary_is_exact()
    {
        Assert.True(PasswordPolicy.IsAcceptable(new string('a', PasswordPolicy.MaximumLength)));
        Assert.False(PasswordPolicy.IsAcceptable(new string('a', PasswordPolicy.MaximumLength + 1)));
    }

    // 发布过的示例值已进入公开仓库历史，等同于已泄漏——即使长度够也必须拒绝。
    [Fact]
    public void Previously_published_sample_values_are_rejected()
    {
        Assert.All(PasswordPolicy.Rejected, p => Assert.False(PasswordPolicy.IsAcceptable(p)));
    }

    [Fact]
    public void Rejected_list_is_matched_case_insensitively()
    {
        Assert.False(PasswordPolicy.IsAcceptable("aDmIn@123456"));
    }

    // 失败必须抛 400 而不是 500，且消息带上主体，否则调用方看不出是哪个配置项不合格。
    [Fact]
    public void Ensure_reports_the_subject_in_a_bad_request()
    {
        var ex = Assert.Throws<BadRequestException>(
            () => PasswordPolicy.Ensure("short", "DefaultAdmin:Password"));

        Assert.Contains("DefaultAdmin:Password", ex.Message);
    }

    [Fact]
    public void Ensure_passes_silently_for_an_acceptable_password()
    {
        PasswordPolicy.Ensure("aVeryLongPassphrase", "DefaultAdmin:Password");
    }
}
#endif
