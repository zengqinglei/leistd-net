using System.Reflection;
using Castle.DynamicProxy;
using Leistd.UnitOfWork.Events;
using Microsoft.Extensions.Logging;
using Leistd.DynamicProxy.Interceptors;
using Leistd.EventBus.EventHandlers;
using Leistd.EventBus.Events;

namespace Leistd.UnitOfWork.Interceptors;

/// <summary>
/// 按工作单元阶段调度事件处理器。
/// </summary>
/// <remarks>
/// 挂在<b>所有</b> <see cref="IEventHandler{TEvent}"/> 上：<c>CompleteAsync</c> 对每个事件发布两趟
/// （BeforeCommit 与 AfterCommit），由本拦截器挡掉不属于当前阶段的那趟；未被代理的处理器两趟都会执行。
/// 因此"未标注特性"被解释成确定的 <c>AfterCommit</c> 阶段，而不是放行。
/// </remarks>
public class UnitOfWorkEventHandlerInterceptor(ILogger<UnitOfWorkEventHandlerInterceptor>? logger = null)
    : BaseAsyncInterceptor
{
    // 无法兑现的阶段声明必须记录警告，不能当作普通阶段过滤静默跳过。
    private enum Decision
    {
        Execute,
        SkipPhaseMismatch,
        SkipUnsatisfiable
    }

    /// <inheritdoc />
    protected override async Task InterceptAsync(
        IInvocation invocation,
        IInvocationProceedInfo proceedInfo,
        Func<IInvocation, IInvocationProceedInfo, Task> proceed)
    {
        if (!ShouldExecute(invocation))
        {
            return;
        }

        await proceed(invocation, proceedInfo);
    }

    /// <inheritdoc />
    protected override async Task<TResult> InterceptAsync<TResult>(
        IInvocation invocation,
        IInvocationProceedInfo proceedInfo,
        Func<IInvocation, IInvocationProceedInfo, Task<TResult>> proceed)
    {
        if (!ShouldExecute(invocation))
        {
            return default!;
        }

        return await proceed(invocation, proceedInfo);
    }

    // 两个拦截重载共用判定，保持同步与异步语义一致。
    private bool ShouldExecute(IInvocation invocation)
    {
        var decision = Decide(invocation, out var reason);

        switch (decision)
        {
            case Decision.SkipPhaseMismatch:
                logger?.LogDebug(
                    "Skipping event handler {Handler}; reason: {Reason}",
                    invocation.TargetType?.Name, reason);
                return false;

            case Decision.SkipUnsatisfiable:
                logger?.LogWarning(
                    "Skipping event handler {Handler}; reason: {Reason}",
                    invocation.TargetType?.Name, reason);
                return false;

            default:
                return true;
        }
    }

    private static Decision Decide(IInvocation invocation, out string reason)
    {
        reason = string.Empty;

        if (invocation.Method.Name != nameof(IEventHandler<IEvent>.HandleAsync))
        {
            return Decision.Execute;
        }

        // 处理器声明的阶段。未标注特性时取 AfterCommit——与 UnitOfWorkEventHandlerAttribute
        // 的构造函数默认值一致。这里绝不能返回"放行"：那会让处理器在两趟发布里各跑一次
        var handlerType = invocation.TargetType;
        var declaredPhase = handlerType?.GetCustomAttribute<UnitOfWorkEventHandlerAttribute>()?.Phase
            ?? UnitOfWorkPhase.AfterCommit;

        var currentPhase = UnitOfWorkContext.CurrentPhase;

        // 不在工作单元的提交流程里（直接发布，或工作单元外发布）：按 AfterCommit 语义执行一次。
        //
        // BeforeCommit 处理器在这条路上必须跳过，且要记 Warning：这条路的发布点在
        // SavedChanges 之后，数据已落库，"我的异常能回滚事务"这个契约无从兑现。
        // 带着假承诺执行比跳过更危险，但"写了却不执行"也必须留下痕迹
        if (currentPhase is null)
        {
            if (declaredPhase == UnitOfWorkPhase.BeforeCommit)
            {
                reason = "published outside a unit of work commit, where BeforeCommit cannot affect any transaction";
                return Decision.SkipUnsatisfiable;
            }

            return Decision.Execute;
        }

        if (currentPhase == declaredPhase)
        {
            return Decision.Execute;
        }

        reason = $"current phase is {currentPhase}, handler runs in {declaredPhase}";
        return Decision.SkipPhaseMismatch;
    }
}
