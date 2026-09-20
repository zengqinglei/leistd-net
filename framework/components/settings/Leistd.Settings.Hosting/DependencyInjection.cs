using Leistd.BackgroundJobs;
using Leistd.BackgroundJobs.Recurring;
using Leistd.EventBus.EventHandlers;
using Leistd.Settings.Abstractions;
using Leistd.Settings.Events;
using Leistd.Settings.Hosting.Configuration;
using Leistd.Settings.Hosting.Definitions;
using Leistd.Settings.Hosting.Options;
using Leistd.Settings.Hosting.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Leistd.Settings.Hosting;

/// <summary>
/// 宿主级设置 → 配置源 → <c>IOptionsMonitor</c> 的注册入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 让宿主级设置覆盖部署配置：消费方照常注入 <c>IOptionsMonitor&lt;T&gt;</c>，不必知道值来自设置表。
    /// </summary>
    /// <remarks>
    /// <para>值在三处推进配置：宿主开始接收请求之前一次；写入宿主级设置的事务提交后本进程立即一次；
    /// 其余时候由每个副本上的 <c>EveryInstance</c> 周期任务按 <see cref="HostSettingOptions.RefreshInterval"/> 跟上
    /// （需要宿主注册调度器，如 <c>AddInProcessBackgroundJobs()</c>）。</para>
    /// <para>构建之后还要调用 <see cref="UseHostSettings{THost}"/> 把配置源挂上，漏了启动时抛出。
    /// 可重复调用，绑定累加。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddHostSettings(bindings => bindings
    ///     .Bind("Logging.MinimumLevel", ["Serilog:MinimumLevel", "Serilog:MinimumLevel:Default"], fallback: "Information")
    ///     .BindOption&lt;SmtpOptions&gt;("Email.SmtpHost", SmtpOptions.SectionName, nameof(SmtpOptions.Host)));
    ///
    /// var app = builder.Build();
    /// app.UseHostSettings();
    /// </code>
    /// </example>
    /// <param name="services">服务集合。</param>
    /// <param name="bind">声明绑定。</param>
    public static IServiceCollection AddHostSettings(
        this IServiceCollection services,
        Action<HostSettingBindingBuilder> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);

        services.AddOptions<HostSettingBindingCollection>()
            .Configure(collection => bind(new HostSettingBindingBuilder(collection.Bindings)));

        if (services.Any(descriptor => descriptor.ServiceType == typeof(HostSettingsConfigurationProvider)))
        {
            return services;
        }

        services.AddSingleton(new HostSettingsConfigurationProvider());
        services.AddSingleton<ISettingDefinitionProvider, HostSettingDefaultsProvider>();
        services.TryAddScoped<HostSettingApplier>();
        services.AddScoped<IEventHandler<SettingChangedEvent>, HostSettingChangedHandler>();
        services.AddHostedService<HostSettingStartupService>();

        services.AddOptions<HostSettingOptions>()
            .BindConfiguration(HostSettingOptions.SectionName)
            .Validate(options => options.RefreshInterval >= TimeSpan.FromSeconds(1),
                $"{HostSettingOptions.SectionName}:{nameof(HostSettingOptions.RefreshInterval)} must be at least one second.")
            .ValidateOnStart();
        services.AddRecurringJob<HostSettingRefreshJob>(
            HostSettingRefreshJob.Name,
            provider => RecurringJobSchedule.Every(provider.GetRequiredService<IOptions<HostSettingOptions>>().Value.RefreshInterval),
            RecurringJobScope.EveryInstance);

        return services;
    }

    /// <summary>
    /// 把宿主级设置作为优先级最高的配置源挂到宿主配置上。
    /// </summary>
    /// <remarks>
    /// 要在构建<b>之后</b>调用：构建期间还会追加配置源（例如测试宿主的覆盖配置），
    /// 在那之前挂上的话它们会排在后面、压过设置。配置在构建之后仍可追加，追加即触发一次重载。
    /// </remarks>
    /// <typeparam name="THost">宿主类型。</typeparam>
    /// <param name="host">已构建的宿主。</param>
    public static THost UseHostSettings<THost>(this THost host)
        where THost : IHost
    {
        ArgumentNullException.ThrowIfNull(host);

        var provider = host.Services.GetService<HostSettingsConfigurationProvider>()
            ?? throw new InvalidOperationException("Call AddHostSettings() before UseHostSettings().");
        if (provider.IsAttached)
        {
            return host;
        }

        if (host.Services.GetRequiredService<IConfiguration>() is not IConfigurationBuilder configuration)
        {
            throw new InvalidOperationException(
                "The host configuration does not accept new sources; host settings need a ConfigurationManager-based host.");
        }

        configuration.Add(new HostSettingsConfigurationSource(provider));
        return host;
    }
}
