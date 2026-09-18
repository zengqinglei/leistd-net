using Leistd.Settings.Abstractions;
using Leistd.OperationRecords.Abstractions;
using CompanyName.ProjectName.Application.OperationRecords;
using CompanyName.ProjectName.Application.Settings.Provider;
#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.Policies;
#endif
using CompanyName.ProjectName.Application.Settings.AppServices;
using CompanyName.ProjectName.Application.Settings.Timing;
#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.AppServices;
using CompanyName.ProjectName.Application.TenantConnections.AppServices;
#if (OpenIddictServer)
using CompanyName.ProjectName.Application.OpenApplications.AppServices;
#endif
#endif
using CompanyName.ProjectName.Application.Initialization;
using CompanyName.ProjectName.Application.Users.AppServices;
using Leistd.ObjectMapping.Mapster;
using Leistd.Authorization;
using CompanyName.ProjectName.Application.Permissions.AppServices;
using CompanyName.ProjectName.Application.Permissions.Checker;
using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Application.OperationRecords.AppServices;
using CompanyName.ProjectName.Application.Roles.AppServices;
#if (LocalIdentity)
using CompanyName.ProjectName.Application.Tenants;
using CompanyName.ProjectName.Application.Tenants.AppServices;
#endif
using Microsoft.Extensions.DependencyInjection;
using Leistd.Authorization.Abstractions;

namespace CompanyName.ProjectName.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddMapsterObjectMapper(options =>
        {
            options.AddProfiles(typeof(DependencyInjection).Assembly);
        });

        services.AddTransient<ISystemInitializer, SystemInitializer>();

#if (LocalIdentity)
        services.AddTransient<ICaptchaAppService, CaptchaAppService>();
        services.AddTransient<IEmailVerificationAppService, EmailVerificationAppService>();
        services.AddTransient<SessionSignInService>();
        services.AddTransient<IAuthAppService, AuthAppService>();

#if (OpenIddictServer)
        // OAuth 主体工厂与开放应用管理仅供自签发令牌模式使用。
        services.AddTransient<IAuthPrincipalFactory, AuthPrincipalFactory>();
        services.AddTransient<IOpenApplicationAppService, OpenApplicationAppService>();
#endif
        // 租户连接配置管理随租户控制面存在（外层 LocalIdentity 即是），与是否签发令牌无关。
        // 不能放进上面那个"仅自签发令牌"的块：Controller 与应用服务类型受 LocalIdentity 保护，
        // 注册若更窄，Cookie 会话形态下 Controller 在而注册不在，请求以 500 收场
        services.AddTransient<ITenantConnectionAppService, TenantConnectionAppService>();

#if (ExternalLogin)
        services.AddTransient<IExternalAuthAppService, ExternalAuthAppService>();
#endif
#endif

        services.AddTransient<IUserAppService, UserAppService>();

        services.AddTransient<IRoleAppService, RoleAppService>();

        services.AddPermissionAuthorizationCore();
        // Scoped：一次请求内的主体解析结果被 PermissionSubjectProvider 与 IPermissionChecker 共享。
        services.AddScoped<IPermissionSubjectProvider, PermissionSubjectProvider>();
        services.AddSingleton<IPermissionDefinitionProvider, PermissionDefinitionProvider>();
        services.AddSingleton<ISettingDefinitionProvider, SettingDefinitionProvider>();
        // 动作定义与权限、设置同属"启动期一次性登记"的定义族，注册方式与它们一致。
        // 不注册的话管理器拿到空索引：界面按"未登记码"降级为原样显示裸码，
        // 症状是页面照常能用、只是动作列全是机器码——不会报错，所以很容易漏。
        services.AddSingleton<IOperationActionDefinitionProvider, OperationActionDefinitionProvider>();
        services.AddTransient<IPermissionAppService, PermissionAppService>();
        // 设置对所有服务形态都开放：Controller 与设置页在 Resource 模式下同样保留，
        // 少了这条注册，认证用户一访问 /api/v1/settings 就因解析不到构造参数返回 500。
        services.AddTransient<ISettingAppService, SettingAppService>();
        // 服务端产出给人看的时间文本时注入它；DTO 保持 UTC 交给前端渲染，不必经过这里。
        services.AddTransient<IUserTimeZoneProvider, UserTimeZoneProvider>();
        // 操作记录对所有服务形态开放：Resource 形态同样有带策略的写端点，被拒与成功都要能查。
        services.AddTransient<IOperationRecordAppService, OperationRecordAppService>();
#if (LocalIdentity)
        // 注册策略按租户从设置里解析；appsettings 仍是部署基线（设置定义的默认值取自它）。
        services.AddTransient<IUserRegistrationPolicyProvider, UserRegistrationPolicyProvider>();
#endif

#if (LocalIdentity)
        services.AddTransient<ITenantAppService, TenantAppService>();
        services.AddTransient<ITenantSeeder, TenantSeeder>();
        services.AddTransient<ITenantImpersonationAppService, TenantImpersonationAppService>();
#endif

        return services;
    }
}
