using System.Net;
using Leistd.Webhooks.Signing;
using StandardWebhooks.Exceptions;
using Xunit;

namespace Leistd.Webhooks.Tests;

/// <summary>签名与 Standard Webhooks v1 互通：用官方 C# 库验签，而不是拿组件自己的算法验自己。</summary>
public sealed class WebhookSignatureTests
{
    private const string Body = """{"type":"user.created","timestamp":"2026-11-03T00:00:00Z","data":{"id":1}}""";

    private readonly WebhookSigner _signer = new();

    [Fact]
    public void Signature_verifies_with_the_official_library()
    {
        var secret = _signer.GenerateSecret();

        var headers = Sign(Body, [secret]);

        new StandardWebhooks.Webhook(secret).Verify(Body, headers);
    }

    /// <summary>轮换过渡期接收方可能还停在旧密钥，也可能已换成新密钥：两把都要能单独验过。</summary>
    [Fact]
    public void Each_secret_verifies_on_its_own_during_rotation()
    {
        var current = _signer.GenerateSecret();
        var previous = _signer.GenerateSecret();

        var headers = Sign(Body, [current, previous]);

        Assert.Equal(2, headers[WebhookSigner.SignatureHeader]!.Split(' ').Length);
        new StandardWebhooks.Webhook(current).Verify(Body, headers);
        new StandardWebhooks.Webhook(previous).Verify(Body, headers);
    }

    [Fact]
    public void A_tampered_body_fails_verification()
    {
        var secret = _signer.GenerateSecret();
        var headers = Sign(Body, [secret]);
        var tampered = Body.Replace("\"id\":1", "\"id\":2", StringComparison.Ordinal);

        Assert.Throws<WebhookVerificationException>(() => new StandardWebhooks.Webhook(secret).Verify(tampered, headers));
    }

    [Fact]
    public void A_different_secret_fails_verification()
    {
        var headers = Sign(Body, [_signer.GenerateSecret()]);

        Assert.Throws<WebhookVerificationException>(
            () => new StandardWebhooks.Webhook(_signer.GenerateSecret()).Verify(Body, headers));
    }

    /// <summary>规范要求密钥 24–64 字节随机数并带 <c>whsec_</c> 前缀；两次生成不得相同。</summary>
    [Fact]
    public void Generated_secrets_are_prefixed_random_bytes_of_the_required_length()
    {
        var first = _signer.GenerateSecret();
        var second = _signer.GenerateSecret();

        Assert.StartsWith("whsec_", first, StringComparison.Ordinal);
        Assert.InRange(Convert.FromBase64String(first["whsec_".Length..]).Length, 24, 64);
        Assert.NotEqual(first, second);
    }

    private WebHeaderCollection Sign(string body, IReadOnlyList<string> secrets)
    {
        const string id = "1f0c1a4e-6b8d-4a52-9c3b-0f6e2f8a7d10";
        var timestamp = DateTimeOffset.UtcNow;
        return new WebHeaderCollection
        {
            [WebhookSigner.IdHeader] = id,
            [WebhookSigner.TimestampHeader] = WebhookSigner.FormatTimestamp(timestamp),
            [WebhookSigner.SignatureHeader] = _signer.Sign(id, timestamp, body, secrets)
        };
    }
}
