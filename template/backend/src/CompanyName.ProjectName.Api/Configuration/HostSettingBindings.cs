using CompanyName.ProjectName.Api.Options;
using CompanyName.ProjectName.Application.Settings.Provider;
using Leistd.OperationRecords.EntityFrameworkCore.Options;
using Leistd.Settings.Hosting;
#if (LocalIdentity)
using Leistd.Email.Smtp.Options;
#endif

namespace CompanyName.ProjectName.Api.Configuration;

/// <summary>
/// 哪些宿主级设置覆盖哪些配置键。
/// </summary>
/// <remarks>
/// <para>新增一项运行期可改的部署配置只需三步：定义宿主级设置（带值域）、在这里加一行绑定、
/// 消费方注入 <c>IOptionsMonitor&lt;T&gt;</c>。属性名用 <c>nameof</c> 取，改名时编译期就能发现映射断了。</para>
/// <para>设置组件负责其余的事：设过的值作为优先级最高的配置源覆盖这些键，写入后本进程立即应用、其它实例周期跟上；
/// 这些设置的代码默认值取部署配置里的基线，清除覆盖值即回到部署配置。</para>
/// </remarks>
public static class HostSettingBindings
{
    /// <summary>Serilog 最小级别的配置节。</summary>
    public const string SerilogMinimumLevel = "Serilog:MinimumLevel";

    /// <summary>注册本项目的宿主级设置绑定。</summary>
    /// <param name="services">服务集合。</param>
    public static IServiceCollection AddMyProjectHostSettings(this IServiceCollection services)
        => services.AddHostSettings(bindings =>
        {
            // Serilog 的最小级别有标量与对象两种写法，它在启动时按配置源优先级选定一种并订阅其重载。
            // 两个键都写：无论它订阅的是哪一种，拿到的都是设置里的值；清除设置即回到部署配置。
            // 前提是部署配置里至少有一种写法（模板的 appsettings.json 自带），否则 Serilog 没有可订阅的键。
            bindings.Bind(
                SettingConstant.Logging.MinimumLevel,
                [SerilogMinimumLevel, $"{SerilogMinimumLevel}:Default"],
                fallback: nameof(Serilog.Events.LogEventLevel.Information));
            bindings.BindOption<RequestLoggingOptions>(
                SettingConstant.Logging.RequestLevel, RequestLoggingOptions.SectionName, nameof(RequestLoggingOptions.Level));
#if (LocalIdentity)
            bindings.BindOption<SmtpOptions>(SettingConstant.Email.SmtpHost, SmtpOptions.SectionName, nameof(SmtpOptions.Host));
            bindings.BindOption<SmtpOptions>(SettingConstant.Email.SmtpPort, SmtpOptions.SectionName, nameof(SmtpOptions.Port));
            bindings.BindOption<SmtpOptions>(SettingConstant.Email.SmtpEnableSsl, SmtpOptions.SectionName, nameof(SmtpOptions.EnableSsl));
            bindings.BindOption<SmtpOptions>(SettingConstant.Email.SmtpUsername, SmtpOptions.SectionName, nameof(SmtpOptions.Username));
            bindings.BindOption<SmtpOptions>(SettingConstant.Email.SmtpPassword, SmtpOptions.SectionName, nameof(SmtpOptions.Password));
            bindings.BindOption<SmtpOptions>(SettingConstant.Email.DefaultFromAddress, SmtpOptions.SectionName, nameof(SmtpOptions.DefaultFromAddress));
            bindings.BindOption<SmtpOptions>(SettingConstant.Email.DefaultFromName, SmtpOptions.SectionName, nameof(SmtpOptions.DefaultFromName));
#endif
            bindings.BindOption<OperationRecordRetentionOptions>(
                SettingConstant.Audit.RetentionEnabled, OperationRecordRetentionOptions.SectionName, nameof(OperationRecordRetentionOptions.Enabled));
            bindings.BindOption<OperationRecordRetentionOptions>(
                SettingConstant.Audit.RetentionDays, OperationRecordRetentionOptions.SectionName, nameof(OperationRecordRetentionOptions.RetentionDays));
        });
}
