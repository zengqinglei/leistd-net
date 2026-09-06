using Leistd.ServiceClient.OAuth.Handlers;
using Leistd.ServiceClient.OAuth.Options;
using Leistd.ServiceClient.OAuth.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Leistd.ServiceClient.OAuth.Abstractions;

namespace Leistd.ServiceClient.OAuth;

/// <summary>
/// 服务间 OAuth2 client credentials 认证注册入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 获取服务调用身份配置节名称 <c>Leistd:ServiceAuth</c>。
    /// </summary>
    public const string ServiceAuthSectionName = "Leistd:ServiceAuth";

    /// <summary>
    /// 为服务客户端追加 client credentials 认证。
    /// </summary>
    /// <remarks>
    /// 应在客户端注册后调用。调用身份来自 <c>Leistd:ServiceAuth</c>，目标范围可由客户端配置节覆盖。
    /// </remarks>
    /// <param name="builder">来自 <c>AddServiceClient</c> 的 <see cref="IHttpClientBuilder"/></param>
    /// <param name="configuration">应用配置</param>
    /// <example>
    /// <code>
    /// builder.Services
    ///     .AddRefitServiceClient&lt;IIdentityApi, IdentityClientOptions&gt;("identity", builder.Configuration)
    ///     .AddClientCredentials(builder.Configuration);   // token 缓存与 401 自愈
    /// </code>
    /// </example>
    public static IHttpClientBuilder AddClientCredentials(
        this IHttpClientBuilder builder,
        IConfiguration configuration)
    {
        var clientName = builder.Name;

        builder.Services.Configure<ClientCredentialsOptions>(
            clientName, configuration.GetSection(ServiceAuthSectionName));

        builder.Services.Configure<ClientCredentialsOptions>(clientName, options =>
        {
            var scope = configuration[
                $"{ServiceClient.DependencyInjection.ConfigurationSectionPrefix}:{clientName}:Scope"];
            if (!string.IsNullOrWhiteSpace(scope))
            {
                options.Scope = scope;
            }
        });

        return AddClientCredentialsCore(builder);
    }

    /// <summary>
    /// 为服务客户端追加 client credentials 认证（委托配置版）。
    /// </summary>
    /// <param name="builder">来自 <c>AddServiceClient</c> 的 <see cref="IHttpClientBuilder"/></param>
    /// <param name="configureOptions">认证配置委托</param>
    public static IHttpClientBuilder AddClientCredentials(
        this IHttpClientBuilder builder,
        Action<ClientCredentialsOptions> configureOptions)
    {
        builder.Services.Configure(builder.Name, configureOptions);
        return AddClientCredentialsCore(builder);
    }

    private static IHttpClientBuilder AddClientCredentialsCore(IHttpClientBuilder builder)
    {
        // 令牌请求必须使用独立管道，避免认证处理器递归调用自身。
        builder.Services.AddHttpClient(ClientCredentialsTokenProvider.TokenHttpClientName);
        builder.Services.TryAddSingleton<IServiceTokenProvider, ClientCredentialsTokenProvider>();

        var clientName = builder.Name;
        builder.AddHttpMessageHandler(provider => new ClientCredentialsDelegatingHandler(
            clientName,
            provider.GetRequiredService<IServiceTokenProvider>()));

        return builder;
    }
}
