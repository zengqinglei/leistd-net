using CompanyName.ProjectName.Domain.Users.DomainServices;
using Microsoft.Extensions.DependencyInjection;

#if (IncludeExternalLogin)
using CompanyName.ProjectName.Domain.Auth.DomainServices;
#endif

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

#if (IncludeExternalLogin)
        // 外部认证领域服务
        services.AddTransient<ExternalAuthDomainService>();
#endif

        return services;
    }
}
