using Leistd.ServiceClient.Abstractions;
using Leistd.ServiceClient.OAuth.Handlers;
using Leistd.ServiceClient.OAuth.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenIddict.Client;

namespace Leistd.ServiceClient.OAuth;

/// <summary>官方 OpenIddict.Client 服务认证注册入口。</summary>
public static class DependencyInjection
{
    internal const string RegistrationId = "Leistd.ServiceClient";

    /// <summary>绑定工作负载身份，启用官方发现与客户端认证；令牌不进入官方数据库存储。</summary>
    public static IServiceCollection AddServiceAuthentication(this IServiceCollection services,
        Action<ServiceAuthenticationOptions>? configure = null,
        string configSectionPath = ServiceAuthenticationOptions.SectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configSectionPath);
        var options = services.AddOptions<ServiceAuthenticationOptions>().BindConfiguration(configSectionPath);
        if (configure is not null) options.Configure(configure);
        services.AddSingleton<IValidateOptions<ServiceAuthenticationOptions>>(new ServiceAuthenticationOptionsValidator(configSectionPath));
        options.ValidateOnStart();
        services.AddHybridCache();
        services.AddOpenIddict().AddClient(client =>
        {
            client.AllowClientCredentialsFlow().AllowTokenExchangeFlow().DisableTokenStorage();
            client.UseSystemNetHttp();
            // 官方处理器会把远端响应原文（含令牌）写进日志，前置接手解析与状态校验。
            foreach (var descriptor in ResponsePayloadLoggingGuard.Descriptors) client.AddEventHandler(descriptor);
        });
        services.AddOptions<OpenIddictClientOptions>().Configure<IOptions<ServiceAuthenticationOptions>>((client, identity) =>
        {
            var value = identity.Value;
            if (client.Registrations.Any(registration => registration.RegistrationId == RegistrationId)) return;
            client.Registrations.Add(new OpenIddictClientRegistration
            {
                RegistrationId = RegistrationId, Issuer = new Uri(value.Authority, UriKind.Absolute),
                ClientId = value.ClientId, ClientSecret = value.ClientSecret
            });
        });
        services.TryAddSingleton<TokenCache>();
        return services;
    }

    /// <summary>为命名客户端追加机器认证；默认绑定 Leistd:ServiceClients:{Name}。</summary>
    public static IHttpClientBuilder AddClientCredentials(this IHttpClientBuilder builder,
        Action<ClientCredentialsOptions>? configure = null, string? configSectionPath = null)
    {
        RegisterAuthentication(builder);
        var path = configSectionPath ?? $"{ServiceClient.DependencyInjection.ConfigurationSectionPrefix}:{builder.Name}";
        var options = builder.Services.AddOptions<ClientCredentialsOptions>(builder.Name).BindConfiguration(path);
        if (configure is not null) options.Configure(configure);
        options.ValidateOnStart();
        return builder.AddHttpMessageHandler(provider => new ClientCredentialsDelegatingHandler(builder.Name,
            provider.GetRequiredService<OpenIddictClientService>(), provider.GetRequiredService<TokenCache>(),
            provider.GetRequiredService<IOptions<ServiceAuthenticationOptions>>(),
            provider.GetRequiredService<IOptionsMonitor<ClientCredentialsOptions>>()));
    }

    /// <summary>追加单跳 Token Exchange；没有已验证的用户令牌时拒绝调用，不回退为机器身份。</summary>
    public static IHttpClientBuilder AddTokenExchange(this IHttpClientBuilder builder,
        Action<TokenExchangeOptions>? configure = null, string? configSectionPath = null)
    {
        RegisterAuthentication(builder);
        var path = configSectionPath ?? $"{ServiceClient.DependencyInjection.ConfigurationSectionPrefix}:{builder.Name}:TokenExchange";
        var options = builder.Services.AddOptions<TokenExchangeOptions>(builder.Name).BindConfiguration(path);
        if (configure is not null) options.Configure(configure);
        builder.Services.AddSingleton<IValidateOptions<TokenExchangeOptions>>(new TokenExchangeOptionsValidator(builder.Name, path));
        options.ValidateOnStart();
        return builder.AddHttpMessageHandler(provider => new TokenExchangeDelegatingHandler(builder.Name,
            provider.GetRequiredService<OpenIddictClientService>(), provider.GetRequiredService<TokenCache>(),
            provider.GetRequiredService<IOptions<ServiceAuthenticationOptions>>(),
            provider.GetRequiredService<IOptionsMonitor<TokenExchangeOptions>>(),
            provider.GetRequiredService<IUserAccessTokenAccessor>()));
    }

    private sealed record AuthenticationMode(string Name);
    private static void RegisterAuthentication(IHttpClientBuilder builder)
    {
        if (builder.Services.Any(descriptor => descriptor.ImplementationInstance is AuthenticationMode existing && existing.Name == builder.Name))
            throw new InvalidOperationException($"Service client '{builder.Name}' already has an authentication handler.");
        builder.Services.AddSingleton(new AuthenticationMode(builder.Name));
    }

}
