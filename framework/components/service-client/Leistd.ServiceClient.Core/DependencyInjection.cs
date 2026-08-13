using Leistd.MultiTenancy;
using Leistd.Security.Users;
using Leistd.ServiceClient.Handlers;
using Leistd.ServiceClient.Options;
using Leistd.Tracing.Core.Options;
using Leistd.Tracing.Core.Services;
using Leistd.Tracing.HttpClient.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    /// 注册强类型服务客户端，装配标准调用管道（自外向内）：
    /// 调用日志 → TraceId 透传 → 用户上下文头注入 →（可选，见 OAuth 包）Bearer 认证。
    /// Options 绑定配置节 <c>Leistd:ServiceClients:&lt;serviceName&gt;</c>。
    /// </summary>
    /// <remarks>
    /// TraceId 透传与用户上下文注入是**跟随宿主显式组合**的可选能力：
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
    /// 在既有 <see cref="IHttpClientBuilder"/> 上装配服务客户端标准能力：
    /// 从 <typeparamref name="TOptions"/> 应用 BaseAddress / Timeout，并按序挂载
    /// 调用日志 → TraceId 透传 → 用户上下文头注入。供 Refit 等其他客户端注册形态复用
    /// （如 <c>AddRefitClient&lt;TApi&gt;(...).AddServiceClientPipeline&lt;TOptions&gt;(serviceName)</c>）。
    /// </summary>
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

        // 1. 调用日志（最外层：覆盖含认证重试在内的完整调用）
        builder.AddHttpMessageHandler(provider =>
        {
            var options = provider.GetRequiredService<IOptions<TOptions>>().Value;
            var logger = provider.GetRequiredService<ILoggerFactory>()
                .CreateLogger($"{LoggerCategoryPrefix}.{serviceName}");
            return new ServiceClientLoggingHandler(logger, serviceName, options.LogPayloads, options.MaxPayloadLength);
        });

        // 2. TraceId 透传（复用 Leistd.Tracing.HttpClient；宿主未注册 tracing 时直通）
        builder.AddHttpMessageHandler(provider =>
            provider.GetService<ICorrelationIdProvider>() is { } correlationIdProvider
                ? new CorrelationIdDelegatingHandler(
                    correlationIdProvider,
                    provider.GetRequiredService<IOptions<CorrelationIdOptions>>())
                : new PassthroughDelegatingHandler());

        // 3. 用户上下文头注入（宿主未注册 ICurrentUser 或 Options 关闭时直通）
        builder.AddHttpMessageHandler(provider =>
        {
            var options = provider.GetRequiredService<IOptions<TOptions>>().Value;
            return options.UserContext.Enable && provider.GetService<ICurrentUser>() is { } currentUser
                ? new UserContextDelegatingHandler(currentUser, options.UserContext)
                : new PassthroughDelegatingHandler();
        });

        // 4. 租户上下文头注入（宿主未注册 ICurrentTenant 或 Options 关闭时直通）；
        //    独立于用户头开关：后台任务可能只有租户上下文而无用户主体
        builder.AddHttpMessageHandler(provider =>
        {
            var options = provider.GetRequiredService<IOptions<TOptions>>().Value;
            return options.UserContext.ForwardTenantId && provider.GetService<ICurrentTenant>() is { } currentTenant
                ? new TenantContextDelegatingHandler(currentTenant)
                : new PassthroughDelegatingHandler();
        });

        return builder;
    }
}
