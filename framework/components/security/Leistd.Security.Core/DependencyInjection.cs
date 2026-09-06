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
    /// <para>注册的主体访问器<b>没有底层来源</b>——只有经
    /// <see cref="IAmbientContext.Begin"/> 建立的主体才可见，适用于后台作业、消息消费者
    /// 与 Hub 调用。Web 宿主调 <c>AddSecurity()</c>，它会覆盖为读 <c>HttpContext.User</c> 的实现。</para>
    /// <para>各维度由拥有它的组件自行登记为 <see cref="IAmbientContextContributor"/>：
    /// 多租户组件登记租户、追踪组件登记链路标识。<b>只装了本方法时只建立主体</b>——
    /// 没装的维度就是没有，不会静默给一个猜测值。</para>
    /// <para>全部为 <c>TryAdd</c>：与 <c>AddSecurity()</c> 的调用顺序无关。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddAmbientContext();
    ///
    /// using (ambientContext.Begin(systemPrincipal))
    /// {
    ///     await job.RunAsync();   // 作业内部注入的 ICurrentUser 读得到该主体
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
