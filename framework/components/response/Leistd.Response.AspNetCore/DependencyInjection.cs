using Leistd.Response.AspNetCore.Filters;
using Leistd.Response.AspNetCore.Writers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        // 自动模型校验经问题详情管道写出，信封写入器才接得到它
        Leistd.ExceptionHandling.AspNetCore.DependencyInjection.ConfigureApiValidation(builder);

        // 问题详情写入器按注册顺序先到先写：插到最前，才不被默认写入器与 MVC 的写入器抢先，
        // 且与宿主调用 AddProblemDetails / AddControllers 的先后无关
        if (!builder.Services.Any(service => service.ServiceType == typeof(IProblemDetailsWriter)
                && service.ImplementationType == typeof(ResultProblemDetailsWriter)))
        {
            builder.Services.Insert(0, ServiceDescriptor.Singleton<IProblemDetailsWriter, ResultProblemDetailsWriter>());
        }

        builder.AddMvcOptions(options =>
        {
            // 幂等：宿主组合根拆分时重复调用是常态，挂两遍会把响应包两层信封，
            // 而症状只在运行时的响应体里显形。
            if (options.Filters.OfType<TypeFilterAttribute>()
                .Any(f => f.ImplementationType == typeof(ResultWrapperFilter)))
            {
                return;
            }

            options.Filters.Add<ResultWrapperFilter>();
        });

        return builder;
    }
}
