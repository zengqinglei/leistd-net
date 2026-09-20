using Leistd.Response.AspNetCore.Attributes;
using Leistd.Response.Wrappers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;

namespace Leistd.Response.AspNetCore.Filters;

/// <summary>
/// 将 Minimal API 端点的成功响应包装为统一结果，与 MVC 的 <see cref="ResultWrapperFilter"/> 同一口径。
/// </summary>
/// <remarks>
/// <para>只包装两种形态：处理器直接返回的对象，以及 <c>TypedResults.Ok(value)</c>。这两种都只表达"200 加这个值"，
/// 换成信封不丢任何 HTTP 语义。</para>
/// <para>其余 <see cref="IResult"/> 一律原样放行，包括 <c>Created</c>、<c>Accepted</c>、文件与流、
/// 重定向、<c>NoContent</c> 与非 2xx：它们各自带着响应头（<c>Location</c>）、内容类型或序列化选项，
/// 重建成 JSON 会把这些语义丢掉，而状态码看上去还是对的。要让这类端点也走信封，
/// 由端点自己把信封放进结果：<c>TypedResults.Created(location, Result&lt;T&gt;.Ok(dto))</c>。</para>
/// <para>已是 <see cref="Result"/> 的值与标了 <see cref="NoWrapAttribute"/> 的端点同样原样放行。</para>
/// <para>包装改变的是运行时响应体，不改端点的 OpenAPI 元数据：端点若用 <c>Produces&lt;T&gt;()</c> 声明了形状，
/// 要改声明为 <c>Produces&lt;Result&lt;T&gt;&gt;()</c>，否则文档与实际响应不一致。</para>
/// <para>错误不经本过滤器：抛出的异常交给异常处理组件输出 Problem Details，与 MVC 侧一致。</para>
/// </remarks>
/// <param name="logger">日志。</param>
public sealed class ResultWrapperEndpointFilter(ILogger<ResultWrapperEndpointFilter> logger) : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);

        if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<NoWrapAttribute>() is not null)
        {
            return result;
        }

        switch (result)
        {
            case Result:
                return result;
            case IResult httpResult:
                if (!TryGetPlainOkValue(httpResult, out var value) || value is Result)
                {
                    return result;
                }

                logger.LogDebug("Wrapping endpoint response: {Endpoint}", context.HttpContext.GetEndpoint()?.DisplayName);
                return TypedResults.Ok(Result<object?>.Ok(value));
            default:
                logger.LogDebug("Wrapping endpoint response: {Endpoint}", context.HttpContext.GetEndpoint()?.DisplayName);
                return Result<object?>.Ok(result);
        }
    }

    // 按具体类型判断而不是按 IValueHttpResult：后者把 Created、Accepted 等带额外语义的结果也算进来，
    // 重建成 JSON 会丢掉它们的响应头
    private static bool TryGetPlainOkValue(IResult result, out object? value)
    {
        var type = result.GetType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Ok<>))
        {
            value = ((IValueHttpResult)result).Value;
            return true;
        }

        value = null;
        return false;
    }
}
