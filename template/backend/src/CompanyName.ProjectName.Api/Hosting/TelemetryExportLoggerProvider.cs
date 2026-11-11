using CompanyName.ProjectName.Api.Options;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Api.Hosting;

/// <summary>OpenTelemetry 日志提供程序的入口：导出不活动时日志不交给它，不排队、不序列化。</summary>
/// <remarks>
/// <para>包在 SDK 自己注册的提供程序外面，导出器与批处理仍走官方 <c>AddOtlpExporter</c>，标准配置
/// （<c>OTEL_BLRP_*</c>、属性限额等，环境变量与宿主配置源都算）照常生效。SDK 的日志管道没有可插在批处理之前的门控点，自建导出器又会丢掉这些配置。</para>
/// <para>别名与 SDK 提供程序相同：<c>Logging:OpenTelemetry:LogLevel</c> 这类按别名写的过滤规则照常适用；
/// 按 SDK 提供程序完整类型名写的规则不再匹配。作用域经 <see cref="ISupportExternalScope"/> 转给它。</para>
/// </remarks>
[ProviderAlias("OpenTelemetry")]
internal sealed class TelemetryExportLoggerProvider(
    ILoggerProvider inner,
    IOptionsMonitor<TelemetryExportOptions> options) : ILoggerProvider, ISupportExternalScope
{
    /// <summary>被包住的 SDK 提供程序。</summary>
    public ILoggerProvider Inner => inner;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new Logger(inner.CreateLogger(categoryName), options);

    /// <inheritdoc />
    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        => (inner as ISupportExternalScope)?.SetScopeProvider(scopeProvider);

    /// <inheritdoc />
    public void Dispose() => inner.Dispose();

    private sealed class Logger(ILogger inner, IOptionsMonitor<TelemetryExportOptions> options) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => options.CurrentValue.IsActive && inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (options.CurrentValue.IsActive)
            {
                inner.Log(logLevel, eventId, state, exception, formatter);
            }
        }
    }
}
