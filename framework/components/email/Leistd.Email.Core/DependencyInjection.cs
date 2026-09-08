using Leistd.Email.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Leistd.Email;

/// <summary>
/// 提供空邮件发送器的注册入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 <see cref="NullEmailSender"/>：不真正发信，只按 Warning 记录。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <remarks>
    /// 仅用于没有可用 SMTP 的环境，且必须是宿主的主动选择。生产环境注册它等于静默丢信。
    /// </remarks>
    /// <example>
    /// <code>
    /// if (builder.Environment.IsDevelopment())
    /// {
    ///     builder.Services.AddNullEmailSender();
    /// }
    /// else
    /// {
    ///     builder.Services.AddSmtpEmailSender(builder.Configuration);
    /// }
    /// </code>
    /// </example>
    public static IServiceCollection AddNullEmailSender(this IServiceCollection services)
    {
        services.TryAddSingleton<IEmailSender, NullEmailSender>();
        return services;
    }
}
