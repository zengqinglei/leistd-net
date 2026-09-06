using System.Buffers.Binary;
using CompanyName.ProjectName.Infrastructure.Shared.Security.PasswordHash;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 口令哈希的格式与成本契约
/// </summary>
/// <remarks>
/// <para>裸 <c>[salt][hash]</c> 里没有算法、格式版本和成本参数，于是既答不出"这条是怎么算的"，
/// 也无法在不作废存量口令的前提下调高成本。</para>
/// <para><b>本用例不受服务形态裁剪。</b><c>PasswordHasher</c> 无条件注册、
/// <c>UserDomainService</c> 无条件调用它校验口令，因此资源服务形态下它同样在发货路径上；
/// 把用例一起裁掉等于让那种形态的产物带着一份没有任何测试的密码学代码。
/// 需要本地身份才能跑的验证码摘要用例在 <c>VerificationCodeDigestTests</c>。</para>
/// </remarks>
public class PasswordHashingTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void The_same_password_hashes_differently_every_time()
    {
        const string password = "PasswordHashingTests!Pw";

        // 盐必须随机：相同口令产生相同密文意味着可以直接比对出"这两个账号同密码"
        Assert.NotEqual(_hasher.HashPassword(password), _hasher.HashPassword(password));
    }

    [Fact]
    public void A_hash_verifies_against_its_own_password_only()
    {
        var hash = _hasher.HashPassword("PasswordHashingTests!Pw");

        Assert.True(_hasher.VerifyPassword(hash, "PasswordHashingTests!Pw"));
        Assert.False(_hasher.VerifyPassword(hash, "PasswordHashingTests!Px"));
    }

    /// <summary>
    /// 密文自带格式版本与成本参数
    /// </summary>
    /// <remarks>
    /// 这是"能不能在不作废存量口令的前提下提高成本"的前提：
    /// 没有它就只能全部作废或永远停在旧参数上。
    /// </remarks>
    [Fact]
    public void A_hash_carries_its_format_version_and_cost()
    {
        var payload = Convert.FromBase64String(_hasher.HashPassword("PasswordHashingTests!Pw"));

        Assert.Equal(1 + 4 + 16 + 32, payload.Length);
        Assert.Equal(1, payload[0]);

        var iterations = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(1, 4));
        Assert.Equal(600_000, iterations);
    }

    /// <summary>
    /// 损坏或旧格式的密文按"不匹配"处理，不抛
    /// </summary>
    /// <remarks>
    /// 这条路径直接面向登录请求：抛异常会把"这条哈希坏了"变成 500，
    /// 还能被用来区分账号是否存在。
    /// </remarks>
    [Theory]
    [InlineData("not-base64!!")]
    [InlineData("cGxhaW4=")]                       // 合法 Base64 但长度不对
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void A_malformed_hash_does_not_verify_and_does_not_throw(string malformed)
    {
        Assert.False(_hasher.VerifyPassword(malformed, "PasswordHashingTests!Pw"));
    }

    /// <summary>按密文记录的成本校验，而不是按当前常量——否则调高成本会作废存量口令</summary>
    [Fact]
    public void A_hash_stored_at_a_lower_cost_still_verifies()
    {
        // 手工构造一条低成本密文：盐与哈希都按 10,000 次算
        var salt = new byte[16];
        Random.Shared.NextBytes(salt);
        var hash = KeyDerivation.Pbkdf2(
            "PasswordHashingTests!Pw", salt, KeyDerivationPrf.HMACSHA256, 10_000, 32);

        var payload = new byte[1 + 4 + 16 + 32];
        payload[0] = 1;
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(1, 4), 10_000);
        salt.CopyTo(payload.AsSpan(5, 16));
        hash.CopyTo(payload.AsSpan(21, 32));

        Assert.True(_hasher.VerifyPassword(Convert.ToBase64String(payload), "PasswordHashingTests!Pw"));
    }
}
