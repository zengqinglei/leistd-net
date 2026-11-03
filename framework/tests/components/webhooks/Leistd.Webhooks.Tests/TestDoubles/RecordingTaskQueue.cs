using System.Collections.Concurrent;
using Leistd.BackgroundJobs.Queues;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.Webhooks.Tests.TestDoubles;

/// <summary>只记录、不自动执行的后台队列：用例决定执行还是丢弃入队项，模拟立即投递成功或丢失。</summary>
public sealed class RecordingTaskQueue : IBackgroundTaskQueue
{
    private readonly ConcurrentQueue<Func<IServiceProvider, CancellationToken, ValueTask>> _items = new();

    /// <summary>当前尚未执行的入队项数。</summary>
    public int Count => _items.Count;

    /// <summary>为 <see langword="false"/> 时模拟队列已满。</summary>
    public bool Accepts { get; set; } = true;

    /// <inheritdoc />
    public ValueTask QueueAsync(Func<IServiceProvider, CancellationToken, ValueTask> workItem, CancellationToken cancellationToken = default)
    {
        _items.Enqueue(workItem);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public bool TryQueue(Func<IServiceProvider, CancellationToken, ValueTask> workItem)
    {
        if (Accepts)
        {
            _items.Enqueue(workItem);
        }

        return Accepts;
    }

    /// <summary>丢弃全部入队项，模拟进程退出。</summary>
    public void Drop() => _items.Clear();

    /// <summary>按入队顺序在各自的新作用域里执行全部入队项（不还原入队时的环境上下文）。</summary>
    public async Task RunAllAsync(IServiceProvider services)
    {
        while (_items.TryDequeue(out var item))
        {
            await using var scope = services.CreateAsyncScope();
            await item(scope.ServiceProvider, CancellationToken.None);
        }
    }
}
