using Leistd.Email.Abstractions;
using Leistd.Email.Smtp.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Leistd.Email.Smtp;

/// <summary>
/// 提供 SMTP 邮件发送器的注册入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 SMTP 邮件发送器并绑定 <c>Leistd:Email:Smtp</c> 配置节。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">应用配置</param>
    /// <example>
    /// <code>
    /// builder.Services.AddSmtpEmailSender(builder.Configuration);
    /// </code>
    /// </example>
    public static IServiceCollection AddSmtpEmailSender(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        return services.AddSmtpEmailSenderCore();
    }

    /// <summary>
    /// 使用委托配置注册 SMTP 邮件发送器。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configure">选项配置委托</param>
    public static IServiceCollection AddSmtpEmailSender(
        this IServiceCollection services,
        Action<SmtpOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }

        return services.AddSmtpEmailSenderCore();
    }

    private static IServiceCollection AddSmtpEmailSenderCore(this IServiceCollection services)
    {
        // 配置错了的代价是"用户已经点了发送"：注册验证码这类流程会把一个永远收不到码的
        // 挑战交给用户。必须在接流量之前失败。
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<SmtpOptions>, SmtpOptionsValidator>());
        services.AddOptions<SmtpOptions>().ValidateOnStart();

        // 实现类型只注册一次，接口作别名转发，避免重复调用产生两份实例。
        services.TryAddSingleton<SmtpEmailSender>();
        services.TryAddSingleton<IEmailSender>(sp => sp.GetRequiredService<SmtpEmailSender>());

        return services;
    }
}
