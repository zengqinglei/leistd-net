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
    /// 用于明确不需要实际投递的环境，不作为发送失败时的兜底。
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddNullEmailSender();
    /// </code>
    /// </example>
    public static IServiceCollection AddNullEmailSender(this IServiceCollection services)
    {
        services.TryAddSingleton<IEmailSender, NullEmailSender>();
        return services;
    }
}
