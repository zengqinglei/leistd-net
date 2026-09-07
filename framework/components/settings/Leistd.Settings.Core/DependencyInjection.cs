using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Leistd.Settings.Abstractions;
using Leistd.Settings.Services;

namespace Leistd.Settings;

/// <summary>
/// 设置定义与解析的核心依赖注入扩展。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册设置定义管理器、解析器与写入入口。
    /// </summary>
    /// <remarks>
    /// <para><see cref="ISettingDefinitionManager"/> 为 Singleton——定义在进程生命周期内不变；
    /// <see cref="ISettingProvider"/> 为 Scoped，一次请求内只查一次库并复用结果。</para>
    /// <para>还需要一个 <see cref="ISettingStore"/> 实现（如 <c>AddSettingsEfCore&lt;TDbContext&gt;()</c>）：
    /// 缺失时解析 <see cref="ISettingProvider"/> 直接失败，而不是静默只返回默认值。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddSettingsCore();
    /// builder.Services.AddSingleton&lt;ISettingDefinitionProvider, DisplaySettingDefinitionProvider&gt;();
    /// </code>
    /// </example>
    /// <param name="services">服务集合。</param>
    public static IServiceCollection AddSettingsCore(this IServiceCollection services)
    {
        services.TryAddSingleton<ISettingDefinitionManager, SettingDefinitionManager>();
        services.TryAddScoped<ISettingProvider, DefaultSettingProvider>();
        services.TryAddTransient<ISettingManager, DefaultSettingManager>();
        return services;
    }
}
