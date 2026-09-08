using System.Globalization;
using Leistd.Localization.Json;
using Leistd.Localization.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Leistd.Localization.AspNetCore.HostedServices;

namespace Leistd.Localization.AspNetCore;

/// <summary>
/// Leistd JSON 本地化的注册与中间件装配入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册基于嵌入 JSON 的本地化：<see cref="CompositeStringLocalizerFactory"/> 按<b>资源标记类型</b>路由
    /// （仅 <see cref="JsonLocalizationOptions.JsonResourceTypes"/> 中的类型走 JSON，其余委派官方 RESX），
    /// 及 <see cref="IStringLocalizer"/> / <see cref="IStringLocalizer{T}"/> 解析，并配置支持语言与默认语言。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="supportedCultures">支持的语言（首个作为默认/回落语言）。默认 <c>["en", "zh-CN"]</c>。</param>
    /// <param name="configure">进一步配置本地化选项（如追加资源程序集、登记 JSON 资源标记类型）。</param>
    /// <remarks>
    /// 仅注册服务，不挂载中间件：请求 culture 解析由宿主显式调用 <see cref="UseJsonRequestLocalization"/>。
    /// 本组件所在程序集被默认登记为资源程序集之一，用于分发通用键（<c>Error:*</c>）。
    /// 不全局接管宿主本地化——未登记为 JSON 资源类型的 <see cref="IStringLocalizer{T}"/> 一律走官方 RESX 工厂。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddJsonLocalization(options =&gt;
    /// {
    ///     options.ResourceAssemblies.Add(typeof(Program).Assembly);
    ///     options.JsonResourceTypes.Add(typeof(OrderResource));   // 未登记的类型走官方 RESX
    ///     options.DefaultCulture = "zh-CN";
    /// });
    ///
    /// app.UseJsonRequestLocalization();
    /// </code>
    /// </example>
    public static IServiceCollection AddJsonLocalization(
        this IServiceCollection services,
        string[]? supportedCultures = null,
        Action<JsonLocalizationOptions>? configure = null)
    {
        var cultures = supportedCultures is { Length: > 0 }
            ? supportedCultures
            : ["en", "zh-CN"];
        var defaultCulture = cultures[0];

        // JSON localizer 栈 + 官方 RESX 工厂，二者由组合工厂按资源来源路由（不全局接管宿主本地化）。
        services.AddSingleton<JsonLocalizationResourceReader>();
        services.AddSingleton<JsonStringLocalizerFactory>();
        // 官方 ResourceManagerStringLocalizerFactory 依赖 IOptions<LocalizationOptions> / ILoggerFactory：由 AddLocalization 提供。
        services.AddLocalization();
        services.AddSingleton<ResourceManagerStringLocalizerFactory>();
        // 用组合工厂替换默认全局 IStringLocalizerFactory：typed IStringLocalizer<T> 按**类型**路由
        //（TResourceSource 登记在 JsonResourceTypes 才走 JSON，其余委派官方 RESX），从而不接管宿主既有本地化。
        services.AddSingleton<IStringLocalizerFactory, CompositeStringLocalizerFactory>();
        services.AddTransient(typeof(IStringLocalizer<>), typeof(StringLocalizer<>));
        // 无参 IStringLocalizer 直接使用全局 JSON 词条；按 object 路由会误入 RESX 分支。
        services.AddTransient<IStringLocalizer>(sp =>
            sp.GetRequiredService<JsonStringLocalizerFactory>().Create(typeof(object)));

        services.Configure<JsonLocalizationOptions>(options =>
        {
            options.DefaultCulture = defaultCulture;
            // 框架自身资源程序集（承载通用键默认中英文案）
            var frameworkAssembly = typeof(JsonLocalizationOptions).Assembly;
            if (!options.ResourceAssemblies.Contains(frameworkAssembly))
                options.ResourceAssemblies.Add(frameworkAssembly);
            configure?.Invoke(options);
        });

        var cultureInfos = cultures.Select(c => new CultureInfo(c)).ToList();
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new(defaultCulture);
            options.SupportedCultures = cultureInfos;
            options.SupportedUICultures = cultureInfos;
            options.ApplyCurrentCultureToResponseHeaders = true;
        });

        // 启动预热：把资源解析/坏文件告警提前到启动阶段，而非生产首个请求（P4）
        services.AddHostedService<LocalizationPreloadHostedService>();

        return services;
    }

    /// <summary>
    /// 启用请求区域性解析中间件。
    /// </summary>
    /// <remarks>必须在任何读取当前区域性的中间件之前调用。</remarks>
    public static IApplicationBuilder UseJsonRequestLocalization(this IApplicationBuilder app)
        => app.UseRequestLocalization();
}
