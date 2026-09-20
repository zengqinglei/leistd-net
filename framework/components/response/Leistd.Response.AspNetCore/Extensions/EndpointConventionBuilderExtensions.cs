using Leistd.Response.AspNetCore.Filters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

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
    /// MVC 侧的 <c>AddResponseWrapper()</c> 只作用于控制器；组件经 <c>Map*</c> 提供的端点要包装时，
    /// 在宿主的路由组上调本方法。不要在同一端点链上重复调用：每次调用都会再挂一层过滤器，
    /// 外层看到的是已包装的 <see cref="Wrappers.Result"/>，因此不会重复包装，但会多一次无谓的判定。
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
        => builder.AddEndpointFilter<TBuilder, ResultWrapperEndpointFilter>();
}
