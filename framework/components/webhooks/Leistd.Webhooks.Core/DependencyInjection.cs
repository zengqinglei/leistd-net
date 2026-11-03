using Leistd.ExceptionHandling.Options;
using Leistd.Localization;
using Leistd.Webhooks.Definitions;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.Errors;
using Leistd.Webhooks.ExceptionMappings;
using Leistd.Webhooks.Management;
using Leistd.Webhooks.Options;
using Leistd.Webhooks.Signing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Leistd.Webhooks;

/// <summary>webhook 核心服务注册入口。</summary>
public static class DependencyInjection
{
    /// <summary>注册 webhook 核心能力：绑定并在启动期校验 <see cref="WebhookOptions"/>，登记事件类型定义、默认 HTTP 发送器
    /// （<see cref="IWebhookSender"/>）、管理用例（<see cref="IWebhookManagementService"/>）、错误码的默认译文与默认 HTTP 状态。</summary>
    /// <remarks>
    /// <para>还需要存储（<c>AddWebhooksEfCore&lt;TDbContext&gt;()</c>，内部已调用本方法；管理用例经它读写，<c>MapWebhooks</c> 映射时检查），
    /// 以及宿主的 <c>AddUnitOfWork()</c> 与 <c>AddDataProtection()</c>（签名密钥加密存储）；事件类型经
    /// <c>AddSingleton&lt;IWebhookDefinitionProvider, XxxProvider&gt;()</c> 登记。</para>
    /// <para>校验消息以实际配置节的键开头。可重复调用：服务只注册一次，<paramref name="configure"/> 每次都叠加；
    /// 重复调用换用另一配置节时抛出 <see cref="InvalidOperationException"/>。</para>
    /// <para>默认发送器经命名客户端 <c>Leistd.Webhooks</c> 发出请求，以 TryAdd 登记，宿主先注册或之后 Replace 自己的
    /// <see cref="IWebhookSender"/> 均可覆盖。该客户端不走系统代理：内网拦截按连接目标的实际地址判断。</para>
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
        var registered = services.Select(descriptor => descriptor.ImplementationInstance).OfType<WebhookOptionsValidator>().FirstOrDefault();
        if (registered is not null && registered.ConfigSectionPath != configSectionPath)
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

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<WebhookSigner>();
        services.TryAddSingleton<WebhookAttemptClassifier>();
        services.TryAddSingleton(provider => new WebhookNetworkGuard(
            provider.GetRequiredService<IOptionsMonitor<WebhookOptions>>(), configSectionPath));
        services.TryAddTransient<IWebhookSender, WebhookHttpSender>();
        services.TryAddSingleton<WebhookSecretProtector>();
        services.TryAddTransient<IWebhookManagementService, WebhookManagementService>();

        // 命名客户端的配置是追加式的，只在首次调用时登记一遍
        if (registered is null)
        {
            services.AddHttpClient(WebhookHttpSender.HttpClientName)
                // 超时由发送器按 RequestTimeout 逐次控制（可热更新），客户端自身不限时
                .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
                .ConfigurePrimaryHttpMessageHandler(provider => new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    // 走代理时连接目标是代理本身，内网拦截就看不到真实地址
                    UseProxy = false,
                    UseCookies = false,
                    ConnectCallback = provider.GetRequiredService<WebhookNetworkGuard>().ConnectAsync
                });
        }

        services.AddJsonLocalizationResources(typeof(WebhookErrorCodes).Assembly);
        // 错误码的状态语义属于本组件默认值，在此登记，避免宿主漏配时静默回落成 400。
        services.Configure<GlobalExceptionOptions>(WebhookExceptionMappings.Configure);
        return services;
    }
}
