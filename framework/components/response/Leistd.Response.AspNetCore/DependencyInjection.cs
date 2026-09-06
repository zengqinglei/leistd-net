using Leistd.Response.AspNetCore.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.Response.AspNetCore;

/// <summary>
/// 统一响应包装的注册入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 在宿主的 MVC 链上挂载统一响应包装过滤器。
    /// </summary>
    /// <remarks>
    /// 挂在 <see cref="IMvcBuilder"/> 上而不是 <c>IServiceCollection</c>：MVC 由宿主组装，
    /// 组件替宿主调 <c>AddControllers()</c> 会形成第二个 MVC 入口，与宿主自己的
    /// <c>AddJsonOptions(...)</c> 等配置顺序不清。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddControllers()
    ///     .AddJsonOptions(options =&gt; { /* 宿主自己的序列化配置 */ })
    ///     .AddResponseWrapper();
    ///
    /// // 控制器照常返回业务对象，过滤器自动包成 Result&lt;object?&gt;
    /// [HttpGet("{id}")]
    /// public async Task&lt;IActionResult&gt; GetAsync(long id) =&gt; Ok(await service.GetAsync(id));
    /// </code>
    /// </example>
    public static IMvcBuilder AddResponseWrapper(this IMvcBuilder builder)
    {
        builder.AddMvcOptions(options => options.Filters.Add<ResultWrapperFilter>());
        return builder;
    }
}
