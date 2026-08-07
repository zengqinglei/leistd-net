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
using CompanyName.ProjectName.Application.Permissions.AppServices;
using CompanyName.ProjectName.Application.Permissions.Checker;
using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Application.Roles.AppServices;
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
        // 角色管理
        services.AddTransient<IRoleAppService, RoleAppService>();

        // 权限
        services.AddPermissionAuthorizationCore();
        // Scoped：一次请求内的主体解析结果被 PermissionSubjectProvider 与 IPermissionChecker 共享。
        services.AddScoped<IPermissionSubjectProvider, PermissionSubjectProvider>();
        services.AddSingleton<IPermissionDefinitionProvider, PermissionDefinitionProvider>();
        services.AddTransient<IPermissionAppService, PermissionAppService>();
#endif

        return services;
    }
}
