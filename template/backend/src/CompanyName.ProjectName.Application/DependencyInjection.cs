using Leistd.Settings.Abstractions;
using Leistd.Settings.Events;
using Leistd.Settings.Validation;
using Leistd.OperationRecords.Abstractions;
using CompanyName.ProjectName.Application.OperationRecords.EventHandlers;
using CompanyName.ProjectName.Application.Settings.Provider;
using CompanyName.ProjectName.Application.Settings.Validators;
#if (LocalIdentity)
using CompanyName.ProjectName.Application.Settings.AppServices;
#endif
#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.Policies;
#endif
using CompanyName.ProjectName.Application.Settings.Timing;
#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.AppServices;
using CompanyName.ProjectName.Application.Auth.SecurityAlerts;
using CompanyName.ProjectName.Application.Auth.EventHandlers;
using CompanyName.ProjectName.Application.Auth.Sessions;
using CompanyName.ProjectName.Application.Auth.SignIn;
using CompanyName.ProjectName.Application.Auth.TwoFactor;
using CompanyName.ProjectName.Domain.Auth.Events;
#if (OpenIddictServer)
using CompanyName.ProjectName.Application.OpenApplications.AppServices;
#endif
#endif
using CompanyName.ProjectName.Application.Initialization;
using CompanyName.ProjectName.Application.Users.AppServices;
using Leistd.ObjectMapping.Mapster;
using Leistd.Authorization;
using Leistd.Authorization.Events;
using CompanyName.ProjectName.Application.OperationRecords.Provider;
using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Application.Roles.AppServices;
#if (LocalIdentity)
using CompanyName.ProjectName.Application.Tenants;
using CompanyName.ProjectName.Application.Tenants.AppServices;
using Leistd.MultiTenancy.Events;
using Leistd.MultiTenancy.Provisioning;
#endif
using Leistd.EventBus.EventHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        services.AddTransient<IUserSessionValidator, UserSessionValidator>();
        // 会话撤销后作废它的校验缓存（事务提交后由本地事件总线分发）
        services.AddTransient<IEventHandler<UserSessionRevokedEvent>, UserSessionRevokedEventHandler>();
        services.AddTransient<IUserSessionAppService, UserSessionAppService>();
        // 安全提醒默认不发；启用通知时宿主换成经通知组件发布的实现
        services.TryAddTransient<ISecurityAlertPublisher, NullSecurityAlertPublisher>();
        services.AddTransient<TwoFactorChallengeStore>();
        services.AddTransient<ITwoFactorAppService, TwoFactorAppService>();
        services.AddTransient<IAuthAppService, AuthAppService>();

#if (OpenIddictServer)
        // OAuth 主体工厂与开放应用管理仅供自签发令牌模式使用。
        services.AddTransient<IAuthPrincipalFactory, AuthPrincipalFactory>();
        services.AddTransient<IOpenApplicationAppService, OpenApplicationAppService>();
#endif
#if (ExternalLogin)
        services.AddTransient<IExternalAuthAppService, ExternalAuthAppService>();
#endif
#endif

        services.AddTransient<IUserAppService, UserAppService>();

        services.AddTransient<IRoleAppService, RoleAppService>();

        services.AddPermissionAuthorizationCore();
        // Scoped：一次请求内的主体解析结果被 PermissionSubjectProvider 与 IPermissionChecker 共享。
        services.AddScoped<IPermissionSubjectProvider, PermissionSubjectProvider>();
        // 权限管理用例（端点由 Api 映射）经它确认主体存在、取显示名
        services.AddTransient<IPermissionSubjectDirectory, PermissionSubjectDirectory>();
        services.AddSingleton<IPermissionDefinitionProvider, PermissionDefinitionProvider>();
        services.AddSingleton<ISettingDefinitionProvider, SettingDefinitionProvider>();
        // 动作定义与权限、设置同属"启动期一次性登记"的定义族，注册方式与它们一致。
        // 不注册的话管理器拿到空索引：界面按"未登记码"降级为原样显示裸码，
        // 症状是页面照常能用、只是动作列全是机器码——不会报错，所以很容易漏。
        services.AddSingleton<IOperationActionDefinitionProvider, OperationActionDefinitionProvider>();
        // 设置值的业务校验：值域（布尔、区间、候选）写在定义上，这里只放定义表达不了的规则
        services.AddTransient<ISettingValueValidator, TimeZoneSettingValidator>();
#if (LocalIdentity)
        services.AddTransient<ISettingValueValidator, EmailSettingValidator>();
        services.AddTransient<IEmailSettingsAppService, EmailSettingsAppService>();
#endif
        // 组件写入后发布的事件转成本项目的操作记录（提交后分发，回滚的写入不留痕）
        services.AddTransient<IEventHandler<SettingChangedEvent>, SettingChangedAuditHandler>();
        services.AddTransient<IEventHandler<PermissionGrantsReplacedEvent>, PermissionGrantsReplacedAuditHandler>();
        // 服务端产出给人看的时间文本时注入它；DTO 保持 UTC 交给前端渲染，不必经过这里。
        services.AddTransient<IUserTimeZoneProvider, UserTimeZoneProvider>();
#if (LocalIdentity)
        // 注册策略按租户从设置里解析；appsettings 仍是部署基线（设置定义的默认值取自它）。
        services.AddTransient<IUserRegistrationPolicyProvider, UserRegistrationPolicyProvider>();
        services.AddTransient<ILoginSecurityPolicyProvider, LoginSecurityPolicyProvider>();
#endif

#if (LocalIdentity)
        // 租户管理的编排与补偿在多租户组件里；本项目只负责开通内容与启用前置条件
        services.AddTransient<ITenantProvisioner, TenantSeeder>();
        services.AddTransient<ITenantActivationGuard, TenantHasUsersActivationGuard>();
        services.AddTransient<IEventHandler<TenantChangedEvent>, TenantChangedAuditHandler>();
        services.AddTransient<IEventHandler<TenantConnectionChangedEvent>, TenantConnectionChangedAuditHandler>();
        services.AddTransient<ITenantImpersonationAppService, TenantImpersonationAppService>();
#endif

        return services;
    }
}
