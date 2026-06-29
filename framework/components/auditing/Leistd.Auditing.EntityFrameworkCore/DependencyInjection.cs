using Microsoft.Extensions.DependencyInjection;

namespace Leistd.Auditing.EntityFrameworkCore;

/// <summary>
/// 审计组件依赖注入扩展
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 Leistd 审计能力（审计属性设置器 + 审计拦截器）。
    /// </summary>
    /// <remarks>
    /// 调用方仍需将 <see cref="AuditSaveChangesInterceptor"/> 通过
    /// <c>DbContextOptionsBuilder.AddInterceptors(...)</c> 挂载到目标 DbContext。
    /// 本方法依赖 <see cref="Leistd.Timing.IClock"/> 与
    /// <see cref="Leistd.Security.Users.ICurrentUser"/> 已在容器中注册。
    /// </remarks>
    public static IServiceCollection AddAuditingEfcore(this IServiceCollection services)
    {
        services.AddScoped<IAuditPropertySetter, AuditPropertySetter>();
        services.AddScoped<AuditSaveChangesInterceptor>();
        return services;
    }

}
