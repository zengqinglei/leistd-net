using System.Security.Cryptography;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Leistd.Webhooks.Signing;
using Microsoft.AspNetCore.DataProtection;

namespace Leistd.Webhooks.EntityFrameworkCore.Subscriptions;

// 签名密钥以宿主 Data Protection 加密存储；本组件不管理密钥环。
// 用途字符串固定：改动会让所有已存密文不可解密。
internal sealed class WebhookSecretProtector(IDataProtectionProvider provider, WebhookSigner signer)
{
    public const string Purpose = "Leistd.Webhooks.Secrets.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    // 生成新密钥：明文只交给调用方显示一次，密文写进订阅。
    public (string Secret, string ProtectedSecret) CreateSecret()
    {
        var secret = signer.GenerateSecret();
        return (secret, _protector.Protect(secret));
    }

    public string Protect(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        return _protector.Protect(secret);
    }

    public string Unprotect(WebhookSubscription subscription, string protectedSecret)
    {
        try
        {
            return _protector.Unprotect(protectedSecret);
        }
        catch (CryptographicException exception)
        {
            // 密钥环丢失、未在副本间共享或密文被篡改；拒绝而不是发出签不对的请求。消息不带密文
            throw new InvalidOperationException(
                $"The signing secret of webhook subscription '{subscription.Id}' could not be decrypted with the current "
                + "Data Protection key ring. Check that this process shares the key ring and application name "
                + "of the process that wrote it.",
                exception);
        }
    }

    // 本次签名用的明文密钥：当前密钥在前；轮换过渡期内（截止时刻之前）带上旧密钥。
    public IReadOnlyList<string> GetSigningSecrets(WebhookSubscription subscription, DateTime now)
    {
        var current = Unprotect(subscription, subscription.Secret);
        return subscription is { PreviousSecret: { } previous, PreviousSecretExpiresAt: { } expiresAt } && now < expiresAt
            ? [current, Unprotect(subscription, previous)]
            : [current];
    }
}
