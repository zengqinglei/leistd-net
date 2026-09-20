using Leistd.Response.AspNetCore.Attributes;
using Leistd.Response.AspNetCore.Filters;
using Leistd.Response.Wrappers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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

    // 只改 200：其余状态码的结果原样放行（Created、文件、非 2xx），元数据也该保持原样
    private static void RewriteSuccessResponseType(EndpointBuilder endpoint)
    {
        if (endpoint.Metadata.Any(metadata => metadata is NoWrapAttribute))
        {
            return;
        }

        for (var index = 0; index < endpoint.Metadata.Count; index++)
        {
            if (endpoint.Metadata[index] is not IProducesResponseTypeMetadata produces
                || produces.StatusCode != StatusCodes.Status200OK
                || produces.Type is not { } type
                || type == typeof(void)
                || typeof(Result).IsAssignableFrom(type))
            {
                continue;
            }

            endpoint.Metadata[index] = new ProducesResponseTypeMetadata(
                produces.StatusCode,
                typeof(Result<>).MakeGenericType(type),
                [.. produces.ContentTypes]);
        }
    }
}
