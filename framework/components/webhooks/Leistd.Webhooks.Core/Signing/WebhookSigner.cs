using System.Security.Cryptography;
using System.Text;

namespace Leistd.Webhooks.Signing;

// Standard Webhooks v1（https://www.standardwebhooks.com/）：对 "{id}.{timestamp}.{body}" 做 HMAC-SHA256，
// 签名头为空格分隔的 "v1,<base64>"；轮换过渡期每把密钥各签一份，接收方任一验过即可。
internal sealed class WebhookSigner
{
    internal const string IdHeader = "webhook-id";
    internal const string TimestampHeader = "webhook-timestamp";
    internal const string SignatureHeader = "webhook-signature";
    internal const string SecretPrefix = "whsec_";

    // 规范要求 24–64 字节；取 32 字节，与 HMAC-SHA256 的块内密钥长度相称。
    internal const int SecretByteLength = 32;

    // 生成新密钥：whsec_ + base64(32 字节密码学随机数)。
    public string GenerateSecret()
        => SecretPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretByteLength));

    // 返回 webhook-signature 的值；secrets 新密钥在前，输出顺序与之一致。
    public string Sign(string id, DateTimeOffset timestamp, string body, IReadOnlyList<string> secrets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(secrets);
        if (secrets.Count == 0)
        {
            throw new ArgumentException("At least one signing secret is required.", nameof(secrets));
        }

        var content = Encoding.UTF8.GetBytes($"{id}.{FormatTimestamp(timestamp)}.{body}");
        var signatures = new string[secrets.Count];
        for (var i = 0; i < secrets.Count; i++)
        {
            signatures[i] = "v1," + Convert.ToBase64String(HMACSHA256.HashData(DecodeSecret(secrets[i]), content));
        }

        return string.Join(' ', signatures);
    }

    // webhook-timestamp：Unix 秒。
    public static string FormatTimestamp(DateTimeOffset timestamp)
        => timestamp.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static byte[] DecodeSecret(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        if (!secret.StartsWith(SecretPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Webhook secrets must start with '{SecretPrefix}'.", nameof(secret));
        }

        try
        {
            return Convert.FromBase64String(secret[SecretPrefix.Length..]);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("Webhook secret is not valid base64 after the prefix.", nameof(secret), exception);
        }
    }
}
