using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Leistd.AmbientContext;
using Leistd.Security.Claims;
using Leistd.Security.Clients;
using Leistd.Security.Users;

namespace Leistd.Security;

/// <summary>
/// security 核心能力的注册入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册非 HTTP 入口的安全上下文：<see cref="IAmbientContext"/> 及其读取面。
    /// </summary>
    /// <remarks>
    /// 主体来源须显式建立；<see cref="IAmbientContext.Begin"/> 同时建立已注册贡献者的维度。
    /// 仅注册本方法时只有主体维度。Web 宿主用 <c>AddSecurity()</c> 接入 HTTP 主体来源，调用顺序无关。
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddAmbientContext();
    ///
    /// using (ambientContext.Begin(systemPrincipal))
    /// {
    ///     await job.RunAsync();
    /// }
    /// </code>
    /// </example>
    public static IServiceCollection AddAmbientContext(this IServiceCollection services)
    {
        services.TryAddSingleton<ICurrentPrincipalAccessor, CurrentPrincipalAccessor>();
        services.TryAddTransient<ICurrentUser, CurrentUser>();
        services.TryAddTransient<ICurrentClient, CurrentClient>();
        services.TryAddTransient<IAmbientContext, AmbientContext.AmbientContext>();
        return services;
    }
}
