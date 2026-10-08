using Leistd.Security.AspNetCore.Claims;
using Leistd.Security.AspNetCore.RequestContext;
using Leistd.Security.RequestContext;
using Leistd.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Leistd.Security.AspNetCore;

/// <summary>当前主体安全上下文的注册入口。</summary>
public static class DependencyInjection
{
    /// <summary>注册当前主体、用户和客户端访问器，主体来源为 <c>HttpContext.User</c>。</summary>
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
    /// 与 <c>AddAmbientContext()</c> 的调用顺序无关。可重复调用，结果与调用一次相同。
    /// </remarks>
    public static IServiceCollection AddSecurity(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddAmbientContext();

        services.Replace(ServiceDescriptor.Singleton<ICurrentPrincipalAccessor, HttpContextCurrentPrincipalAccessor>());

        return services;
    }

    /// <summary>注册请求客户端信息的 HTTP 实现。</summary>
    /// <remarks>默认实现为 Transient，读取调用时的上下文；重复登记幂等，不覆盖宿主实现。</remarks>
    public static IServiceCollection AddRequestClientInfo(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddTransient<IRequestClientInfo, HttpRequestClientInfo>();
        return services;
    }
}
