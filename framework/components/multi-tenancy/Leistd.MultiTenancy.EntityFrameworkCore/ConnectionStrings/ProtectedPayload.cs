namespace Leistd.MultiTenancy.EntityFrameworkCore.ConnectionStrings;

// 按 Data Protection 的载荷格式从密文头读密钥标识：base64url 文本解码后，前 4 字节是魔数 0x09F0C9F0（大端），
// 紧接 16 字节密钥标识（Guid 的 ToByteArray 布局）。格式见官方文档 "Authenticated encryption details"。
// 与设置组件的 Leistd.Settings.Resolution.ProtectedPayload 同一算法：两个包互不依赖，十几行不单独建包
internal static class ProtectedPayload
{
    private const uint MagicHeader = 0x09F0C9F0;

    public static Guid? TryReadKeyId(string protectedText)
    {
        var bytes = DecodeBase64Url(protectedText);
        if (bytes is null || bytes.Length < 20)
        {
            return null;
        }

        var magic = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
        return magic == MagicHeader ? new Guid(bytes.AsSpan(4, 16)) : null;
    }

    private static byte[]? DecodeBase64Url(string text)
    {
        var base64 = text.Replace('-', '+').Replace('_', '/');
        base64 = (base64.Length % 4) switch
        {
            2 => base64 + "==",
            3 => base64 + "=",
            _ => base64
        };

        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
