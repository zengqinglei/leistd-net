using Leistd.Ddd.Domain.DataFilters;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.Ddd.Domain.Tests;

/// <summary>
/// 数据过滤器的嵌套开关：软删除与租户隔离都建立在它之上，弹栈错一次就是越权读。
/// </summary>
public class DataFilterTests
{
    private interface ISoftDeleteMarker;
    private interface ITenantMarker;

    private static IDataFilter NewFilter()
    {
        var provider = new ServiceCollection()
            .AddSingleton(typeof(IDataFilter<>), typeof(DataFilter<>))
            .BuildServiceProvider();

        return new DataFilter(provider);
    }

    // 默认启用：过滤器的存在意义就是默认拦住，需要放行时显式开口子。
    [Fact]
    public void Filters_are_enabled_by_default()
    {
        Assert.True(NewFilter().IsEnabled<ISoftDeleteMarker>());
    }

    [Fact]
    public void Disable_applies_only_inside_the_returned_scope()
    {
        var filter = NewFilter();

        using (filter.Disable<ISoftDeleteMarker>())
        {
            Assert.False(filter.IsEnabled<ISoftDeleteMarker>());
        }

        Assert.True(filter.IsEnabled<ISoftDeleteMarker>());
    }

    // 嵌套按栈还原：内层重新启用后退出，必须回到外层的"禁用"，而不是回到默认的"启用"。
    // 这一条错了，一个本该继续看到已删除数据的作用域会突然被过滤，或者反过来。
    [Fact]
    public void Nested_scopes_unwind_to_the_enclosing_state_not_to_the_default()
    {
        var filter = NewFilter();

        using (filter.Disable<ISoftDeleteMarker>())
        {
            using (filter.Enable<ISoftDeleteMarker>())
            {
                Assert.True(filter.IsEnabled<ISoftDeleteMarker>());
            }

            Assert.False(filter.IsEnabled<ISoftDeleteMarker>());
        }

        Assert.True(filter.IsEnabled<ISoftDeleteMarker>());
    }

    // 标记类型之间互不影响：关掉软删除过滤不得顺带关掉租户隔离。
    [Fact]
    public void Markers_are_independent()
    {
        var filter = NewFilter();

        using (filter.Disable<ISoftDeleteMarker>())
        {
            Assert.False(filter.IsEnabled<ISoftDeleteMarker>());
            Assert.True(filter.IsEnabled<ITenantMarker>());
        }
    }

    // 同一标记必须复用同一个 DataFilter<T> 实例，否则两次 Disable 各自压各自的栈，
    // 状态互相看不见。
    [Fact]
    public void The_same_marker_resolves_to_the_same_underlying_filter()
    {
        var filter = NewFilter();

        using (filter.Disable<ISoftDeleteMarker>())
        {
            Assert.False(filter.IsEnabled<ISoftDeleteMarker>());
            using (filter.Disable<ISoftDeleteMarker>())
            {
                Assert.False(filter.IsEnabled<ISoftDeleteMarker>());
            }
            Assert.False(filter.IsEnabled<ISoftDeleteMarker>());
        }
    }

    // 状态挂在 AsyncLocal 上：并行分支各自独立，不能互相污染。
    [Fact]
    public async Task Scopes_do_not_leak_across_async_branches()
    {
        var filter = NewFilter();
        var observed = new bool[2];

        await Task.WhenAll(
            Task.Run(() =>
            {
                using (filter.Disable<ISoftDeleteMarker>())
                {
                    Thread.Sleep(20);
                    observed[0] = filter.IsEnabled<ISoftDeleteMarker>();
                }
            }),
            Task.Run(() =>
            {
                Thread.Sleep(10);
                observed[1] = filter.IsEnabled<ISoftDeleteMarker>();
            }));

        Assert.False(observed[0]);
        Assert.True(observed[1]);
    }

    // 重复释放同一个作用域会多弹一次栈，把外层的状态也还原掉。
    // DisposeAction 的幂等保证正是为了挡住这个，这里从过滤器一侧再钉一次。
    [Fact]
    public void Disposing_an_inner_scope_twice_does_not_unwind_the_outer_one()
    {
        var filter = NewFilter();

        using (filter.Disable<ISoftDeleteMarker>())
        {
            var inner = filter.Enable<ISoftDeleteMarker>();
            inner.Dispose();
            inner.Dispose();

            Assert.False(filter.IsEnabled<ISoftDeleteMarker>());
        }
    }
}
