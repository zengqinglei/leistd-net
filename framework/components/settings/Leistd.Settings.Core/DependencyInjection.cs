using Leistd.ExceptionHandling.Options;
using Leistd.Settings.ExceptionMappings;
using Leistd.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Leistd.Settings.Checks;
using Leistd.Settings.Definitions;
using Leistd.Settings.Errors;
using Leistd.Settings.Management;
using Leistd.Settings.Resolution;
using Leistd.Settings.Stores;
using Leistd.Settings.Options;
using Leistd.Settings.Registration;
using Microsoft.Extensions.Hosting;

namespace Leistd.Settings;

/// <summary>设置定义与解析的核心注册入口。</summary>
public static class DependencyInjection
{
    /// <summary>注册设置定义管理器、解析器、写入入口与设置页用例。</summary>
    /// <remarks>
    /// <para><see cref="ISettingDefinitionManager"/> 为 Singleton——定义在进程生命周期内不变；
    /// <see cref="ISettingProvider"/> 为 Scoped，按当前租户与当前用户缓存，同一作用域里每个租户、每个用户只查一次库。</para>
    /// <para>还需要一个 <see cref="ISettingStore"/> 实现（如 <c>AddSettingsEfCore&lt;TDbContext&gt;()</c>），
    /// 缺失时解析 <see cref="ISettingProvider"/> 失败。</para>
    /// <para>机密设置（<see cref="ISettingDefinition.IsEncrypted"/>）的加解密二选一：设了 <see cref="SettingEncryptionOptions.DataProtectionProvider"/>
    /// 就用这个专用实例（应用名不同的多个宿主共用机密设置时用它），否则用宿主 <c>AddDataProtection()</c> 注册的实例；
    /// 都要配置持久化、可共享的密钥环。两者都没有时只有读写机密设置会失败，不定义机密设置的宿主不受影响。</para>
    /// <para>默认写入入口 <see cref="DefaultSettingManager"/> 按租户取 <c>IDistributedLock</c>、每次写入在独立的事务型工作单元里提交，
    /// 宿主因此须注册锁（单副本 <c>AddMemoryLocalLock()</c>，多副本 <c>AddRedisDistributedLock(...)</c>，否则副本之间不互斥）
    /// 与工作单元（<c>AddUnitOfWork()</c>）。只读设置的宿主同样如此：本方法总会登记默认写入入口。
    /// 经 Host 启动时缺任何一项即启动失败；宿主替换了 <see cref="ISettingManager"/> 时不检查这两项。</para>
    /// <para>可重复调用：服务与启动检查只注册一次，<paramref name="configure"/> 每次都叠加。</para>
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
        services.TryAddTransient<ISettingCheckService, SettingCheckService>();
        // 检查要看宿主最终的登记（替换发生在本方法之后），因此持有登记集合本身而不是此刻的快照
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SettingManagerDependencyCheck>(
            sp => new SettingManagerDependencyCheck(sp.GetRequiredService<IServiceScopeFactory>(), services)));
        services.AddOptions<SettingManagementOptions>();
        services.AddOptions<SettingEncryptionOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddJsonLocalizationResources(typeof(SettingErrorCodes).Assembly);
        // 错误码的状态语义与默认译文属于本组件默认值，在此登记，避免宿主漏配时静默回落成 400。
        // 宿主的 MapCode / MapException 覆盖同一码或同一类型，与调用顺序无关。
        services.Configure<GlobalExceptionOptions>(SettingsExceptionMappings.Configure);
        return services;
    }

    /// <summary>给一项设置登记测试：设置页在该行提供"测试"，结果保存为本租户（宿主上下文为宿主）的最近结论。</summary>
    /// <remarks>
    /// <para>同一设置只认最后一次登记。探针按作用域解析。<paramref name="dependsOn"/> 列出结论依赖的其他设置，
    /// 它们与主设置在同一租户里任一项被写入或清除，结果即标记为过期；只在用户层被写入不影响。</para>
    /// <para>结果需要 <see cref="ISettingCheckStore"/>：<c>AddSettingsEfCore</c> 已提供。</para>
    /// </remarks>
    /// <typeparam name="TCheck">探针类型。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="settingName">主设置：测试按钮所在的那一项，须允许租户层或宿主层写入。</param>
    /// <param name="dependsOn">结论依赖的其他设置。</param>
    /// <returns>同一服务集合。</returns>
    public static IServiceCollection AddSettingCheck<TCheck>(
        this IServiceCollection services,
        string settingName,
        params string[] dependsOn)
        where TCheck : class, ISettingCheck
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingName);
        ArgumentNullException.ThrowIfNull(dependsOn);
        services.TryAddScoped<TCheck>();
        services.AddSingleton(new SettingCheckRegistration(settingName, [.. dependsOn], typeof(TCheck)));
        return services;
    }
}
