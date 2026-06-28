using Microsoft.Extensions.DependencyInjection;

namespace Leistd.Auditing;

/// <summary>
/// 审计核心组件依赖注入配置。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册审计核心能力。
    /// </summary>
    public static IServiceCollection AddAuditingCore(this IServiceCollection services)
    {
        return services;
    }
}
