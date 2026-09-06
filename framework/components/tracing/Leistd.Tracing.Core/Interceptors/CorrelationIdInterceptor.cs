using Castle.DynamicProxy;
using Leistd.Tracing.Constants;
using Leistd.Tracing.Services;
using Microsoft.Extensions.Logging;
using Leistd.DynamicProxy.Interceptors;
using Leistd.Tracing.Abstractions;

namespace Leistd.Tracing.Interceptors;

/// <summary>
/// <c>[CorrelationId]</c> 特性的 AOP 拦截器：为没有入站请求的调用（后台任务、定时作业）建立链路标识。
/// </summary>
public class CorrelationIdInterceptor(
    ICorrelationIdProvider correlationIdProvider,
    ILogger<CorrelationIdInterceptor> logger) : BaseAsyncInterceptor
{
    /// <summary>
    /// 获取最外层优先级，使日志上下文覆盖后续拦截器。
    /// </summary>
    public override int Order => -1000;

    /// <inheritdoc />
    protected override async Task InterceptAsync(IInvocation invocation, IInvocationProceedInfo proceedInfo, Func<IInvocation, IInvocationProceedInfo, Task> proceed)
    {
        await ExecuteInScope<object?>(invocation, async () =>
        {
            await proceed(invocation, proceedInfo);
            return null;
        });
    }

    /// <inheritdoc />
    protected override async Task<TResult> InterceptAsync<TResult>(IInvocation invocation, IInvocationProceedInfo proceedInfo, Func<IInvocation, IInvocationProceedInfo, Task<TResult>> proceed)
    {
        return await ExecuteInScope(invocation, async () => await proceed(invocation, proceedInfo));
    }

    private async Task<T> ExecuteInScope<T>(IInvocation invocation, Func<Task<T>> proceed)
    {
        var currentId = correlationIdProvider.Get();
        if (!string.IsNullOrEmpty(currentId))
        {
            return await proceed();
        }

        var newId = correlationIdProvider.Create();
        using (correlationIdProvider.Change(newId))
        {
            using (logger.BeginScope(new Dictionary<string, object>
            {
                { CorrelationIdConstants.TraceIdLogKey, newId }
            }))
            {
                logger.LogDebug("TraceId context (AOP) initialized: {TraceId} [Method: {Method}]", newId, invocation.Method.Name);
                return await proceed();
            }
        }
    }
}
