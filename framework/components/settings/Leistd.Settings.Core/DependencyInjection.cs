using Leistd.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Leistd.Settings.Abstractions;
using Leistd.Settings.Options;
using Leistd.Settings.Services;

namespace Leistd.Settings;

/// <summary>
/// 设置定义与解析的核心依赖注入扩展。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册设置定义管理器、解析器、写入入口与设置页用例。
    /// </summary>
    /// <remarks>
    /// <para><see cref="ISettingDefinitionManager"/> 为 Singleton——定义在进程生命周期内不变；
    /// <see cref="ISettingProvider"/> 为 Scoped，一次请求内只查一次库并复用结果。</para>
    /// <para>还需要一个 <see cref="ISettingStore"/> 实现（如 <c>AddSettingsEfCore&lt;TDbContext&gt;()</c>）：
    /// 缺失时解析 <see cref="ISettingProvider"/> 直接失败，而不是静默只返回默认值。</para>
    /// <para>机密设置（<see cref="ISettingDefinition.IsEncrypted"/>）用宿主的 Data Protection 加解密：
    /// 宿主须 <c>AddDataProtection()</c> 并配置持久化、可共享的密钥环；没注册时只有读写机密设置会失败，
    /// 不定义机密设置的宿主不受影响。</para>
    /// <para>可重复调用：服务只注册一次，<paramref name="configure"/> 每次都叠加。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddSettingsCore(options => options.LocalizationResource = typeof(AppResource));
    /// builder.Services.AddSingleton&lt;ISettingDefinitionProvider, DisplaySettingDefinitionProvider&gt;();
    /// </code>
    /// </example>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">设置页用例的展示约定。</param>
    public static IServiceCollection AddSettingsCore(
        this IServiceCollection services,
        Action<SettingManagementOptions>? configure = null)
    {
        services.TryAddSingleton<ISettingDefinitionManager, SettingDefinitionManager>();
        services.TryAddScoped<ISettingProvider, DefaultSettingProvider>();
        services.TryAddTransient<ISettingManager, DefaultSettingManager>();
        services.TryAddTransient<ISettingManagementService, SettingManagementService>();
        services.AddOptions<SettingManagementOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddJsonLocalizationResources(typeof(SettingErrorCodes).Assembly);
        return services;
    }
}
