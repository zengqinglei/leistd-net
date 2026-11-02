using Leistd.ExceptionHandling.Options;
using Leistd.Localization;
using Leistd.Webhooks.Definitions;
using Leistd.Webhooks.Errors;
using Leistd.Webhooks.ExceptionMappings;
using Leistd.Webhooks.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Leistd.Webhooks;

/// <summary>webhook 核心服务注册入口。</summary>
public static class DependencyInjection
{
    /// <summary>注册 webhook 核心能力：绑定并在启动期校验 <see cref="WebhookOptions"/>，登记事件类型定义、错误码的默认译文与默认 HTTP 状态。</summary>
    /// <remarks>
    /// <para>还需要存储（<c>AddWebhooksEfCore&lt;TDbContext&gt;()</c>，内部已调用本方法），以及宿主的
    /// <c>AddUnitOfWork()</c>；事件类型经 <c>AddSingleton&lt;IWebhookDefinitionProvider, XxxProvider&gt;()</c> 登记。</para>
    /// <para>校验消息以实际配置节的键开头。可重复调用：服务只注册一次，<paramref name="configure"/> 每次都叠加；
    /// 重复调用换用另一配置节时抛出 <see cref="InvalidOperationException"/>。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddWebhooks(options =&gt; options.AllowedNetworks.Add("10.0.0.0/8"));
    /// builder.Services.AddSingleton&lt;IWebhookDefinitionProvider, AppWebhookDefinitionProvider&gt;();
    /// </code>
    /// </example>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">编程式配置，在配置节绑定之后应用。</param>
    /// <param name="configSectionPath">配置节路径，校验消息按它报键名。</param>
    public static IServiceCollection AddWebhooks(
        this IServiceCollection services,
        Action<WebhookOptions>? configure = null,
        string configSectionPath = WebhookOptions.SectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configSectionPath);

        // 选项只有一份：换用另一配置节的重复调用会让校验消息报错键名
        if (services.Select(descriptor => descriptor.ImplementationInstance).OfType<WebhookOptionsValidator>().FirstOrDefault()
                is { } registered && registered.ConfigSectionPath != configSectionPath)
        {
            throw new InvalidOperationException(
                $"AddWebhooks() already binds '{registered.ConfigSectionPath}'; it cannot also bind '{configSectionPath}'.");
        }

        var options = services.AddOptions<WebhookOptions>().BindConfiguration(configSectionPath);
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<WebhookOptions>>(
            new WebhookOptionsValidator(configSectionPath)));

        // 事件类型索引是启动期事实，单例
        services.TryAddSingleton<IWebhookDefinitionManager, WebhookDefinitionManager>();

        services.AddJsonLocalizationResources(typeof(WebhookErrorCodes).Assembly);
        // 错误码的状态语义属于本组件默认值，在此登记，避免宿主漏配时静默回落成 400。
        services.Configure<GlobalExceptionOptions>(WebhookExceptionMappings.Configure);
        return services;
    }
}
