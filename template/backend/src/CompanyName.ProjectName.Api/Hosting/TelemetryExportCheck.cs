using System.Net;
using CompanyName.ProjectName.Api.Options;
using CompanyName.ProjectName.Application.Settings.Provider;
using Leistd.Settings.Checks;
using Leistd.Settings.Resolution;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Api.Hosting;

/// <summary>系统设置的"测试"：向 OTLP 导出地址发一个空的 OTLP/HTTP 请求，按状态码判断地址与认证头是否可用。</summary>
/// <remarks>
/// <para>先经设置读取器取值：存着的认证头解不开时读取抛出，由设置组件记为 <c>secret-unavailable</c>，
/// 而不是用配置里尚未被替换的旧值测出一个"通过"。没有存值的项回落到部署配置，与导出时的取值一致。</para>
/// <para>空的追踪导出请求是合法的 OTLP 消息，Collector 与托管平台按正常请求鉴权并返回 2xx；不跟随重定向，
/// 落到登录页的 200 不会被当成通过。</para>
/// </remarks>
internal sealed class TelemetryExportCheck(
    ISettingProvider settings,
    IOptionsMonitor<TelemetryExportOptions> export,
    IHttpClientFactory httpClients) : ISettingCheck
{
    public const string HttpClientName = nameof(TelemetryExportCheck);

    public async Task<SettingCheckOutcome> ExecuteAsync(SettingCheckContext context, CancellationToken cancellationToken)
    {
        var headers = await settings.GetOrNullAsync(SettingConstant.Telemetry.ExportHeaders, cancellationToken)
                      ?? export.CurrentValue.Headers;
        var endpoint = await settings.GetOrNullAsync(SettingConstant.Telemetry.ExportEndpoint, cancellationToken)
                       ?? export.CurrentValue.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return SettingCheckOutcome.Fail(SettingCheckFailureCodes.NotConfigured);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint.Trim().TrimEnd('/')}/v1/traces")
        {
            Content = new ByteArrayContent([])
        };
        request.Content.Headers.ContentType = new("application/x-protobuf");
        foreach (var (name, value) in TelemetryExportHandler.ParseHeaders(headers))
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        try
        {
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            return response.StatusCode switch
            {
                _ when response.IsSuccessStatusCode => SettingCheckOutcome.Pass(),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => SettingCheckOutcome.Fail(SettingCheckFailureCodes.AuthRejected),
                HttpStatusCode.TooManyRequests => SettingCheckOutcome.Fail(SettingCheckFailureCodes.RateLimited),
                _ => SettingCheckOutcome.Fail(SettingCheckFailureCodes.UnexpectedStatus)
            };
        }
        catch (Exception exception) when (exception is HttpRequestException
                                              || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return SettingCheckOutcome.Fail(SettingCheckFailureCodes.Unreachable);
        }
    }
}
