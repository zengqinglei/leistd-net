using CompanyName.ProjectName.Api.Hosting.ExceptionMappings;
using Leistd.Authorization.AspNetCore.ExceptionMappings;
using Leistd.ExceptionHandling.AspNetCore.Options;
using Leistd.MultiTenancy.AspNetCore.ExceptionMappings;
using Leistd.Settings.AspNetCore.ExceptionMappings;
#if (ServiceUserContextEnabled)
using Leistd.ServiceClient.AspNetCore.ExceptionMappings;
#endif
#if (IncludeNotifications)
using Leistd.Notifications.AspNetCore.ExceptionMappings;
#endif

namespace CompanyName.ProjectName.Api.Hosting;

/// <summary>组合业务模块与组件拥有的非默认 HTTP 错误映射。</summary>
internal static class ApiExceptionMappings
{
    public static void Configure(GlobalExceptionOptions options)
    {
        AuthorizationExceptionMappings.Configure(options);
        MultiTenancyExceptionMappings.Configure(options);
        SettingsExceptionMappings.Configure(options);
#if (IncludeNotifications)
        NotificationExceptionMappings.Configure(options);
#endif
#if (LocalIdentity)
        AuthExceptionMappings.Configure(options);
        TenantExceptionMappings.Configure(options);
#endif
#if (OpenIddictServer)
        OpenApplicationExceptionMappings.Configure(options);
#endif
        UserExceptionMappings.Configure(options);
        RoleExceptionMappings.Configure(options);
        AppSettingExceptionMappings.Configure(options);
#if (ServiceUserContextEnabled)
        ServiceClientExceptionMappings.Configure(options);
#endif
    }

    internal static void Map(GlobalExceptionOptions options, int statusCode, params string[] codes)
    {
        foreach (var code in codes)
            options.MapCode(code, statusCode);
    }
}
