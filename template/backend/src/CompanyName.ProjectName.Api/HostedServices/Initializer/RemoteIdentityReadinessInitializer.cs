#if (RemoteTokenAuth)
using System.Text.Json;
using CompanyName.ProjectName.Api.Extensions;
using CompanyName.ProjectName.Api.Options;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Api.HostedServices.Initializer;

/// <summary>
/// 启动期确认签发方的元数据与签名密钥可用，成功即打开 <see cref="RemoteIdentityReadinessGate"/>
/// </summary>
/// <remarks>
/// 探针要求发现文档可解析、<c>issuer</c> 与本地配置精确一致，且 <c>jwks_uri</c>
/// 至少返回一把签名密钥；仅有成功状态码不足以打开门禁。租户路由端点需要业务凭据
/// 和具体租户，不属于本探针声明的就绪范围。
/// </remarks>
internal sealed class RemoteIdentityReadinessInitializer(
    RemoteIdentityReadinessGate gate,
    IHttpClientFactory httpClientFactory,
    IOptions<RemoteIdentityOptions> remoteIdentityOptions,
    ILogger<RemoteIdentityReadinessInitializer> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// 解析发现文档、比对 issuer、取回 JWKS。任一步不成立即抛出
    /// </summary>
    internal static async Task ConfirmAsync(
        HttpClient client,
        string issuer,
        Uri metadataUrl,
        CancellationToken cancellationToken)
    {
        using var metadataResponse = await client.GetAsync(metadataUrl, cancellationToken);
        metadataResponse.EnsureSuccessStatusCode();

        // 必须解析正文，才能识别返回 200 的 HTML 错误页或登录页。
        using var document = JsonDocument.Parse(
            await metadataResponse.Content.ReadAsByteArrayAsync(cancellationToken));

        var declaredIssuer = document.RootElement.TryGetProperty("issuer", out var issuerElement)
            ? issuerElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(declaredIssuer))
        {
            throw new InvalidOperationException(
                $"The discovery document at {metadataUrl} does not declare an issuer.");
        }

        // issuer 是安全标识符；门禁必须与令牌校验器采用相同的精确比较。
        if (!string.Equals(declaredIssuer, issuer, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The discovery document at {metadataUrl} declares issuer '{declaredIssuer}', " +
                $"but this service is configured for '{issuer}'.");
        }

        var jwksUri = document.RootElement.TryGetProperty("jwks_uri", out var jwksElement)
            ? jwksElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(jwksUri))
        {
            throw new InvalidOperationException(
                $"The discovery document at {metadataUrl} does not declare a jwks_uri.");
        }

        using var jwksResponse = await client.GetAsync(jwksUri, cancellationToken);
        jwksResponse.EnsureSuccessStatusCode();

        using var jwks = JsonDocument.Parse(
            await jwksResponse.Content.ReadAsByteArrayAsync(cancellationToken));
        if (!jwks.RootElement.TryGetProperty("keys", out var keys) ||
            keys.ValueKind != JsonValueKind.Array ||
            keys.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                $"The JWKS at {jwksUri} contains no signing keys; token validation cannot succeed.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 使用已校验选项，避免重读原始配置破坏“保持未就绪并重试”的失败语义。
        var options = remoteIdentityOptions.Value;
        var issuer = options.Issuer!;
        var metadataUrl = options.MetadataUrl;
        var client = httpClientFactory.CreateClient(nameof(RemoteIdentityReadinessInitializer));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConfirmAsync(client, issuer, metadataUrl, stoppingToken);

                gate.MarkReady();
                logger.LogInformation(
                    "Remote identity metadata and signing keys confirmed at {MetadataUrl}; " +
                    "the service is now ready.",
                    metadataUrl);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // 保持未就绪并重试，让编排系统停止流量而非触发崩溃循环。
                logger.LogWarning(
                    exception,
                    "Remote identity metadata at {MetadataUrl} is not usable yet; retrying in {Delay}.",
                    metadataUrl,
                    RetryDelay);

                try
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
#endif
