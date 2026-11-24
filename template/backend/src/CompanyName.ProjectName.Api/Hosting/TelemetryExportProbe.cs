using System.Net;
using CompanyName.ProjectName.Api.Options;
using CompanyName.ProjectName.Application.Settings.Errors;
using CompanyName.ProjectName.Application.Settings.Options;
using Leistd.ExceptionHandling;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Api.Hosting;

/// <summary>系统设置「运维」的导出测试：用当前生效的地址与认证头向 OTLP 端点发一个空的追踪导出请求。</summary>
/// <remarks>
/// <para>取 <see cref="IOptionsMonitor{TOptions}"/> 的当前值，与导出时同一来源；导出开关关着也测，开启之前可以先验证。
/// 不经 <see cref="TelemetryExportHandler"/>：它在导出不活动时就地丢弃并回空的 200，还会把结果记进导出失败日志。</para>
/// <para>空的追踪导出请求是合法的 OTLP 消息，Collector 与托管平台按正常请求鉴权并返回 2xx。
/// 自有客户端不跟随重定向，落到登录页的 200 不会被当成通过；超时取短值，测试不该挂住半分钟。</para>
/// <para>纯探测，不改状态也不记操作记录；不通过时抛带码的业务异常，界面按码给出原因。</para>
/// </remarks>
public sealed class TelemetryExportProbe(
    IOptionsMonitor<TelemetryExportOptions> options,
    IOptions<TelemetrySettingsOptions> settings,
    IHttpClientFactory httpClients)
{
    /// <summary>探测用的具名客户端：不跟随重定向、10 秒超时。</summary>
    public const string HttpClientName = nameof(TelemetryExportProbe);

    /// <summary>发一次空的追踪导出；对方返回 2xx 即通过。</summary>
    /// <exception cref="BusinessException">未配置地址、部署关闭了 SDK、认证被拒、连不上或返回其他状态。</exception>
    public async Task TestAsync(CancellationToken cancellationToken = default)
    {
        // 部署关闭 SDK 时导出选项没有注册，取到的是空的默认值；说"未配置"会让人去填一个不会生效的地址
        if (!settings.Value.ExportConfigurable)
        {
            throw new BusinessException(AppSettingErrorCodes.TelemetryExportUnavailable,
                "Telemetry is turned off for this deployment, so the export cannot be tested.");
        }

        var current = options.CurrentValue;
        if (string.IsNullOrWhiteSpace(current.Endpoint))
        {
            throw new BusinessException(AppSettingErrorCodes.TelemetryExportNotConfigured, "No export endpoint is configured.");
        }

        // 系统设置的地址在写入时已校验；部署配置里写坏的地址同样发不出去，按连不上报
        if (!Uri.TryCreate($"{current.Endpoint.Trim().TrimEnd('/')}/v1/traces", UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw Unreachable(innerException: null);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new ByteArrayContent([]) };
        request.Content.Headers.ContentType = new("application/x-protobuf");
        TelemetryExportHandler.WriteHeaders(request.Headers, current.Headers);

        HttpStatusCode status;
        try
        {
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            status = response.StatusCode;
        }
        // 客户端超时也抛 TaskCanceledException；调用方自己取消的照常往外传
        catch (Exception exception) when (exception is HttpRequestException
                                              || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw Unreachable(exception);
        }

        if ((int)status is >= 200 and <= 299)
        {
            return;
        }

        throw status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? new BusinessException(AppSettingErrorCodes.TelemetryExportAuthRejected,
                    "The export endpoint rejected the credentials. Check the headers.")
                .WithData("Status", (int)status)
            : new BusinessException(AppSettingErrorCodes.TelemetryExportUnexpectedStatus,
                    "The export endpoint answered with an unexpected status. Check the endpoint address.")
                .WithData("Status", (int)status);
    }

    private static BusinessException Unreachable(Exception? innerException)
        => new(AppSettingErrorCodes.TelemetryExportUnreachable,
            "The export endpoint could not be reached or did not answer in time.", innerException);
}
