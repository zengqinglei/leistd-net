#if (LocalIdentity)
using System.Security.Cryptography;
using System.Text;
using CompanyName.ProjectName.Domain.Shared.Text;

namespace CompanyName.ProjectName.Domain.Shared.Security.OneTimeCodes;

/// <summary>
/// 两步验证的恢复码：手机丢了时替代验证码登录，每个只能用一次。
/// </summary>
/// <remarks>
/// 每个码 80 位随机（16 个 Base32 字符，按 4 位一组显示），库里只存 SHA-256 摘要。
/// 这个长度下不加盐的快速哈希也无法离线穷举，所以不必走口令那套慢哈希——
/// 登录时要逐个比对十个摘要，慢哈希会把一次恢复登录拖成秒级。
/// </remarks>
public static class RecoveryCodes
{
    /// <summary>每次生成的个数。</summary>
    public const int Count = 10;

    private const int Length = 16;
    private const int GroupSize = 4;
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    /// <summary>生成一组新的恢复码（明文，只在生成时展示一次）。</summary>
    public static IReadOnlyList<string> Generate()
    {
        var codes = new List<string>(Count);
        for (var i = 0; i < Count; i++)
        {
            var chars = new char[Length];
            for (var j = 0; j < Length; j++)
            {
                chars[j] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            }

            codes.Add(string.Join('-', Enumerable.Range(0, Length / GroupSize)
                .Select(g => new string(chars, g * GroupSize, GroupSize))));
        }

        return codes;
    }

    /// <summary>
    /// 摘要。先规整（去掉分隔符与空白、转小写）：用户照抄时多一个空格或少一个短横线都应当认。
    /// </summary>
    public static string Hash(string code)
    {
        var normalized = new string(code
            .Where(c => !char.IsWhiteSpace(c) && c != '-')
            .Select(char.ToLowerInvariant)
            .ToArray());
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(normalized)));
    }
}
#endif
