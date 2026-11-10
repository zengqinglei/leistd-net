using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Leistd.TestBase.Doubles;

/// <summary>把日志交给内置控制台格式化器（Simple、JSON）写进内存的日志提供程序，用于断言宿主 stdout 实际看到的文本。</summary>
/// <remarks>
/// 格式化器取自真实的 <c>AddConsole</c> 注册，打开 <c>IncludeScopes</c>、关闭颜色；
/// 与控制台提供程序不同，写出是同步的，不经后台队列，日志调用返回后即可读取 <see cref="Output"/>。
/// </remarks>
public sealed class ConsoleFormatterCapture : ILoggerProvider, ISupportExternalScope
{
    private readonly ServiceProvider _services;
    private readonly ConsoleFormatter _formatter;
    private readonly StringWriter _writer = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    /// <summary>使用名为 <paramref name="formatterName"/> 的内置格式化器，取值见 <see cref="ConsoleFormatterNames"/>。</summary>
    public ConsoleFormatterCapture(string formatterName)
    {
        _services = new ServiceCollection()
            .AddLogging(logging => logging
                .AddSimpleConsole(options =>
                {
                    options.IncludeScopes = true;
                    options.ColorBehavior = LoggerColorBehavior.Disabled;
                })
                .AddJsonConsole(options => options.IncludeScopes = true))
            .BuildServiceProvider();
        _formatter = _services.GetServices<ConsoleFormatter>().Single(f => f.Name == formatterName);
    }

    /// <summary>迄今写出的全部文本。</summary>
    public string Output
    {
        get
        {
            lock (_writer)
            {
                return _writer.ToString();
            }
        }
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new FormattingLogger(this, categoryName);

    /// <inheritdoc />
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    /// <inheritdoc />
    public void Dispose()
    {
        _services.Dispose();
        _writer.Dispose();
    }

    private sealed class FormattingLogger(ConsoleFormatterCapture owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (owner._writer)
            {
                owner._formatter.Write(
                    new LogEntry<TState>(logLevel, category, eventId, state, exception, formatter),
                    owner._scopes,
                    owner._writer);
            }
        }
    }
}
