#if (IncludeIdentity)
using CompanyName.ProjectName.Application.Auth.AppServices;
using CompanyName.ProjectName.Application.OpenApplications.AppServices;
#endif
using CompanyName.ProjectName.Application.Initialization;
using CompanyName.ProjectName.Application.Users.AppServices;
using Leistd.ObjectMapping.Mapster;
#if (IncludeRoles)
using Leistd.Ddd.Application.Permission;
using CompanyName.ProjectName.Application.Permissions.Checker;
using CompanyName.ProjectName.Application.Permissions.Provider;
#endif
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddMapsterObjectMapper(options =>
        {
            options.AddProfiles(typeof(DependencyInjection).Assembly);
        });

        // 系统初始化
        services.AddTransient<ISystemInitializer, SystemInitializer>();

#if (IncludeIdentity)
        // 认证
        services.AddScoped<ICaptchaAppService, CaptchaAppService>();
        services.AddScoped<IEmailVerificationAppService, EmailVerificationAppService>();
        services.AddScoped<IAuthAppService, AuthAppService>();
        services.AddScoped<IAuthPrincipalFactory, AuthPrincipalFactory>();

        // OAuth 应用管理
        services.AddScoped<IOpenApplicationAppService, OpenApplicationAppService>();

        // 外部认证
        services.AddScoped<IExternalAuthAppService, ExternalAuthAppService>();
#endif

        // 用户管理
        services.AddScoped<IUserAppService, UserAppService>();

#if (IncludeRoles)
        // 权限
        services.AddScoped<IPermissionChecker, PermissionChecker>();
        services.AddSingleton<IPermissionDefinitionProvider, PermissionDefinitionProvider>();
        services.AddSingleton<IPermissionDefinitionManager, PermissionDefinitionManager>();
#endif

        return services;
    }
}
