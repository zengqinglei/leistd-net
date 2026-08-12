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
    /// 本服务调用身份的配置节：<c>Leistd:ServiceAuth</c>（Authority、ClientId、ClientSecret，
    /// 可选默认 Scope）。一个业务服务作为调用方只有一个身份，全局配置一次，
    /// 所有服务客户端共享；目标服务级差异只有 <c>Leistd:ServiceClients:&lt;服务名&gt;:Scope</c>。
    /// </summary>
    public const string ServiceAuthSectionName = "Leistd:ServiceAuth";

    /// <summary>
    /// 为服务客户端追加 client credentials 认证（应在 <c>AddServiceClient</c> 之后调用，
    /// 使认证处理器位于管道最内层）。认证配置来源：
    /// 全局 <c>Leistd:ServiceAuth</c>（本服务的调用身份）+
    /// 客户端节 <c>Leistd:ServiceClients:&lt;服务名&gt;:Scope</c>（目标服务的 scope，可省）。
    /// </summary>
    /// <param name="builder">来自 <c>AddServiceClient</c> 的 <see cref="IHttpClientBuilder"/></param>
    /// <param name="configuration">应用配置</param>
    public static IHttpClientBuilder AddClientCredentials(
        this IHttpClientBuilder builder,
        IConfiguration configuration)
    {
        var clientName = builder.Name;

        // 1. 全局调用身份
        builder.Services.Configure<ClientCredentialsOptions>(
            clientName, configuration.GetSection(ServiceAuthSectionName));

        // 2. 目标服务级 Scope（存在才覆盖，未配置时继承全局默认 Scope）
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
