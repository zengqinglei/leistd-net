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
using Leistd.Authorization;
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
        services.AddTransient<ICaptchaAppService, CaptchaAppService>();
        services.AddTransient<IEmailVerificationAppService, EmailVerificationAppService>();
        services.AddTransient<IAuthAppService, AuthAppService>();

#if (IncludeOpenIddict)
        // OAuth token 主体工厂 + 开放应用管理（仅 OpenIddict）
        services.AddTransient<IAuthPrincipalFactory, AuthPrincipalFactory>();
        services.AddTransient<IOpenApplicationAppService, OpenApplicationAppService>();
#endif

#if (IncludeExternalLogin)
        // 外部认证（仅 ExternalLogin）
        services.AddTransient<IExternalAuthAppService, ExternalAuthAppService>();
#endif
#endif

        // 用户管理
        services.AddTransient<IUserAppService, UserAppService>();

#if (IncludeRoles)
        // 权限
        services.AddPermissionAuthorizationCore();
        services.AddTransient<IPermissionSubjectProvider, PermissionSubjectProvider>();
        services.AddSingleton<IPermissionDefinitionProvider, PermissionDefinitionProvider>();
#endif

        return services;
    }
}
