#if (LocalIdentity)
namespace CompanyName.ProjectName.Domain.Shared.Text;

/// <summary>
/// RFC 4648 Base32（不带填充）。身份验证器应用按这种写法导入密钥。
/// </summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>编码为大写 Base32，不带 <c>=</c> 填充。</summary>
    public static string Encode(ReadOnlySpan<byte> data)
    {
        var output = new System.Text.StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            output.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return output.ToString();
    }

    /// <summary>解码；忽略大小写、空格与填充。含非法字符时返回 null。</summary>
    public static byte[]? Decode(string text)
    {
        var output = new List<byte>(text.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in text)
        {
            if (c is ' ' or '=' or '-')
                continue;

            var index = Alphabet.IndexOf(char.ToUpperInvariant(c));
            if (index < 0)
                return null;

            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
#endif
