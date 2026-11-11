using System.Net;
using CompanyName.ProjectName.Api.Options;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Api.Hosting;

/// <summary>OTLP 导出请求的发送层：按当时的有效配置决定发不发、发到哪、带什么认证头。</summary>
/// <remarks>
/// <para>OpenTelemetry .NET 不支持运行期重配导出器，三路导出器因此常驻，经 <c>HttpClientFactory</c> 共用本处理器。
/// 每次请求读 <see cref="IOptionsMonitor{TOptions}"/> 的当前值，系统设置提交后的下一次导出即用新值。</para>
/// <para>宿主启动完成（<see cref="TelemetryExportGate"/> 打开）之前一律不发：数据库里的宿主级设置
/// 在宿主启动阶段才加载，此前发出的请求可能违背已保存的关闭。按发送时刻判断：启动完成前的发送尝试就地丢弃，
/// 启动前进入批队列、启动后才发送的数据照常按发送时的配置处理。</para>
/// <para>SDK 在 .NET 上走同步 <see cref="HttpClient.Send(HttpRequestMessage)"/>，同步与异步两条路径都要拦。
/// 发出后的失败与恢复交给 <see cref="TelemetryExportFailureReporter"/> 记日志，结果照原样返回 SDK。</para>
/// </remarks>
internal sealed class TelemetryExportHandler(
    IOptionsMonitor<TelemetryExportOptions> options,
    TelemetryExportGate gate,
    TelemetryExportFailureReporter failures) : DelegatingHandler
{
    /// <inheritdoc />
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!Prepare(request))
        {
            return Dropped(request);
        }

        var signal = SignalOf(request);
        try
        {
            return Observed(signal, base.Send(request, cancellationToken));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            failures.Failed(signal, exception);
            throw;
        }
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!Prepare(request))
        {
            return Dropped(request);
        }

        var signal = SignalOf(request);
        try
        {
            return Observed(signal, await base.SendAsync(request, cancellationToken));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            failures.Failed(signal, exception);
            throw;
        }
    }

    // 结果照原样交回 SDK（它按状态码决定重试），这里只记失败与恢复
    private HttpResponseMessage Observed(string signal, HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            failures.Succeeded(signal);
        }
        else
        {
            failures.Rejected(signal, (int)response.StatusCode);
        }

        return response;
    }

    private static string SignalOf(HttpRequestMessage request) => request.RequestUri!.Segments[^1].Trim('/');

    // 返回 false 表示不发送。地址与认证头由这里独占写入：SDK 一侧的认证头已清空，每个头只有当前的一份
    private bool Prepare(HttpRequestMessage request)
    {
        var current = options.CurrentValue;
        if (!gate.IsOpen || !current.IsActive)
        {
            return false;
        }

        request.RequestUri = new Uri($"{current.Endpoint!.Trim().TrimEnd('/')}/v1/{SignalOf(request)}");
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
