using System.Collections.Concurrent;

namespace CompanyName.ProjectName.Api.Hosting;

/// <summary>OTLP 导出失败的提示：每路信号开始失败时记一条警告，恢复时记一条信息，持续失败不重复记。</summary>
/// <remarks>
/// <para>SDK 的导出器只经 EventSource 报错，不进 <c>ILogger</c>：不在这里记，收集器拒绝（如认证头错误）时进程里一行提示都没有。</para>
/// <para>日志器在第一次需要时才解析：导出器的 HttpClient 在构建日志工厂时就创建，那时解析日志工厂会形成环。
/// 这条日志是在导出过程中写的，SDK 此时抑制遥测采集，它只进控制台等其他提供程序，不会再被导出。</para>
/// <para>只写信号名与状态码或异常，不写请求头：认证头是机密。</para>
/// </remarks>
internal sealed class TelemetryExportFailureReporter(IServiceProvider services)
{
    private readonly ConcurrentDictionary<string, bool> failing = new(StringComparer.Ordinal);
    private ILogger? logger;

    private ILogger Logger => logger ??= services.GetRequiredService<ILoggerFactory>()
        .CreateLogger(typeof(TelemetryExportFailureReporter).FullName!);

    /// <summary>这一路导出成功；此前在失败时记一条恢复。</summary>
    public void Succeeded(string signal)
    {
        if (failing.TryRemove(signal, out _))
        {
            Logger.LogInformation("OTLP export of {Signal} recovered.", signal);
        }
    }

    /// <summary>这一路导出被拒（非 2xx）。</summary>
    public void Rejected(string signal, int statusCode)
    {
        if (failing.TryAdd(signal, true))
        {
            Logger.LogWarning(
                "OTLP export of {Signal} was rejected with HTTP {StatusCode}; check the endpoint and headers. Repeated failures are not logged until it recovers.",
                signal,
                statusCode);
        }
    }

    /// <summary>这一路导出没有发出或没有响应（连接失败、超时等）。</summary>
    public void Failed(string signal, Exception exception)
    {
        if (failing.TryAdd(signal, true))
        {
            Logger.LogWarning(
                exception,
                "OTLP export of {Signal} could not be sent. Repeated failures are not logged until it recovers.",
                signal);
        }
    }
}
