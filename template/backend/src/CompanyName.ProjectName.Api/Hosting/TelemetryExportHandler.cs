using System.Net;
using CompanyName.ProjectName.Api.Options;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Api.Hosting;

/// <summary>OTLP 导出请求的发送层：按当时的有效配置决定发不发、发到哪、带什么认证头。</summary>
/// <remarks>
/// <para>OpenTelemetry .NET 不支持运行期重配导出器，三路导出器因此常驻，经 <c>HttpClientFactory</c> 共用本处理器。
/// 每次请求读 <see cref="IOptionsMonitor{TOptions}"/> 的当前值，系统设置提交后的下一次导出即用新值。</para>
/// <para>宿主启动完成（<see cref="TelemetryExportGate"/> 打开）之前一律不发：数据库里的宿主级设置
/// 在宿主启动阶段才加载，此前发出的请求可能违背已保存的关闭。按发送时刻判断，启动前的遥测就地丢弃。</para>
/// <para>SDK 在 .NET 上走同步 <see cref="HttpClient.Send(HttpRequestMessage)"/>，同步与异步两条路径都要拦。</para>
/// </remarks>
internal sealed class TelemetryExportHandler(
    IOptionsMonitor<TelemetryExportOptions> options,
    TelemetryExportGate gate) : DelegatingHandler
{
    /// <inheritdoc />
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        => Prepare(request) ? base.Send(request, cancellationToken) : Dropped(request);

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Prepare(request) ? base.SendAsync(request, cancellationToken) : Task.FromResult(Dropped(request));

    // 返回 false 表示不发送。地址与认证头由这里独占写入：SDK 一侧的认证头已清空，每个头只有当前的一份
    private bool Prepare(HttpRequestMessage request)
    {
        var current = options.CurrentValue;
        if (!gate.IsOpen || !current.Enabled
            || string.IsNullOrWhiteSpace(current.Endpoint) || request.RequestUri is null)
        {
            return false;
        }

        var signal = request.RequestUri.Segments[^1].Trim('/');
        request.RequestUri = new Uri($"{current.Endpoint.Trim().TrimEnd('/')}/v1/{signal}");
        foreach (var (name, value) in ParseHeaders(current.Headers))
        {
            request.Headers.Remove(name);
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return true;
    }

    // 格式同 OTEL_EXPORTER_OTLP_HEADERS：逗号分隔的 key=value，值按 URL 编码解码（与 SDK 一致）。
    // 系统设置的写法在写入时已校验，这里只解析
    private static IEnumerable<(string Name, string Value)> ParseHeaders(string? headers)
    {
        if (string.IsNullOrWhiteSpace(headers))
        {
            yield break;
        }

        foreach (var pair in headers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator > 0)
            {
                yield return (pair[..separator].Trim(), Uri.UnescapeDataString(pair[(separator + 1)..].Trim()));
            }
        }
    }

    // 空的 200：SDK 视为发送成功，不重试、不记失败
    private static HttpResponseMessage Dropped(HttpRequestMessage request)
        => new(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent([]) };
}
