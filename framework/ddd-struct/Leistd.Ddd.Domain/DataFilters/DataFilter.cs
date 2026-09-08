using Microsoft.Extensions.DependencyInjection;
using Leistd.Disposables;
using System.Collections.Concurrent;

namespace Leistd.Ddd.Domain.DataFilters;

/// <summary>
/// 按标记类型解析并控制数据过滤器。
/// </summary>
public class DataFilter(IServiceProvider serviceProvider) : IDataFilter
{
    private readonly ConcurrentDictionary<Type, object> _filters = new();

    /// <inheritdoc />
    public IDisposable Disable<TFilter>() where TFilter : class
    {
        return GetFilter<TFilter>().Disable();
    }

    /// <inheritdoc />
    public IDisposable Enable<TFilter>() where TFilter : class
    {
        return GetFilter<TFilter>().Enable();
    }

    /// <inheritdoc />
    public bool IsEnabled<TFilter>() where TFilter : class
    {
        return GetFilter<TFilter>().IsEnabled;
    }

    private IDataFilter<TFilter> GetFilter<TFilter>() where TFilter : class
    {
        return (_filters.GetOrAdd(
            typeof(TFilter),
            _ => serviceProvider.GetRequiredService<IDataFilter<TFilter>>()
        ) as IDataFilter<TFilter>)!;
    }
}

/// <summary>
/// 在当前异步上下文中管理指定数据过滤器的嵌套状态。
/// </summary>
/// <typeparam name="TFilter">过滤器标记类型。</typeparam>
public class DataFilter<TFilter> : IDataFilter<TFilter>
    where TFilter : class
{
    private readonly AsyncLocal<FilterState> _filterState = new();

    /// <summary>
    /// 获取过滤器当前是否启用。
    /// </summary>
    public bool IsEnabled
    {
        get
        {
            EnsureInitialized();
            return _filterState.Value!.StateStack.Count > 0
                ? _filterState.Value.StateStack.Peek()
                : true;
        }
    }

    /// <summary>
    /// 在返回的作用域内禁用过滤器。
    /// </summary>
    public IDisposable Disable()
    {
        return SetIsEnabled(false);
    }

    /// <summary>
    /// 在返回的作用域内启用过滤器。
    /// </summary>
    public IDisposable Enable()
    {
        return SetIsEnabled(true);
    }

    private IDisposable SetIsEnabled(bool isEnabled)
    {
        EnsureInitialized();

        _filterState.Value!.StateStack.Push(isEnabled);

        return new DisposeAction(() =>
        {
            if (_filterState.Value.StateStack.Count > 0)
            {
                _filterState.Value.StateStack.Pop();
            }
        });
    }

    private void EnsureInitialized()
    {
        if (_filterState.Value == null)
        {
            _filterState.Value = new FilterState();
        }
    }

    private class FilterState
    {
        public Stack<bool> StateStack { get; } = new();
    }
}
