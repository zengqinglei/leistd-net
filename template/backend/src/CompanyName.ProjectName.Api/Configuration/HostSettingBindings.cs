using CompanyName.ProjectName.Api.Options;
using CompanyName.ProjectName.Application.Settings.Provider;
#if (IncludeOperationRecords)
using Leistd.OperationRecords.EntityFrameworkCore.Options;
#endif
using Leistd.Settings.Hosting;
#if (Email)
using Leistd.Email.Smtp.Options;
#endif
#if (IncludeNotifications)
using Leistd.Notifications.EntityFrameworkCore.Options;
#endif
#if (ExternalLogin)
using CompanyName.ProjectName.Infrastructure.Auth.OAuth.Options;
using Leistd.Settings.Hosting.Options;
#endif

namespace CompanyName.ProjectName.Api.Configuration;

/// <summary>哪些宿主级设置覆盖哪些配置键。</summary>
/// <remarks>
/// <para>新增一项运行期可改的部署配置只需三步：定义宿主级设置（带值域）、在这里加一行绑定、
/// 消费方注入 <c>IOptionsMonitor&lt;T&gt;</c>。属性名用 <c>nameof</c> 取，改名时编译期就能发现映射断了。</para>
/// <para>设置组件负责其余的事：设过的值作为优先级最高的配置源覆盖这些键，写入后本进程立即应用、其它实例周期跟上；
/// 这些设置的代码默认值取部署配置里的基线，清除覆盖值即回到部署配置。</para>
/// </remarks>
public static class HostSettingBindings
{
    /// <summary>全局最小日志级别的配置键：只覆盖 <c>Default</c>，按类别列出的覆盖不受影响。</summary>
    public const string LogLevelDefault = "Logging:LogLevel:Default";

    /// <summary>注册本项目的宿主级设置绑定。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="deploymentConfiguration">部署配置：遥测导出开关的兜底值由它算出（见 <see cref="TelemetryExportOptions.IsEnabledByDeployment"/>）。</param>
    public static IServiceCollection AddMyProjectHostSettings(this IServiceCollection services, IConfiguration deploymentConfiguration)
        => services.AddHostSettings(bindings =>
        {
            // 内置日志过滤随配置重载即时生效：覆盖这个键即改变全局级别，清除即回到部署配置
            bindings.Bind(SettingConstant.Logging.MinimumLevel, [LogLevelDefault], fallback: nameof(LogLevel.Information));
            bindings.BindOption<RequestLoggingOptions>(
                SettingConstant.Logging.RequestLogging, RequestLoggingOptions.SectionName, nameof(RequestLoggingOptions.Enabled));
            // 地址与认证头直接覆盖 OpenTelemetry 原本读的键，部署值即兜底；开关没有部署键时按部署地址是否配置推导
            bindings.Bind(
                SettingConstant.Telemetry.ExportEnabled,
                [TelemetryExportOptions.EnabledKey],
                fallback: TelemetryExportOptions.IsEnabledByDeployment(deploymentConfiguration) ? "true" : "false");
            bindings.Bind(SettingConstant.Telemetry.ExportEndpoint, [TelemetryExportOptions.EndpointKey]);
            bindings.Bind(SettingConstant.Telemetry.ExportHeaders, [TelemetryExportOptions.HeadersKey]);
#if (Email)
            bindings.BindOption<SmtpOptions>(SettingConstant.Email.SmtpHost, SmtpOptions.SectionName, nameof(SmtpOptions.Host));
            bindings.BindOption<SmtpOptions>(SettingConstant.Email.SmtpPort, SmtpOptions.SectionName, nameof(SmtpOptions.Port));
            bindings.BindOption<SmtpOptions>(SettingConstant.Email.SmtpEnableSsl, SmtpOptions.SectionName, nameof(SmtpOptions.EnableSsl));
            bindings.BindOption<SmtpOptions>(SettingConstant.Email.SmtpUsername, SmtpOptions.SectionName, nameof(SmtpOptions.Username));
            bindings.BindOption<SmtpOptions>(SettingConstant.Email.SmtpPassword, SmtpOptions.SectionName, nameof(SmtpOptions.Password));
            bindings.BindOption<SmtpOptions>(SettingConstant.Email.DefaultFromAddress, SmtpOptions.SectionName, nameof(SmtpOptions.DefaultFromAddress));
            bindings.BindOption<SmtpOptions>(SettingConstant.Email.DefaultFromName, SmtpOptions.SectionName, nameof(SmtpOptions.DefaultFromName));
#endif
#if (IncludeOperationRecords)
            bindings.BindOption<OperationRecordRetentionOptions>(
                SettingConstant.Audit.RetentionEnabled, OperationRecordRetentionOptions.SectionName, nameof(OperationRecordRetentionOptions.Enabled));
            bindings.BindOption<OperationRecordRetentionOptions>(
                SettingConstant.Audit.RetentionDays, OperationRecordRetentionOptions.SectionName, nameof(OperationRecordRetentionOptions.RetentionDays));
#endif
#if (IncludeNotifications)
            bindings.BindOption<NotificationRetentionOptions>(
                SettingConstant.NotificationRetention.Enabled, NotificationRetentionOptions.SectionName, nameof(NotificationRetentionOptions.Enabled));
            bindings.BindOption<NotificationRetentionOptions>(
                SettingConstant.NotificationRetention.ReadDays, NotificationRetentionOptions.SectionName, nameof(NotificationRetentionOptions.ReadRetentionDays));
            bindings.BindOption<NotificationRetentionOptions>(
                SettingConstant.NotificationRetention.UnreadDays, NotificationRetentionOptions.SectionName, nameof(NotificationRetentionOptions.UnreadRetentionDays));
#endif
#if (ExternalLogin)
            // 提供方是 ExternalAuth 下的嵌套节，BindOption 只映射顶层属性，这里直接列配置键。
            // 不参与选项的整组校验：凭据不全只让该提供方不可用（见 ExternalSchemeSynchronizer），不连带回滚同批的其他设置
            BindExternalLogin(bindings, "Github",
                SettingConstant.ExternalLogin.GithubEnabled, SettingConstant.ExternalLogin.GithubClientId, SettingConstant.ExternalLogin.GithubClientSecret);
            BindExternalLogin(bindings, "Google",
                SettingConstant.ExternalLogin.GoogleEnabled, SettingConstant.ExternalLogin.GoogleClientId, SettingConstant.ExternalLogin.GoogleClientSecret);
#endif
        });
#if (ExternalLogin)

    private static void BindExternalLogin(
        HostSettingBindingBuilder bindings, string provider, string enabled, string clientId, string clientSecret)
    {
        var section = $"{ExternalAuthOptions.SectionName}:{provider}";
        bindings.Bind(enabled, [$"{section}:{nameof(ExternalAuthOptions.ProviderOptions.Enabled)}"], fallback: "false");
        bindings.Bind(clientId, [$"{section}:{nameof(ExternalAuthOptions.ProviderOptions.ClientId)}"]);
        bindings.Bind(clientSecret, [$"{section}:{nameof(ExternalAuthOptions.ProviderOptions.ClientSecret)}"]);
    }
#endif
}
