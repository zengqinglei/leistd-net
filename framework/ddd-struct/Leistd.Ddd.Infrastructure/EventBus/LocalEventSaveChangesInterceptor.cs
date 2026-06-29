using System.Runtime.CompilerServices;
using Leistd.Ddd.Domain.Entities;
using Leistd.EventBus.Core.Event;
using Leistd.EventBus.Core.EventBus;
using Leistd.UnitOfWork.Core.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Leistd.Ddd.Infrastructure.EventBus;

/// <summary>
/// 本地事件收集和发布拦截器
/// </summary>
/// <remarks>
/// 关键时序：
/// - <b>收集</b>在 <c>SavingChanges</c>（保存前）进行：此时新增/修改实体仍为
///   Added/Modified/Deleted，能被正确收集；若在 <c>SavedChanges</c>（保存后）收集，
///   EF Core 默认已执行 AcceptAllChangesOnSuccess，实体变为 Unchanged，会被状态过滤器漏掉，导致事件丢失。
/// - <b>发布</b>在 <c>SavedChanges</c>（保存成功后）进行：保证“先持久化成功、再发事件”的语义。
/// 收集到的事件按 DbContext 实例暂存，发布后清理。
/// </remarks>
public class LocalEventSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ILocalEventBus? _localEventBus;
    private readonly IUnitOfWorkManager? _unitOfWorkManager;
    private readonly ILogger<LocalEventSaveChangesInterceptor> _logger;

    /// <summary>
    /// 按 DbContext 实例暂存“保存前已收集”的事件，等保存成功后发布。
    /// 使用 ConditionalWeakTable 避免持有 DbContext 引用导致的泄漏，并天然隔离并发的不同 DbContext。
    /// </summary>
    private static readonly ConditionalWeakTable<DbContext, List<ILocalEvent>> _pendingByContext = new();

    public LocalEventSaveChangesInterceptor(
        ILocalEventBus? localEventBus,
        IUnitOfWorkManager? unitOfWorkManager,
        ILogger<LocalEventSaveChangesInterceptor> logger)
    {
        _localEventBus = localEventBus;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
    }

    // ---- 保存前：收集事件（此时实体状态仍为 Added/Modified/Deleted）----

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        CollectInto(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CollectInto(eventData.Context);
        return new ValueTask<InterceptionResult<int>>(result);
    }

    // ---- 保存成功后：发布事件 ----

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        PublishCollected(eventData.Context);
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await PublishCollectedAsync(eventData.Context, cancellationToken);
        return result;
    }

    // ---- 保存失败：丢弃本次收集，避免泄漏到下一次 ----

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
        => Discard(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Discard(eventData.Context);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 保存前：从 ChangeTracker 收集本地事件并暂存（同时清空实体上的事件，避免重复收集）。
    /// </summary>
    private void CollectInto(DbContext? context)
    {
        if (context == null)
            return;

        var events = CollectLocalEvents(context);
        ClearLocalEvents(context);

        if (events.Count == 0)
            return;

        // 同一保存周期内可能多次进入（极少见），累加而非覆盖。
        var list = _pendingByContext.GetOrCreateValue(context);
        list.AddRange(events);
    }

    private void Discard(DbContext? context)
    {
        if (context != null)
            _pendingByContext.Remove(context);
    }

    private List<ILocalEvent> TakePending(DbContext context)
    {
        if (_pendingByContext.TryGetValue(context, out var list))
        {
            _pendingByContext.Remove(context);
            return list;
        }
        return [];
    }

    /// <summary>
    /// 同步发布本地事件
    /// </summary>
    private void PublishCollected(DbContext? context)
    {
        if (context == null)
            return;

        var localEvents = TakePending(context);
        if (localEvents.Count == 0)
            return;

        var currentUow = _unitOfWorkManager?.Current;

        if (currentUow != null)
        {
            currentUow.AddPendingEvents(localEvents);
            _logger.LogDebug("收集到 {Count} 个事件，已添加到 UnitOfWork 待发布队列", localEvents.Count);
        }
        else if (_localEventBus != null)
        {
            // 警告：这是危险路径！
            _logger.LogWarning("检测到在同步 SaveChanges 中发布 {Count} 个本地事件。这可能会导致线程饥饿(Sync-over-Async)。请尽可能使用 SaveChangesAsync。", localEvents.Count);

            foreach (var @event in localEvents)
            {
                // 注意：同步发布可能会阻塞，建议使用异步版本
                _localEventBus.PublishAsync(@event).GetAwaiter().GetResult();
            }
        }
    }

    /// <summary>
    /// 异步发布本地事件
    /// </summary>
    private async Task PublishCollectedAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context == null)
            return;

        var localEvents = TakePending(context);
        if (localEvents.Count == 0)
            return;

        var currentUow = _unitOfWorkManager?.Current;

        if (currentUow != null)
        {
            currentUow.AddPendingEvents(localEvents);
            _logger.LogDebug("收集到 {Count} 个事件，已添加到 UnitOfWork 待发布队列", localEvents.Count);
        }
        else if (_localEventBus != null)
        {
            _logger.LogDebug("无 UnitOfWork，立即发布 {Count} 个事件（默认 AfterCommit 阶段）", localEvents.Count);
            foreach (var @event in localEvents)
            {
                await _localEventBus.PublishAsync(@event, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 收集实体中的本地事件（仅保存前调用，此时状态为 Added/Modified/Deleted）
    /// </summary>
    private static List<ILocalEvent> CollectLocalEvents(DbContext context)
    {
        return context.ChangeTracker
            .Entries<Entity>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .SelectMany(e => e.Entity.GetLocalEvents())
            .ToList();
    }

    /// <summary>
    /// 清空实体中的本地事件
    /// </summary>
    private static void ClearLocalEvents(DbContext context)
    {
        context.ChangeTracker
            .Entries<Entity>()
            .ToList()
            .ForEach(e => e.Entity.ClearLocalEvents());
    }
}
