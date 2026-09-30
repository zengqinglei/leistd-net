using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>收集派生测试宿主里 Warning 及以上的日志，供断言或失败诊断使用。</summary>
internal sealed class WarningLogCapture : ILoggerProvider
{
    public ConcurrentQueue<(string Category, string Message)> Entries { get; } = new();

    /// <summary>
    /// 装进派生宿主的服务集合。宿主用 AddSerilog 接管了日志工厂，自带的 ILoggerProvider 收不到任何日志，
    /// 所以这里换回标准工厂——只影响装了它的那个宿主。
    /// </summary>
    public void Install(IServiceCollection services)
    {
        services.RemoveAll<ILoggerFactory>();
        services.AddLogging(logging => logging.AddProvider(this));
    }

    public override string ToString() =>
        Entries.IsEmpty
            ? "(no warnings or errors logged)"
            : string.Join(Environment.NewLine, Entries.Select(entry => $"[{entry.Category}] {entry.Message}"));

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(WarningLogCapture owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            // 带上异常全文：被隔离吞掉的渠道故障只剩这一处线索
            var message = formatter(state, exception);
            owner.Entries.Enqueue((category, exception is null ? message : $"{message}{Environment.NewLine}{exception}"));
        }
    }
}
