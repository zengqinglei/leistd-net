using Leistd.Webhooks.EntityFrameworkCore.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Leistd.Webhooks.EntityFrameworkCore;

/// <summary>webhook EF Core 持久化的注册与模型配置。</summary>
public static class DependencyInjection
{
    /// <summary>注册 EF Core 存储、管理用例、投递统计、提交后立即投递，并登记集群任务 <c>webhooks.dispatch</c>（兜底扫描）。</summary>
    /// <remarks>同时调用 Core 的 <c>AddWebhooks()</c>（幂等）。</remarks>
    /// <typeparam name="TDbContext">承载 webhook 表的 DbContext，须在 OnModelCreating 里调用 <see cref="ConfigureWebhooks"/>。</typeparam>
    /// <param name="services">服务集合。</param>
    public static IServiceCollection AddWebhooksEfCore<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
        => throw new NotImplementedException();

    /// <summary>启用 webhook 记录保留期：到期的事件、投递与尝试每天按物理库逐个删除，作为集群周期任务 <c>webhooks.retention</c> 执行。</summary>
    /// <remarks>
    /// 选项绑定 <paramref name="configSectionPath"/>（默认 <c>Leistd:Webhooks:Retention</c>）并在启动期校验，
    /// 重复调用换用另一配置节时抛出 <see cref="InvalidOperationException"/>；默认开启，保留 62 天。
    /// </remarks>
    /// <typeparam name="TDbContext">承载 webhook 表的 DbContext。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">在配置节之后应用的选项配置。</param>
    /// <param name="configSectionPath">选项绑定的配置节，校验消息按它报键名。</param>
    public static IServiceCollection AddWebhookRetention<TDbContext>(
        this IServiceCollection services,
        Action<WebhookRetentionOptions>? configure = null,
        string configSectionPath = WebhookRetentionOptions.SectionName)
        where TDbContext : DbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configSectionPath);

        // 选项只有一份：换用另一配置节的重复调用会让校验消息报错键名
        if (services.Select(descriptor => descriptor.ImplementationInstance).OfType<WebhookRetentionOptionsValidator>().FirstOrDefault()
                is { } registered && registered.ConfigSectionPath != configSectionPath)
        {
            throw new InvalidOperationException(
                $"AddWebhookRetention() already binds '{registered.ConfigSectionPath}'; it cannot also bind '{configSectionPath}'.");
        }

        var options = services.AddOptions<WebhookRetentionOptions>().BindConfiguration(configSectionPath);
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<WebhookRetentionOptions>>(
            new WebhookRetentionOptionsValidator(configSectionPath)));
        return services;
    }

    /// <summary>映射 webhook 的订阅、事件、投递与尝试四张表，在 OnModelCreating 中调用。</summary>
    /// <param name="modelBuilder">模型构建器。</param>
    public static ModelBuilder ConfigureWebhooks(this ModelBuilder modelBuilder)
        => throw new NotImplementedException();
}
