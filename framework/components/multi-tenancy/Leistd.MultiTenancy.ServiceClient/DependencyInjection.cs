using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.ServiceClient.Options;
using Leistd.MultiTenancy.ServiceClient.Stores;
using Leistd.ServiceClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Leistd.MultiTenancy.ServiceClient;

/// <summary>
/// 远端租户连接存储的注册入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 <see cref="ITenantConnectionConfigurationStore"/> 的远端实现：回源控制面经 <c>MapTenantConnections</c> 暴露的机器端点。
    /// </summary>
    /// <remarks>
    /// <para>与控制库的 EF 实现二选一：连接配置只能有一个权威来源，已注册其它实现时抛出。
    /// 解析、缓存与单飞另由 <c>AddRemoteTenantConnectionResolution()</c> 注册。</para>
    /// <para>返回的构建器上由宿主决定鉴权方式（如 client credentials）与弹性策略；控制面按名字下发已解密的连接串，
    /// 本服务不持有控制面的密钥环。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddRemoteTenantConnectionResolution();
    /// builder.Services.AddRemoteTenantConnectionStore("Identity", builder.Configuration)
    ///     .AddClientCredentials(builder.Configuration)
    ///     .AddStandardResilienceHandler();
    /// </code>
    /// </example>
    /// <param name="services">服务集合。</param>
    /// <param name="serviceName">控制面服务名：命名 HttpClient 与配置节 <c>Leistd:ServiceClients:{serviceName}</c>。</param>
    /// <param name="configuration">应用配置。</param>
    /// <returns>HttpClient 构建器。</returns>
    public static IHttpClientBuilder AddRemoteTenantConnectionStore(
        this IServiceCollection services,
        string serviceName,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ITenantConnectionConfigurationStore)))
        {
            throw new InvalidOperationException(
                "Tenant connection configuration must have exactly one authoritative source: either the control " +
                "database (AddMultiTenancyEfCore) or a remote control plane (AddRemoteTenantConnectionStore), never both.");
        }

        var builder = services.AddServiceClient<ITenantConnectionConfigurationStore, RemoteTenantConnectionStore, RemoteTenantConnectionClientOptions>(
            serviceName,
            configuration);

        // 回源全用相对地址：缺 BaseAddress 时组合照常成功，要到首个租户请求才以不带键名的 URI 错误暴露。
        // 只约束本存储自己的客户端，ServiceClient 通用选项仍允许留空（只用绝对地址的客户端存在）。
        var baseAddressKey = $"{Leistd.ServiceClient.DependencyInjection.ConfigurationSectionPrefix}:{serviceName}:BaseAddress";
        services.AddOptions<RemoteTenantConnectionClientOptions>()
            .Validate(
                options => Uri.TryCreate(options.BaseAddress, UriKind.Absolute, out _),
                $"The remote tenant connection store requires {baseAddressKey} (an absolute URI of the control plane).")
            .ValidateOnStart();

        // 回源是宿主侧的控制面查询，租户 Id 已在路径里显式给出：不转发调用方的用户与租户上下文，
        // 否则被调方一旦信任本客户端的委托，查询就会跑进那个租户的上下文里
        services.PostConfigure<RemoteTenantConnectionClientOptions>(options =>
        {
            options.UserContext.Enabled = false;
            options.UserContext.ForwardTenantId = false;
        });

        // 同一个客户端也服务逐库作业的库目录：解析出的实现就是上面那一个，不另建 HttpClient
        services.TryAddTransient<ITenantDatabaseDirectory>(provider =>
            (ITenantDatabaseDirectory)provider.GetRequiredService<ITenantConnectionConfigurationStore>());

        return builder;
    }
}
