using System.Diagnostics;
using Leistd.Tracing.Services;
using Xunit;

namespace Leistd.Tracing.Tests.Core;

/// <summary>
/// 链路标识以 <see cref="Activity"/> 为唯一身份；显式切换优先。
/// </summary>
/// <remarks>
/// 回归点：此前本组件自造一套 AsyncLocal 标识，与 .NET 原生 W3C Trace Context 平行存在，
/// 于是同一请求有两个追踪 Id——日志 Scope 与出站头是自造 GUID，
/// 而 RFC 9457 错误响应里的 traceId 取自 Activity。客户端拿错误响应里的 Id 去日志里搜，搜不到。
/// </remarks>
public class CorrelationIdTests
{
    private static Activity StartActivity()
    {
        // 必须有 Listener，否则 ActivitySource.StartActivity 返回 null（这正是"无 Activity"那一档）
        var source = new ActivitySource("Leistd.Tracing.Tests");
        var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Leistd.Tracing.Tests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);
        return source.StartActivity("test")!;
    }

    [Fact]
    public void Get_returns_the_ambient_activity_trace_id()
    {
        var provider = new CorrelationIdProvider();
        using var activity = StartActivity();

        Assert.NotNull(activity);
        Assert.Equal(activity.TraceId.ToHexString(), provider.Get());
    }

    [Fact]
    public void Create_reuses_the_ambient_activity_trace_id()
    {
        var provider = new CorrelationIdProvider();
        using var activity = StartActivity();

        // 不能生成第二个标识：同一请求出现两个 traceId 正是要修的问题
        Assert.Equal(activity.TraceId.ToHexString(), provider.Create());
    }

    [Fact]
    public void Create_without_an_activity_produces_a_w3c_shaped_trace_id()
    {
        var provider = new CorrelationIdProvider();

        var id = provider.Create();

        // 32 位小写十六进制：与 W3C TraceId 一致，也与旧的 Guid("N") 等长
        Assert.Equal(32, id.Length);
        Assert.True(id.All(c => char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f')), id);
    }

    [Fact]
    public void An_explicit_change_wins_over_the_ambient_activity()
    {
        var provider = new CorrelationIdProvider();
        using var activity = StartActivity();

        using (provider.Change("explicit-value"))
        {
            Assert.Equal("explicit-value", provider.Get());
        }

        Assert.Equal(activity.TraceId.ToHexString(), provider.Get());
    }

    [Fact]
    public void Change_restores_the_parent_value_and_is_idempotent()
    {
        var provider = new CorrelationIdProvider();

        var outer = provider.Change("outer");
        var inner = provider.Change("inner");

        Assert.Equal("inner", provider.Get());
        inner.Dispose();
        inner.Dispose();          // 幂等：重复释放不该把外层也弹掉
        Assert.Equal("outer", provider.Get());

        outer.Dispose();
        Assert.Null(provider.Get());
    }

    [Fact]
    public void Change_rejects_blank_values()
    {
        var provider = new CorrelationIdProvider();

        Assert.Throws<ArgumentException>(() => provider.Change("  "));
        Assert.Throws<ArgumentNullException>(() => provider.Change(null!));
    }
}
