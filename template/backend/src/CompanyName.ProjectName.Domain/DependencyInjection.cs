using CompanyName.ProjectName.Domain.Users.DomainServices;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.Domain;

/// <summary>
/// Domain 层依赖注入配置
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 Domain 层服务
    /// </summary>
    public static IServiceCollection AddDomainServices(this IServiceCollection services)
    {
        // 用户管理领域服务
        services.AddTransient<UserDomainService>();

        return services;
    }
}
