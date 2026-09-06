using Microsoft.Extensions.DependencyInjection;
using Leistd.Auditing.EntityFrameworkCore.Interceptors;
using Leistd.Auditing.EntityFrameworkCore.Services;
using Leistd.Timing;
using Leistd.Auditing.Abstractions;

namespace Leistd.Auditing.EntityFrameworkCore;

/// <summary>
/// 提供 EF Core 审计服务注册入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 Leistd 审计能力（审计属性设置器 + 审计拦截器）。
    /// </summary>
    /// <remarks>
    /// 调用方仍需把 <see cref="AuditSaveChangesInterceptor"/> 经 <c>DbContextOptionsBuilder.AddInterceptors(...)</c> 挂到目标 DbContext。
    /// 依赖 <see cref="IClock"/> 已注册；<see cref="Leistd.Security.Users.ICurrentUser"/> <b>可选</b>——
    /// 未注册时按匿名处理，时间审计照常落值、用户字段留空。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddAuditingEfCore();
    ///
    /// // 拦截器需显式挂到目标 DbContext
    /// builder.Services.AddDbContext&lt;AppDbContext&gt;((sp, options) =&gt; options
    ///     .UseNpgsql(connectionString)
    ///     .AddInterceptors(sp.GetRequiredService&lt;AuditSaveChangesInterceptor&gt;()));
    /// </code>
    /// </example>
    public static IServiceCollection AddAuditingEfCore(this IServiceCollection services)
    {
        // 当前用户是可选依赖，必须通过工厂按匿名场景解析。
        services.AddTransient<IAuditPropertySetter>(sp => new AuditPropertySetter(
            sp.GetRequiredService<IClock>(),
            sp.GetService<Leistd.Security.Users.ICurrentUser>()));
        services.AddTransient<AuditSaveChangesInterceptor>();
        return services;
    }

}
