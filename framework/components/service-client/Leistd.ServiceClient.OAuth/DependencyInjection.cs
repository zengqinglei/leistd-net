using Leistd.ServiceClient.OAuth.Handlers;
using Leistd.ServiceClient.OAuth.Options;
using Leistd.ServiceClient.OAuth.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Leistd.ServiceClient.OAuth;

/// <summary>
/// 服务间 OAuth2 client credentials 认证注册入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 为服务客户端追加 client credentials 认证（应在 <c>AddServiceClient</c> 之后调用，
    /// 使认证处理器位于管道最内层）。认证配置绑定
    /// 配置节 <c>Leistd:ServiceClients:&lt;服务名&gt;:Auth</c>。
    /// </summary>
    /// <param name="builder">来自 <c>AddServiceClient</c> 的 <see cref="IHttpClientBuilder"/></param>
    /// <param name="configuration">应用配置</param>
    public static IHttpClientBuilder AddClientCredentials(
        this IHttpClientBuilder builder,
        IConfiguration configuration)
    {
        builder.Services.Configure<ClientCredentialsOptions>(
            builder.Name,
            configuration.GetSection(
                $"{ServiceClient.DependencyInjection.ConfigurationSectionPrefix}:{builder.Name}:Auth"));
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
        // token 请求专用 HttpClient（无认证/用户头处理器，避免管道递归）
        builder.Services.AddHttpClient(ClientCredentialsTokenProvider.TokenHttpClientName);
        builder.Services.TryAddSingleton<IServiceTokenProvider, ClientCredentialsTokenProvider>();

        var clientName = builder.Name;
        builder.AddHttpMessageHandler(provider => new ClientCredentialsDelegatingHandler(
            clientName,
            provider.GetRequiredService<IServiceTokenProvider>()));

        return builder;
    }
}
