#if (LocalIdentity)
using System.Security.Cryptography;
using System.Text;
using CompanyName.ProjectName.Domain.Auth.Options;
using CompanyName.ProjectName.Domain.Auth.VerificationCodes;
using Leistd.ExceptionHandling;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Infrastructure.Shared.Security.VerificationCodes;

/// <summary>
/// 带服务端密钥的 HMAC-SHA256 验证码摘要
/// </summary>
/// <remarks>
/// 确定性、开销恒定、可直接比对。密钥来自 <see cref="VerificationCodeOptions"/>，
/// 其可用性由启动期校验保证——跨实例跨重启稳定是正确性要求，不是优化。
/// </remarks>
internal sealed class HmacVerificationCodeDigest(IOptions<VerificationCodeOptions> options)
    : IVerificationCodeDigest
{
    /// <summary>
    /// 密钥在首次真正使用时才校验，不在构造函数里
    /// </summary>
    /// <remarks>
    /// 认证应用服务依赖邮箱验证服务，后者依赖本类型——构造期抛异常会让登录也一起失败，
    /// 而登录与验证码无关。邮箱验证默认关闭，那种部署根本不需要这把密钥；
    /// 让它在构造期成为硬前置，等于用一个默认关闭的功能挡住整个认证链路。
    /// </remarks>
    private byte[] Key =>
        options.Value.TryGetKeyBytes(out var key)
            ? key
            : throw new InternalServerException(
                $"{VerificationCodeOptions.SectionName}:Key is not configured or is too short; " +
                $"provide at least {VerificationCodeOptions.MinimumKeyBytes} base64-encoded bytes. " +
                "It is required whenever UserRegistration:EnableEmailVerification is true.");

    /// <inheritdoc />
    public string Compute(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return Convert.ToBase64String(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(code)));
    }

    /// <inheritdoc />
    public bool Matches(string digest, string code)
    {
        if (string.IsNullOrEmpty(digest) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        try
        {
            // 恒定时间比对：摘要相等性判断的耗时不应泄漏匹配了多少前缀
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(digest),
                Convert.FromBase64String(Compute(code)));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
#endif
