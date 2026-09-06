using Leistd.Security.AspNetCore.Claims;
using Leistd.Security.Claims;
using Leistd.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Leistd.Security.AspNetCore;

/// <summary>
/// 提供当前主体安全上下文的服务注册。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册当前主体、用户和客户端访问器。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>原服务集合。</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddSecurity();
    ///
    /// public class OrderService(ICurrentUser currentUser)
    /// {
    ///     public Guid? OwnerId =&gt; currentUser.Id;
    /// }
    /// </code>
    /// </example>
    /// <remarks>
    /// 在 <c>AddAmbientContext()</c> 的基础上把主体来源换成 <c>HttpContext.User</c>。
    /// 用 <c>Replace</c> 而非 <c>Add</c>：与 <c>AddAmbientContext()</c> 的调用顺序无关，
    /// 且不留下一条永远不会被解析到的描述符。
    /// </remarks>
    public static IServiceCollection AddSecurity(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddAmbientContext();

        services.Replace(ServiceDescriptor.Singleton<ICurrentPrincipalAccessor, HttpContextCurrentPrincipalAccessor>());

        return services;
    }

}
