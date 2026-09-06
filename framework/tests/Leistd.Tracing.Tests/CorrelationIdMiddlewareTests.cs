using System.Diagnostics;
using Leistd.Tracing.AspNetCore;
using Leistd.Tracing.Constants;
using Leistd.Tracing.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using Leistd.Tracing.Abstractions;

namespace Leistd.Tracing.Tests;

/// <summary>
/// 中间件对入站头的校验，以及与 <c>HttpContext.TraceIdentifier</c> 的对齐。
/// </summary>
public class CorrelationIdMiddlewareTests : IAsyncLifetime
{
    private readonly CapturedScopes _scopes = new();

    private IHost _host = default!;
    private System.Net.Http.HttpClient _client = default!;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddCorrelationId(_ => { });
                    services.AddSingleton(_scopes);
                    services.AddSingleton<ILoggerProvider>(new ScopeCapturingLoggerProvider(_scopes));
                })
                .Configure(app =>
                {
                    app.UseCorrelationId();
                    app.Run(async context =>
                    {
                        var provider = context.RequestServices.GetRequiredService<ICorrelationIdProvider>();
                        // 同时回显三处，用于断言它们一致
                        var activityTraceId = Activity.Current?.TraceId.ToHexString() ?? "";
                        await context.Response.WriteAsync(
                            $"{provider.Get()}|{context.TraceIdentifier}|{activityTraceId}");
                    });
                }))
            .StartAsync();

        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    private async Task<(string Correlation, string TraceIdentifier, string ActivityTraceId)> CallAsync(
        string? headerValue, string? traceParent = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        if (headerValue is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", headerValue);
        }

        if (traceParent is not null)
        {
            request.Headers.TryAddWithoutValidation("traceparent", traceParent);
        }

        using var response = await _client.SendAsync(request);
        var parts = (await response.Content.ReadAsStringAsync()).Split('|');
        return (parts[0], parts[1], parts[2]);
    }

    /// <summary>
    /// 让宿主真正建立 <see cref="Activity"/>。没有 Listener 时 ASP.NET Core 不建 Activity——
    /// 那正是"未接入 OpenTelemetry"那一档，两档都要覆盖。
    /// </summary>
    private static ActivityListener ListenToAspNetCore()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [Fact]
    public async Task A_well_formed_inbound_id_is_accepted()
    {
        var (correlation, _, _) = await CallAsync("0af7651916cd43dd8448eb211c80319c");

        Assert.Equal("0af7651916cd43dd8448eb211c80319c", correlation);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("has\nnewline")]          // 日志注入
    [InlineData("has\rcarriage")]         // 响应头注入
    [InlineData("bad;semicolon")]
    [InlineData("中文")]
    public async Task A_malformed_inbound_id_is_discarded_and_replaced(string malformed)
    {
        var (correlation, _, _) = await CallAsync(malformed);

        Assert.NotEqual(malformed, correlation);
        // 请求不因畸形头失败，而是换成新生成的合规标识
        Assert.Equal(32, correlation.Length);
    }

    [Fact]
    public async Task An_over_long_inbound_id_is_discarded()
    {
        var tooLong = new string('a', 129);

        var (correlation, _, _) = await CallAsync(tooLong);

        Assert.NotEqual(tooLong, correlation);
    }

    [Fact]
    public async Task The_resolved_id_is_written_back_to_TraceIdentifier()
    {
        // 全局异常处理器按 Activity.TraceId ?? TraceIdentifier 取 traceId，
        // 未接入 OpenTelemetry 时靠这条对齐让两边给出同一个值
        var (correlation, traceIdentifier, _) = await CallAsync("0af7651916cd43dd8448eb211c80319c");

        Assert.Equal(correlation, traceIdentifier);
    }

    [Fact]
    public async Task A_missing_inbound_header_yields_a_generated_id()
    {
        var (correlation, traceIdentifier, _) = await CallAsync(headerValue: null);

        Assert.NotEmpty(correlation);
        Assert.Equal(correlation, traceIdentifier);
    }


    [Fact]
    public async Task Without_an_activity_the_inbound_id_is_the_identity()
    {
        // 未接入 OpenTelemetry 的部署：入站头就是身份，三方仍一致
        var (correlation, traceIdentifier, activityTraceId) =
            await CallAsync("0af7651916cd43dd8448eb211c80319c");

        Assert.Equal("0af7651916cd43dd8448eb211c80319c", correlation);
        Assert.Equal(correlation, traceIdentifier);
        Assert.Equal(string.Empty, activityTraceId);   // 无 Listener → 无 Activity
    }

    [Fact]
    public async Task An_active_activity_wins_over_a_different_inbound_id()
    {
        // 回归点：此前入站头优先，于是日志/响应头是一个值、ProblemDetails 的 traceId 是另一个，
        // 客户端拿错误响应里的 Id 去日志里搜不到——正是统一标识要解决的那件事
        using var listener = ListenToAspNetCore();

        var (correlation, traceIdentifier, activityTraceId) =
            await CallAsync(headerValue: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        Assert.NotEqual(string.Empty, activityTraceId);
        Assert.Equal(activityTraceId, correlation);
        Assert.Equal(activityTraceId, traceIdentifier);
        Assert.NotEqual("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", correlation);
    }

    [Fact]
    public async Task A_traceparent_makes_all_three_sources_agree()
    {
        // 调用方按 W3C 传播时：它控制 trace id，三方一致——这是推荐用法
        using var listener = ListenToAspNetCore();
        const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

        var (correlation, traceIdentifier, activityTraceId) =
            await CallAsync(headerValue: null, traceParent: $"00-{TraceId}-00f067aa0ba902b7-01");

        Assert.Equal(TraceId, activityTraceId);
        Assert.Equal(TraceId, correlation);
        Assert.Equal(TraceId, traceIdentifier);
    }

    [Fact]
    public async Task A_matching_inbound_id_alongside_an_activity_is_not_treated_as_superseded()
    {
        using var listener = ListenToAspNetCore();
        const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

        // 入站头与 traceparent 的 trace id 相同：不产生"被取代"的诊断字段，行为等同上一条
        var (correlation, traceIdentifier, activityTraceId) =
            await CallAsync(headerValue: TraceId, traceParent: $"00-{TraceId}-00f067aa0ba902b7-01");

        Assert.Equal(TraceId, activityTraceId);
        Assert.Equal(TraceId, correlation);
        Assert.Equal(TraceId, traceIdentifier);
    }


    [Fact]
    public async Task A_superseded_inbound_id_is_recorded_in_the_log_scope()
    {
        // 有 Activity 时入站头不再是身份，但它不能就此消失——
        // 否则"客户端报的那个 Id 查不到任何东西"，只是把问题从一处挪到另一处
        using var listener = ListenToAspNetCore();
        const string Inbound = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        _scopes.Clear();
        var (correlation, _, activityTraceId) = await CallAsync(headerValue: Inbound);

        Assert.Equal(activityTraceId, correlation);
        Assert.Equal(Inbound, _scopes.Find(CorrelationIdConstants.InboundTraceIdLogKey));
        Assert.Equal(correlation, _scopes.Find(CorrelationIdConstants.TraceIdLogKey));
    }

    [Fact]
    public async Task A_matching_inbound_id_produces_no_superseded_field()
    {
        // 相同值不算"被取代"：多一个恒等字段只会放大日志体积、并暗示发生过冲突
        using var listener = ListenToAspNetCore();
        const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

        _scopes.Clear();
        await CallAsync(headerValue: TraceId, traceParent: $"00-{TraceId}-00f067aa0ba902b7-01");

        Assert.Null(_scopes.Find(CorrelationIdConstants.InboundTraceIdLogKey));
    }

    [Fact]
    public async Task Without_an_activity_there_is_no_superseded_field_either()
    {
        // 无 Activity 时入站头本身就是身份，谈不上被取代
        _scopes.Clear();
        await CallAsync(headerValue: "0af7651916cd43dd8448eb211c80319c");

        Assert.Null(_scopes.Find(CorrelationIdConstants.InboundTraceIdLogKey));
    }


    private sealed class CapturedScopes
    {
        private readonly List<IReadOnlyDictionary<string, object>> _states = [];

        public void Clear() { lock (_states) { _states.Clear(); } }

        public void Add(IReadOnlyDictionary<string, object> state)
        {
            lock (_states) { _states.Add(state); }
        }

        public object? Find(string key)
        {
            lock (_states)
            {
                return _states.FirstOrDefault(s => s.ContainsKey(key))?[key];
            }
        }
    }

    private sealed class ScopeCapturingLoggerProvider(CapturedScopes scopes) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ScopeCapturingLogger(scopes);

        public void Dispose() { }

        private sealed class ScopeCapturingLogger(CapturedScopes scopes) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                if (state is IEnumerable<KeyValuePair<string, object>> pairs)
                {
                    scopes.Add(pairs.ToDictionary(x => x.Key, x => x.Value));
                }

                return NullScope.Instance;
            }

            // 必须返回 false。ASP.NET Core 的 HostingApplicationDiagnostics 会据
            // "Hosting 的 logger 是否启用" 决定要不要为请求建 Activity——恒真会让**每个**请求
            // 都有 Activity，于是"无 Activity 那一档"的测试永远走不到。
            // 探针不该改变被测系统；这里只需要 BeginScope，而它不经过 IsEnabled。
            public bool IsEnabled(LogLevel logLevel) => false;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();

                public void Dispose() { }
            }
        }
    }
}
