using System.Reflection;
using Leistd.Response.AspNetCore.Attributes;
using Leistd.Response.AspNetCore.Filters;
using Leistd.Response.Wrappers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;

namespace Leistd.Response.AspNetCore.Extensions;

/// <summary>
/// 为 Minimal API 端点挂载统一响应包装。
/// </summary>
public static class EndpointConventionBuilderExtensions
{
    /// <summary>
    /// 为端点或路由组挂载 <see cref="ResultWrapperEndpointFilter"/>，成功响应包装为 <c>Result&lt;object?&gt;</c>。
    /// </summary>
    /// <remarks>
    /// <para>MVC 侧的 <c>AddResponseWrapper()</c> 只作用于控制器；组件经 <c>Map*</c> 提供的端点要包装时，
    /// 在宿主的路由组上调本方法。不要在同一端点链上重复调用：每次调用都会再挂一层过滤器，
    /// 外层看到的是已包装的 <see cref="Result"/>，因此不会重复包装，但会多一次无谓的判定。</para>
    /// <para>同时把 200 响应的 OpenAPI 元数据改写成 <c>Result&lt;T&gt;</c>。Minimal API 的响应类型是从处理器
    /// 返回类型推断的，宿主拿不到组件端点的处理器，只能由这里改；不改写，文档描述的就是未包装的形状。
    /// 改写放在 <c>Finally</c> 里，那时推断出的元数据才已经在端点上。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// app.MapGroup("/api/v1")
    ///     .WithResultWrapper()
    ///     .MapGet("/orders/{id}", (long id, OrderService service) =&gt; service.GetAsync(id));
    /// </code>
    /// </example>
    /// <typeparam name="TBuilder">端点约定构建器类型。</typeparam>
    /// <param name="builder">端点或路由组的约定构建器。</param>
    public static TBuilder WithResultWrapper<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddEndpointFilter<TBuilder, ResultWrapperEndpointFilter>();
        builder.Finally(RewriteSuccessResponseType);
        return builder;
    }

    // 判据必须与过滤器同源：过滤器只包装"裸值"和 Ok<T>，所以这里也只改这两种处理器的 200 元数据。
    // 只按状态码改，会把 Json(...)、文件这类原样放行的端点在文档里声明成信封。
    private static void RewriteSuccessResponseType(EndpointBuilder endpoint)
    {
        if (endpoint.Metadata.Any(metadata => metadata is NoWrapAttribute))
        {
            return;
        }

        // 处理器的 MethodInfo 由 Minimal API 放进元数据；拿不到（自定义端点源）就不改
        if (endpoint.Metadata.OfType<MethodInfo>().LastOrDefault() is not { } handler
            || !TryGetWrappedValueType(handler.ReturnType, out var valueType))
        {
            return;
        }

        for (var index = 0; index < endpoint.Metadata.Count; index++)
        {
            if (endpoint.Metadata[index] is not IProducesResponseTypeMetadata produces
                || produces.StatusCode != StatusCodes.Status200OK
                || produces.Type != valueType
                || typeof(Result).IsAssignableFrom(valueType))
            {
                continue;
            }

            endpoint.Metadata[index] = new ProducesResponseTypeMetadata(
                produces.StatusCode,
                typeof(Result<>).MakeGenericType(valueType),
                [.. produces.ContentTypes]);
        }
    }

    // 返回类型 → 过滤器会包装的那个值的类型；不会被包装时返回 false
    private static bool TryGetWrappedValueType(Type returnType, out Type valueType)
    {
        valueType = null!;

        var returned = returnType;
        if (returned.IsGenericType
            && (returned.GetGenericTypeDefinition() == typeof(Task<>)
                || returned.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            returned = returned.GetGenericArguments()[0];
        }

        // 裸值：过滤器走 default 分支包装
        if (!typeof(IResult).IsAssignableFrom(returned))
        {
            valueType = returned;
            return returned != typeof(void) && returned != typeof(object);
        }

        if (!returned.IsGenericType)
        {
            // IResult、NoContent 这类：运行时形状未知或无值，不改
            return false;
        }

        var definition = returned.GetGenericTypeDefinition();
        if (definition == typeof(Ok<>))
        {
            valueType = returned.GetGenericArguments()[0];
            return true;
        }

        // Results<Ok<T>, NotFound, ...>：只有其中的 Ok<T> 会被包装，其余分支各有自己的状态码
        if (definition.Namespace == typeof(Ok<>).Namespace
            && definition.Name.StartsWith("Results`", StringComparison.Ordinal))
        {
            foreach (var alternative in returned.GetGenericArguments())
            {
                if (alternative.IsGenericType && alternative.GetGenericTypeDefinition() == typeof(Ok<>))
                {
                    valueType = alternative.GetGenericArguments()[0];
                    return true;
                }
            }
        }

        return false;
    }
}
