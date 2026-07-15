using System.Globalization;
using Leistd.Localization.Core.Json;
using Leistd.Localization.Core.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Leistd.Localization.AspNetCore;

/// <summary>
/// Leistd JSON 本地化的注册与中间件装配入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册基于嵌入 JSON 的本地化：<see cref="JsonStringLocalizerFactory"/> 及
    /// <see cref="IStringLocalizer"/> / <see cref="IStringLocalizer{T}"/> 解析，并配置支持语言与默认语言。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="supportedCultures">支持的语言（首个作为默认/回落语言）。默认 <c>["en", "zh-CN"]</c>。</param>
    /// <param name="configure">进一步配置本地化选项（如追加资源程序集）。</param>
    /// <remarks>
    /// 本方法仅注册服务，不挂载中间件；请求 culture 解析由宿主显式调用
    /// <see cref="UseJsonRequestLocalization"/> 完成（不替宿主隐式挂载中间件）。
    /// 框架默认将本组件所在程序集登记为资源程序集之一，用于分发通用键（<c>Error:*</c>）。
    /// </remarks>
    public static IServiceCollection AddJsonLocalization(
        this IServiceCollection services,
        string[]? supportedCultures = null,
        Action<JsonLocalizationOptions>? configure = null)
    {
        var cultures = supportedCultures is { Length: > 0 }
            ? supportedCultures
            : ["en", "zh-CN"];
        var defaultCulture = cultures[0];

        // 自写 JSON localizer 栈（替换 .NET 默认的 ResourceManagerStringLocalizerFactory / RESX）
        services.AddSingleton<JsonLocalizationResourceReader>();
        services.AddSingleton<IStringLocalizerFactory, JsonStringLocalizerFactory>();
        services.AddTransient(typeof(IStringLocalizer<>), typeof(StringLocalizer<>));
        services.AddTransient(sp => sp.GetRequiredService<IStringLocalizerFactory>().Create(typeof(object)));

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
    /// 启用请求 culture 解析中间件（QueryString / Cookie / Accept-Language 三 provider）。
    /// 必须在任何读取当前 culture 的中间件之前调用。
    /// </summary>
    public static IApplicationBuilder UseJsonRequestLocalization(this IApplicationBuilder app)
        => app.UseRequestLocalization();
}
