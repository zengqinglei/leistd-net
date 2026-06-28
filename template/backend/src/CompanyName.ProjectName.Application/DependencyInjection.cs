#if (IncludeIdentity)
using CompanyName.ProjectName.Application.Auth.AppServices;
#if (IncludeOpenIddict)
using CompanyName.ProjectName.Application.OpenApplications.AppServices;
#endif
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

#if (IncludeOpenIddict)
        // OAuth token 主体工厂 + 开放应用管理（仅 OpenIddict）
        services.AddScoped<IAuthPrincipalFactory, AuthPrincipalFactory>();
        services.AddScoped<IOpenApplicationAppService, OpenApplicationAppService>();
#endif

#if (IncludeExternalLogin)
        // 外部认证（仅 ExternalLogin）
        services.AddScoped<IExternalAuthAppService, ExternalAuthAppService>();
#endif
#endif

        // 用户管理
        services.AddScoped<IUserAppService, UserAppService>();

#if (IncludeRoles)
        // 权限
        services.AddScoped<IPermissionChecker, PermissionChecker>();
        services.AddSingleton<IPermissionDefinitionProvider, PermissionDefinitionProvider>();
        services.AddSingleton<IPermissionDefinitionManager, PermissionDefinitionManager>();
        // 将权限定义接入微软授权 Policy 管道，使 [Authorize(Policy = "权限名")] 生效
        services.AddPermissionAuthorization();
#endif

        return services;
    }
}
