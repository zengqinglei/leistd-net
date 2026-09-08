using CompanyName.ProjectName.Domain.Auth.Options;
using CompanyName.ProjectName.Infrastructure.Shared.Security.VerificationCodes;
using Leistd.ExceptionHandling;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 验证码摘要：确定性、与口令哈希分开
/// </summary>
/// <remarks>
/// 摘要必须跨实例跨重启稳定，否则同一验证码在签发它的 Pod 之外校验不过，
/// 多副本下表现为"验证码时灵时不灵"。曾试过用 Data Protection 派生密钥——
/// <c>IDataProtector.Protect</c> 含随机 IV、不是确定性的，每次构造都得到不同密钥，
/// 正好踩中这个坑。本用例把"确定性"钉成契约。
/// </remarks>
public class VerificationCodeDigestTests
{
    private static readonly string Key = Convert.ToBase64String(
        Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

    private static HmacVerificationCodeDigest Create() =>
        new(Options.Create(new VerificationCodeOptions { Key = Key }));

    [Fact]
    public void The_same_code_yields_the_same_digest_across_instances()
    {
        // 两个独立实例，模拟两个 Pod / 一次重启
        Assert.Equal(Create().Compute("123456"), Create().Compute("123456"));
    }

    [Fact]
    public void A_digest_matches_only_its_own_code()
    {
        var digest = Create().Compute("123456");

        Assert.True(Create().Matches(digest, "123456"));
        Assert.False(Create().Matches(digest, "123457"));
    }

    [Fact]
    public void A_malformed_digest_does_not_match_and_does_not_throw()
    {
        Assert.False(Create().Matches("not-base64!!", "123456"));
    }

    /// <summary>
    /// 缺密钥或密钥过短时，在使用处失败，而不是回落到某个可推导的值
    /// </summary>
    /// <remarks>
    /// 失败点刻意不在构造函数：认证应用服务依赖邮箱验证服务、后者依赖本类型，
    /// 构造期抛会让登录也一起失败——而登录与验证码无关。
    /// 邮箱验证默认关闭，那种部署根本不需要这把密钥。
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("c2hvcnQ=")]
    public void A_missing_or_short_key_fails_on_use_not_on_construction(string? key)
    {
        var digest = new HmacVerificationCodeDigest(
            Options.Create(new VerificationCodeOptions { Key = key }));

        // 构造本身必须成功：无关的依赖解析不该被它挡住
        // 显式 InternalServerException 而非 BCL 异常：这条路径在请求上（注册/发验证码），
        // 让兜底处理器替它决定会把"密钥没配"变成无信息的"系统错误"
        Assert.Throws<InternalServerException>(() => digest.Compute("123456"));
    }
}
