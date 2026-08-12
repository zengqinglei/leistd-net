using Leistd.ServiceClient.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace Leistd.ServiceClient.Refit;

/// <summary>
/// Refit 形态的服务客户端注册入口：业务 Client 包只声明接口 + Refit 特性，
/// HTTP 实现由 Refit 源生成，标准管道（日志、TraceId 透传、用户上下文头）与
/// 错误契约（<c>RemoteServiceException</c>）与手写路径完全一致。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 Refit 接口客户端并装配标准调用管道。
    /// Options 绑定配置节 <c>Leistd:ServiceClients:&lt;serviceName&gt;</c>；
    /// 返回 <see cref="IHttpClientBuilder"/>，可继续追加认证（OAuth 包）、弹性等处理器。
    /// </summary>
    /// <remarks>
    /// 必须经本方法注册而非裸 <c>AddRefitClient</c>——否则错误语义退回 Refit 默认的
    /// <c>ApiException</c>，与手写路径的 <c>RemoteServiceException</c> 契约分叉。
    /// </remarks>
    /// <typeparam name="TApi">Refit 接口（须对源生成器可见：public，或 internal + InternalsVisibleTo）</typeparam>
    /// <typeparam name="TOptions">客户端配置类型（每个客户端一个具体类型）</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="serviceName">下游服务名：命名 HttpClient、配置节与日志类别</param>
    /// <param name="configuration">应用配置</param>
    /// <param name="settings">自定义 <see cref="RefitSettings"/>；默认 <see cref="LeistdRefitSettings.Create"/></param>
    public static IHttpClientBuilder AddRefitServiceClient<TApi, TOptions>(
        this IServiceCollection services,
        string serviceName,
        IConfiguration configuration,
        RefitSettings? settings = null)
        where TApi : class
        where TOptions : ServiceClientOptions, new()
    {
        services.Configure<TOptions>(configuration.GetSection(
            $"{ServiceClient.DependencyInjection.ConfigurationSectionPrefix}:{serviceName}"));
        return AddRefitServiceClientCore<TApi, TOptions>(services, serviceName, settings);
    }

    /// <summary>
    /// 注册 Refit 接口客户端（委托配置版）。行为同
    /// <see cref="AddRefitServiceClient{TApi,TOptions}(IServiceCollection,string,IConfiguration,RefitSettings?)"/>。
    /// </summary>
    /// <typeparam name="TApi">Refit 接口</typeparam>
    /// <typeparam name="TOptions">客户端配置类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="serviceName">下游服务名</param>
    /// <param name="configureOptions">配置委托</param>
    /// <param name="settings">自定义 <see cref="RefitSettings"/>；默认 <see cref="LeistdRefitSettings.Create"/></param>
    public static IHttpClientBuilder AddRefitServiceClient<TApi, TOptions>(
        this IServiceCollection services,
        string serviceName,
        Action<TOptions> configureOptions,
        RefitSettings? settings = null)
        where TApi : class
        where TOptions : ServiceClientOptions, new()
    {
        services.Configure(configureOptions);
        return AddRefitServiceClientCore<TApi, TOptions>(services, serviceName, settings);
    }

    private static IHttpClientBuilder AddRefitServiceClientCore<TApi, TOptions>(
        IServiceCollection services,
        string serviceName,
        RefitSettings? settings)
        where TApi : class
        where TOptions : ServiceClientOptions, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        return services
            .AddRefitClient<TApi>(settings ?? LeistdRefitSettings.Create(), httpClientName: serviceName)
            .AddServiceClientPipeline<TOptions>(serviceName);
    }
}
