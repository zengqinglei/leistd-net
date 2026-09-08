using Leistd.MultiTenancy;
using Leistd.Security.Users;
using Leistd.ServiceClient.Handlers;
using Leistd.ServiceClient.Options;
using Leistd.Tracing.Options;
using Leistd.Tracing.Services;
using Leistd.Tracing.HttpClient.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Leistd.MultiTenancy.Abstractions;
using Leistd.Tracing.Abstractions;

namespace Leistd.ServiceClient;

/// <summary>
/// 服务客户端注册入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 服务客户端配置节前缀：每个下游服务绑定 <c>Leistd:ServiceClients:&lt;服务名&gt;</c>。
    /// </summary>
    public const string ConfigurationSectionPrefix = "Leistd:ServiceClients";

    /// <summary>
    /// 日志类别前缀：每个客户端的日志类别为 <c>Leistd.ServiceClient.&lt;服务名&gt;</c>。
    /// </summary>
    public const string LoggerCategoryPrefix = "Leistd.ServiceClient";

    /// <summary>
    /// 注册强类型服务客户端并装配标准调用管道。
    /// </summary>
    /// <remarks>
    /// TraceId 透传与用户上下文注入是<b>跟随宿主显式组合</b>的可选能力：
    /// 宿主注册了 <c>AddCorrelationIdCore</c>（Leistd.Tracing）才透传 TraceId；
    /// 注册了 <c>ICurrentUser</c>（如 Leistd.Security 的 <c>AddSecurity()</c>）才注入用户头。
    /// 未注册时对应环节自动直通，本方法不代为注册。
    /// </remarks>
    /// <typeparam name="TClient">客户端接口</typeparam>
    /// <typeparam name="TImplementation">客户端实现</typeparam>
    /// <typeparam name="TOptions">客户端配置类型（每个客户端一个具体类型）</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="serviceName">下游服务名：命名 HttpClient、配置节与日志类别</param>
    /// <param name="configuration">应用配置</param>
    /// <returns><see cref="IHttpClientBuilder"/>，可继续追加认证（OAuth 包）、弹性等处理器</returns>
    /// <example>
    /// <code>
    /// builder.Services
    ///     .AddServiceClient&lt;IIdentityApi, IdentityApiClient, IdentityClientOptions&gt;(
    ///         "identity", builder.Configuration)
    ///     .AddClientCredentials(builder.Configuration);
    /// </code>
    /// </example>
    public static IHttpClientBuilder AddServiceClient<TClient, TImplementation, TOptions>(
        this IServiceCollection services,
        string serviceName,
        IConfiguration configuration)
        where TClient : class
        where TImplementation : class, TClient
        where TOptions : ServiceClientOptions, new()
    {
        services.Configure<TOptions>(configuration.GetSection($"{ConfigurationSectionPrefix}:{serviceName}"));
        return AddServiceClientCore<TClient, TImplementation, TOptions>(services, serviceName);
    }

    /// <summary>
    /// 注册强类型服务客户端（委托配置版）。行为同
    /// <see cref="AddServiceClient{TClient,TImplementation,TOptions}(IServiceCollection,string,IConfiguration)"/>。
    /// </summary>
    /// <typeparam name="TClient">客户端接口</typeparam>
    /// <typeparam name="TImplementation">客户端实现</typeparam>
    /// <typeparam name="TOptions">客户端配置类型（每个客户端一个具体类型）</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="serviceName">下游服务名：命名 HttpClient、配置节与日志类别</param>
    /// <param name="configureOptions">配置委托</param>
    public static IHttpClientBuilder AddServiceClient<TClient, TImplementation, TOptions>(
        this IServiceCollection services,
        string serviceName,
        Action<TOptions> configureOptions)
        where TClient : class
        where TImplementation : class, TClient
        where TOptions : ServiceClientOptions, new()
    {
        services.Configure(configureOptions);
        return AddServiceClientCore<TClient, TImplementation, TOptions>(services, serviceName);
    }

    private static IHttpClientBuilder AddServiceClientCore<TClient, TImplementation, TOptions>(
        IServiceCollection services,
        string serviceName)
        where TClient : class
        where TImplementation : class, TClient
        where TOptions : ServiceClientOptions, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        return services.AddHttpClient<TClient, TImplementation>(serviceName)
            .AddServiceClientPipeline<TOptions>(serviceName);
    }

    /// <summary>
    /// 在现有客户端构建器上装配服务客户端标准管道。
    /// </summary>
    /// <remarks>依次应用日志、链路标识、用户上下文和租户上下文处理器。</remarks>
    /// <typeparam name="TOptions">客户端配置类型（须已绑定，如经 <c>services.Configure</c>）</typeparam>
    /// <param name="builder">HttpClient 构建器</param>
    /// <param name="serviceName">下游服务名（日志类别后缀）</param>
    public static IHttpClientBuilder AddServiceClientPipeline<TOptions>(
        this IHttpClientBuilder builder,
        string serviceName)
        where TOptions : ServiceClientOptions, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        builder.ConfigureHttpClient((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<TOptions>>().Value;
            if (!string.IsNullOrEmpty(options.BaseAddress))
            {
                var baseAddress = options.BaseAddress.EndsWith('/')
                    ? options.BaseAddress
                    : options.BaseAddress + "/";
                client.BaseAddress = new Uri(baseAddress);
            }

            client.Timeout = options.Timeout;
        });

        // 日志必须位于最外层，才能覆盖认证重试在内的完整调用。
        builder.AddHttpMessageHandler(provider =>
        {
            var options = provider.GetRequiredService<IOptions<TOptions>>().Value;
            var logger = provider.GetRequiredService<ILoggerFactory>()
                .CreateLogger($"{LoggerCategoryPrefix}.{serviceName}");
            return new ServiceClientLoggingHandler(logger, serviceName, options.LogPayloads, options.MaxPayloadLength);
        });

        // 可选组件未注册时使用直通处理器，保持宿主显式组合。
        builder.AddHttpMessageHandler(provider =>
            provider.GetService<ICorrelationIdProvider>() is { } correlationIdProvider
                ? new CorrelationIdDelegatingHandler(
                    correlationIdProvider,
                    provider.GetRequiredService<IOptionsMonitor<CorrelationIdOptions>>())
                : new PassthroughDelegatingHandler());

        builder.AddHttpMessageHandler(provider =>
        {
            return provider.GetService<ICurrentUser>() is { } currentUser
                ? new UserContextDelegatingHandler<TOptions>(
                    currentUser,
                    provider.GetRequiredService<IOptionsMonitor<TOptions>>())
                : new PassthroughDelegatingHandler();
        });

        // 租户转发独立于用户转发，后台任务可能只有租户上下文。
        builder.AddHttpMessageHandler(provider =>
        {
            return provider.GetService<ICurrentTenant>() is { } currentTenant
                ? new TenantContextDelegatingHandler<TOptions>(
                    currentTenant,
                    provider.GetRequiredService<IOptionsMonitor<TOptions>>())
                : new PassthroughDelegatingHandler();
        });

        return builder;
    }
}
