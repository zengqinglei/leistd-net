#if (LocalIdentity)
namespace CompanyName.ProjectName.Domain.Auth.Options;

/// <summary>
/// 验证码摘要密钥（配置节 <c>VerificationCodes</c>）
/// </summary>
/// <remarks>
/// <para><b>为什么必须是显式配置的稳定密钥。</b>摘要要做的事是：数据库泄漏后，
/// 攻击者不能离线枚举 10⁶ 空间反查出在途验证码。这需要一个他不知道的密钥。</para>
/// <para>密钥还必须跨实例、跨重启稳定——否则同一个验证码在签发它的 Pod 之外校验不过，
/// 多副本部署下表现为"验证码时灵时不灵"。曾试过用 Data Protection 派生：
/// <c>IDataProtector.Protect</c> 含随机 IV、<b>不是确定性的</b>，
/// 每次构造都会得到不同密钥，正好踩中这个坑。</para>
/// <para>没有默认值：缺失即启动失败。一个"能跑的错误配置"在这里的后果是
/// 验证码功能静默不可靠，而不是明确报错。</para>
/// </remarks>
public sealed class VerificationCodeOptions
{
    /// <summary>配置节名</summary>
    public const string SectionName = "VerificationCodes";

    /// <summary>摘要密钥（Base64）。无默认值</summary>
    public string? Key { get; set; }

    /// <summary>最小密钥长度（字节）</summary>
    public const int MinimumKeyBytes = 32;

    /// <summary>密钥是否可用</summary>
    public bool IsKeyUsable => TryGetKeyBytes(out _);

    /// <summary>解析密钥字节</summary>
    public bool TryGetKeyBytes(out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(Key))
        {
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(Key);
            if (bytes.Length < MinimumKeyBytes)
            {
                return false;
            }

            key = bytes;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
#endif
