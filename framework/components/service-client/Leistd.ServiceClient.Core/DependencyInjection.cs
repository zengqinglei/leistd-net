using Leistd.ExceptionHandling.Options;
using Leistd.ServiceClient.ExceptionMappings;
using Leistd.ServiceClient.Handlers;
using Leistd.ServiceClient.Options;
using Leistd.Tracing.Options;
using Leistd.Tracing.HttpClient.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
    /// 注册强类型服务客户端并装配标准调用管道。
    /// </summary>
    /// <remarks>
    /// <para>先绑定 <paramref name="configSectionPath"/>（默认 <c>Leistd:ServiceClients:{serviceName}</c>），再应用
    /// <paramref name="configure"/>；无主机的 <c>ServiceCollection</c> 须自行注册 <c>IConfiguration</c>。</para>
    /// <para>按 <paramref name="serviceName"/> 区分客户端，不同服务名可多次登记。同一服务名以相同的客户端接口、实现、
    /// 选项类型与配置节重复调用时不重复登记客户端与处理器，只追加 <paramref name="configure"/>，并返回该命名客户端的构建器；
    /// 同一服务名换用其他客户端接口、实现、选项类型或配置节，或同一选项类型用于另一服务名时抛出 <see cref="InvalidOperationException"/>。</para>
    /// <para>TraceId 透传是<b>跟随宿主显式组合</b>的可选能力：
    /// 宿主注册了 <c>AddCorrelationIdCore</c>（Leistd.Tracing）才透传 TraceId；
    /// 未注册时对应环节自动直通，本方法不代为注册。</para>
    /// </remarks>
    /// <typeparam name="TClient">客户端接口</typeparam>
    /// <typeparam name="TImplementation">客户端实现</typeparam>
    /// <typeparam name="TOptions">客户端配置类型（每个客户端一个具体类型）</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="serviceName">下游服务名：命名 HttpClient、默认配置节与日志类别</param>
    /// <param name="configure">在配置节之后应用的选项配置</param>
    /// <param name="configSectionPath">选项绑定的配置节；省略时为 <c>Leistd:ServiceClients:{serviceName}</c></param>
    /// <returns><see cref="IHttpClientBuilder"/>，可继续追加认证（OAuth 包）、弹性等处理器</returns>
    /// <example>
    /// <code>
    /// builder.Services
    ///     .AddServiceClient&lt;IIdentityApi, IdentityApiClient, IdentityClientOptions&gt;("identity")
    ///     .AddClientCredentials();
    /// </code>
    /// </example>
    public static IHttpClientBuilder AddServiceClient<TClient, TImplementation, TOptions>(
        this IServiceCollection services,
        string serviceName,
        Action<TOptions>? configure = null,
        string? configSectionPath = null)
        where TClient : class
        where TImplementation : class, TClient
        where TOptions : ServiceClientOptions, new()
        => AddServiceClientRegistration(
            services,
            serviceName,
            typeof(TClient),
            typeof(TImplementation),
            configure,
            configSectionPath,
            () => services.AddHttpClient<TClient, TImplementation>(serviceName));

    // 按服务名登记一次客户端：绑定选项、创建命名客户端并装配标准管道，供各客户端技术的注册入口共用。
    // clientType 与 implementationType（Refit 等由框架生成实现时为 null）共同标识客户端，用于判断重复调用是否为相同登记；
    // createClient 只在首次登记时调用。
    internal static IHttpClientBuilder AddServiceClientRegistration<TOptions>(
        IServiceCollection services,
        string serviceName,
        Type clientType,
        Type? implementationType,
        Action<TOptions>? configure,
        string? configSectionPath,
        Func<IHttpClientBuilder> createClient)
        where TOptions : ServiceClientOptions, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        if (configSectionPath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(configSectionPath);
        }

        var registration = new ServiceClientRegistration(
            serviceName,
            clientType,
            implementationType,
            typeof(TOptions),
            configSectionPath ?? $"{ConfigurationSectionPrefix}:{serviceName}");
        var registered = services.Select(d => d.ImplementationInstance).OfType<ServiceClientRegistration>().ToList();
        var repeated = registered.Contains(registration);

        if (!repeated)
        {
            // 同名的第二种登记会让两套管道叠在同一个命名客户端上；同一选项类型服务两个客户端会让两个配置节互相覆盖。
            if (registered.FirstOrDefault(r => r.ServiceName == serviceName || r.OptionsType == registration.OptionsType)
                is { } conflict)
            {
                throw new InvalidOperationException(
                    $"Service client '{serviceName}' ({registration.Describe()}) conflicts with the registered service client " +
                    $"'{conflict.ServiceName}' ({conflict.Describe()}). " +
                    "Each service name is registered once, and each service client has its own options type.");
            }

            services.AddSingleton(registration);
            services.AddOptions<TOptions>().BindConfiguration(registration.ConfigSectionPath);
        }

        if (configure is not null)
        {
            services.AddOptions<TOptions>().Configure(configure);
        }

        return repeated
            ? services.AddHttpClient(serviceName)
            : createClient().AddServiceClientPipeline<TOptions>(serviceName);
    }

    private sealed record ServiceClientRegistration(
        string ServiceName,
        Type ClientType,
        Type? ImplementationType,
        Type OptionsType,
        string ConfigSectionPath)
    {
        public string Describe() =>
            $"client {ClientType.Name}, implementation {ImplementationType?.Name ?? "<generated>"}, " +
            $"options {OptionsType.Name}, section '{ConfigSectionPath}'";
    }

    /// <summary>
    /// 在现有客户端构建器上装配服务客户端标准管道。
    /// </summary>
    /// <remarks>依次应用传输异常与链路标识处理器。</remarks>
    /// <typeparam name="TOptions">客户端配置类型（须已绑定，如经 <c>services.Configure</c>）</typeparam>
    /// <param name="builder">HttpClient 构建器</param>
    /// <param name="serviceName">下游服务名（日志类别后缀）</param>
    public static IHttpClientBuilder AddServiceClientPipeline<TOptions>(
        this IHttpClientBuilder builder,
        string serviceName)
        where TOptions : ServiceClientOptions, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        // 上游故障的状态语义属于本组件的默认值，在这里登记而不是交给宿主逐个 Configure：
        // 漏一个不会有编译或启动错误，只会让 502/503/504 静默变成 500。
        // 幂等：MapDefaultException 按类型 TryAdd，多个客户端各调一次也只登记一次；
        // 宿主的 MapException<ServiceClientException> 覆盖它，与调用顺序无关。
        builder.Services.Configure<GlobalExceptionOptions>(ServiceClientExceptionMappings.Configure);

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

            // 超时不在这里设：可归类的超时来自宿主叠加的弹性管道（管道内生效），HttpClient.Timeout 保持
            // .NET 默认值作外层兜底。两者设成同一时长会竞争，外层先到时抛出的是无法归类的取消异常。
        });

        // 传输异常统一放在最外层，才能覆盖取令牌与下游请求的完整调用。
        builder.AddHttpMessageHandler(() => new TransportFailureHandler());

        // 可选组件未注册时使用直通处理器，保持宿主显式组合。
        builder.AddHttpMessageHandler(provider =>
            provider.GetService<ICorrelationIdProvider>() is { } correlationIdProvider
                ? new CorrelationIdDelegatingHandler(
                    correlationIdProvider,
                    provider.GetRequiredService<IOptionsMonitor<CorrelationIdOptions>>())
                : new PassthroughDelegatingHandler());

        return builder;
    }
}
