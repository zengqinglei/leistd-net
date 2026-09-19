#if (LocalIdentity)
using System.Security.Cryptography;
using CompanyName.ProjectName.Domain.Shared.Text;

namespace CompanyName.ProjectName.Domain.Shared.Security.OneTimeCodes;

/// <summary>
/// 基于时间的一次性密码（RFC 6238，HMAC-SHA1、30 秒步长、6 位），与主流身份验证器应用一致。
/// </summary>
/// <remarks>
/// 在模板里自己实现而不是引包：算法本身几十行，且 RFC 附带测试向量可直接验证（见单元测试）。
/// </remarks>
public static class Totp
{
    /// <summary>验证码位数。</summary>
    public const int Digits = 6;

    /// <summary>密钥长度（字节），与 HMAC-SHA1 的输出等长。</summary>
    public const int SecretBytes = 20;

    /// <summary>步长（秒）。</summary>
    public const int StepSeconds = 30;

    /// <summary>
    /// 前后各容忍几个步长：手机与服务器的时钟总有偏差，输入也要时间。
    /// </summary>
    public const int AllowedDrift = 1;

    /// <summary>生成一个新的随机密钥。</summary>
    public static byte[] GenerateSecret() => RandomNumberGenerator.GetBytes(SecretBytes);

    /// <summary>某一时刻所在的步序号。</summary>
    public static long TimeStepAt(DateTime now)
    {
        var utc = now.Kind == DateTimeKind.Local ? now.ToUniversalTime() : now;
        return (long)Math.Floor((utc - DateTime.UnixEpoch).TotalSeconds / StepSeconds);
    }

    /// <summary>计算某一步的验证码。</summary>
    public static string ComputeCode(byte[] secret, long timeStep)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, timeStep);

        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(secret, counter, hash);

        // RFC 4226 §5.3 动态截断
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                     | (hash[offset + 1] << 16)
                     | (hash[offset + 2] << 8)
                     | hash[offset + 3];

        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 校验验证码，返回命中的步序号；不匹配或该步已用过时返回 null。
    /// </summary>
    /// <param name="secret">密钥。</param>
    /// <param name="code">用户输入（容忍空格）。</param>
    /// <param name="now">当前时刻。</param>
    /// <param name="lastUsedStep">上次成功校验用掉的步；不大于它的步一律拒绝，同一个码不能用两次。</param>
    public static long? Verify(byte[] secret, string code, DateTime now, long? lastUsedStep)
    {
        var normalized = code.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (normalized.Length != Digits || !normalized.All(char.IsAsciiDigit))
            return null;

        var current = TimeStepAt(now);
        for (var step = current - AllowedDrift; step <= current + AllowedDrift; step++)
        {
            if (lastUsedStep is { } used && step <= used)
                continue;

            if (CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(ComputeCode(secret, step)),
                    System.Text.Encoding.ASCII.GetBytes(normalized)))
            {
                return step;
            }
        }

        return null;
    }

    /// <summary>
    /// 身份验证器应用识别的 <c>otpauth://</c> 地址（二维码的内容）。
    /// </summary>
    /// <param name="issuer">发行方，显示在应用里的条目标题。</param>
    /// <param name="account">账号名，区分同一发行方下的多个账号。</param>
    /// <param name="base32Secret">Base32 密钥。</param>
    public static string BuildUri(string issuer, string account, string base32Secret)
    {
        var label = Uri.EscapeDataString(issuer) + ":" + Uri.EscapeDataString(account);
        return $"otpauth://totp/{label}?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}"
               + $"&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
    }
}
#endif
