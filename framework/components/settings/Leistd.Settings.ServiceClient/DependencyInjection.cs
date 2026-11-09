using Leistd.ServiceClient;
using Leistd.Settings.ServiceClient.Options;
using Leistd.Settings.ServiceClient.Reading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ServiceClientRegistration = Leistd.ServiceClient.DependencyInjection;

namespace Leistd.Settings.ServiceClient;

/// <summary>读取其他服务共享设置的注册入口。</summary>
public static class DependencyInjection
{
    /// <summary>登记一个共享设置提供方，并声明要读取的设置及各自的兜底值。</summary>
    /// <remarks>
    /// <para>注册 <see cref="ISharedSettingReader"/>、进程内 <c>HybridCache</c>（宿主自己的 <c>AddHybridCache</c> 配置照常生效）
    /// 与名为 <paramref name="provider"/> 的服务客户端。选项先绑定 <paramref name="configSectionPath"/>（默认
    /// <c>Leistd:ServiceClients:{provider}</c>），再应用 <paramref name="configure"/>，启动时校验。</para>
    /// <para>机器认证与弹性管道由宿主在返回的构建器上组合：先 <c>AddStandardResilienceHandler()</c>、再 <c>AddClientCredentials()</c>，
    /// 总时限才包住取令牌。<paramref name="provider"/> 是命名客户端名，不要与同一宿主的其他服务客户端重名。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var identity = builder.Services.AddSharedSettingsClient&lt;IdentitySharedSettingsOptions&gt;(
    ///     "IdentitySharedSettings",
    ///     shared =&gt; shared
    ///         .Add("Security.StepUp.Enabled", fallback: "true")
    ///         .Add("Security.StepUp.WindowMinutes", fallback: "15"),
    ///     configSectionPath: "Leistd:ServiceClients:Identity");
    /// identity.AddStandardResilienceHandler();
    /// identity.AddClientCredentials(configSectionPath: "Leistd:ServiceClients:Identity");
    /// </code>
    /// </example>
    /// <typeparam name="TOptions">该提供方的客户端选项，每个提供方一个具体类型。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="provider">提供方名：读取时传给 <see cref="ISharedSettingReader"/>，也是命名客户端名。</param>
    /// <param name="declare">声明要读取的设置与兜底值，至少一项。</param>
    /// <param name="configure">在配置节之后应用的选项配置。</param>
    /// <param name="configSectionPath">选项绑定的配置节。</param>
    /// <returns>该提供方的客户端构建器，用于追加弹性管道与机器认证。</returns>
    /// <exception cref="InvalidOperationException">同一提供方重复登记，或没有声明任何设置。</exception>
    public static IHttpClientBuilder AddSharedSettingsClient<TOptions>(
        this IServiceCollection services,
        string provider,
        Action<SharedSettingDeclarations> declare,
        Action<TOptions>? configure = null,
        string? configSectionPath = null)
        where TOptions : SharedSettingsClientOptions, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(declare);

        if (services.Select(descriptor => descriptor.ImplementationInstance).OfType<SharedSettingProviderRegistration>()
            .Any(registration => registration.Name == provider))
        {
            throw new InvalidOperationException(
                $"Shared settings provider '{provider}' is already registered; declare all of its settings in one AddSharedSettingsClient call.");
        }

        var declarations = new SharedSettingDeclarations();
        declare(declarations);
        if (declarations.Fallbacks.Count == 0)
        {
            throw new InvalidOperationException($"Shared settings provider '{provider}' declares no settings.");
        }

        var section = configSectionPath ?? $"{ServiceClientRegistration.ConfigurationSectionPrefix}:{provider}";
        var builder = services.AddServiceClient<SharedSettingHttpClient<TOptions>, SharedSettingHttpClient<TOptions>, TOptions>(
            provider, configure, section);
        services.AddOptions<TOptions>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TOptions>>(
            new SharedSettingsClientOptionsValidator<TOptions>(section)));

        services.AddHybridCache();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<SharedSettingCache>();
        services.TryAddScoped<ISharedSettingReader, SharedSettingReader>();
        services.AddSingleton(new SharedSettingProviderRegistration(
            provider,
            declarations.Fallbacks,
            serviceProvider => serviceProvider.GetRequiredService<IOptions<TOptions>>().Value,
            (serviceProvider, tenant, etag, cancellationToken) =>
                serviceProvider.GetRequiredService<SharedSettingHttpClient<TOptions>>().GetAsync(tenant, etag, cancellationToken)));
        return builder;
    }
}
