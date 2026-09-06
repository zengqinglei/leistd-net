using System.Diagnostics;
using Leistd.Tracing.Constants;
using Leistd.Tracing.Options;
using Leistd.Tracing.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Leistd.Tracing.Abstractions;

namespace Leistd.Tracing.AspNetCore.Middlewares;

/// <summary>
/// 入站链路标识中间件：定案本次请求的 TraceId、注入日志 Scope，并按配置写回响应头。
/// </summary>
/// <remarks>应尽量靠近管道前端，使后续中间件的日志都带上链路标识。</remarks>
public class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger,
    IOptionsMonitor<CorrelationIdOptions> optionsMonitor)
{
    /// <inheritdoc />
    public async Task InvokeAsync(HttpContext context, ICorrelationIdProvider correlationIdProvider)
    {
        var options = optionsMonitor.CurrentValue;
        if (!options.Enabled)
        {
            await next(context);
            return;
        }

        var headerNames = options.HeaderNames;
        var (correlationId, supersededInboundId) = ResolveCorrelationId(
            context, correlationIdProvider, headerNames);

        // 同步请求标识，使异常响应与日志使用同一 traceId。
        context.TraceIdentifier = correlationId;

        var logState = new Dictionary<string, object>
        {
            [CorrelationIdConstants.TraceIdLogKey] = correlationId
        };

        // 保留被 Activity 取代的入站值，便于按调用方提供的标识检索日志。
        if (supersededInboundId is not null)
        {
            logState[CorrelationIdConstants.InboundTraceIdLogKey] = supersededInboundId;
        }

        using (correlationIdProvider.Change(correlationId))
        {
            using (logger.BeginScope(logState))
            {
                if (options.IncludeInResponseHeaders)
                {
                    CheckAndSetCorrelationIdOnResponse(context, correlationId, headerNames);
                }

                await next(context);
            }
        }
    }

    // Activity.TraceId 是分布式链路的权威标识；无 Activity 时才采用合法入站值或新值。
    // 返回被取代的入站值供诊断。
    private (string CorrelationId, string? SupersededInboundId) ResolveCorrelationId(
        HttpContext context,
        ICorrelationIdProvider provider,
        IReadOnlyList<string> headerNames)
    {
        var inboundId = TryGetInboundCorrelationId(context, headerNames);
        var activityTraceId = Activity.Current?.TraceId.ToHexString();

        if (activityTraceId is null)
        {
            return (inboundId ?? provider.Create(), null);
        }

        var superseded = inboundId is not null && !string.Equals(inboundId, activityTraceId, StringComparison.Ordinal)
            ? inboundId
            : null;

        return (activityTraceId, superseded);
    }

    // 只接受有限长度的安全字符，防止外部值注入日志或响应头。
    // 非法值被忽略，不使请求失败。
    private string? TryGetInboundCorrelationId(
        HttpContext context,
        IReadOnlyList<string> headerNames)
    {
        foreach (var headerName in headerNames)
        {
            if (!context.Request.Headers.TryGetValue(headerName, out StringValues headerValue))
            {
                continue;
            }

            var correlationId = headerValue.ToString();
            if (string.IsNullOrWhiteSpace(correlationId))
            {
                continue;
            }

            if (IsWellFormed(correlationId))
            {
                return correlationId;
            }

            logger.LogDebug(
                "Discarded malformed inbound correlation id from header {HeaderName} (length {Length})",
                headerName, correlationId.Length);
        }

        return null;
    }

    private const int MaxCorrelationIdLength = 128;

    private static bool IsWellFormed(string value)
    {
        if (value.Length > MaxCorrelationIdLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static void CheckAndSetCorrelationIdOnResponse(
        HttpContext context,
        string correlationId,
        IReadOnlyList<string> headerNames)
    {
        context.Response.OnStarting(() =>
        {
            foreach (var headerName in headerNames)
            {
                if (!context.Response.Headers.ContainsKey(headerName))
                {
                    context.Response.Headers.Append(headerName, correlationId);
                }
            }
            return Task.CompletedTask;
        });
    }
}
