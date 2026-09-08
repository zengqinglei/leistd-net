using System.Reflection;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Leistd.ObjectMapping.Mapster.Options;
using Leistd.ObjectMapping.Mapster.Mapping;
using Leistd.ObjectMapping.Mapster.Services;
using Leistd.ObjectMapping.Abstractions;

namespace Leistd.ObjectMapping.Mapster;

/// <summary>
/// Mapster 对象映射的注册入口：注册 <c>IObjectMapper</c> 与按程序集扫描到的 <c>MapsterProfile</c>。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 Mapster 对象映射器。
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddMapsterObjectMapper(options =&gt;
    /// {
    ///     options.ValidateMappings = true;   // 开发/测试环境尽早暴露未配置的映射
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddMapsterObjectMapper(
        this IServiceCollection services,
        Action<MapsterOptions>? configure = null)
    {
        if (configure != null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IMapper>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MapsterOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<MapsterObjectMapper>>();

            var config = new TypeAdapterConfig();
            config.Default.PreserveReference(true);

            foreach (var configurator in options.Configurators)
            {
                configurator(config);
            }

            if (options.ValidateMappings)
            {
                logger.LogInformation("Validating Mapster mapping configuration");
                config.Compile();
            }

            return new Mapper(config);
        });

        services.TryAddSingleton<IObjectMapper, MapsterObjectMapper>();

        return services;
    }

    /// <summary>
    /// 扫描程序集并添加 Mapster 配置文件。
    /// </summary>
    public static MapsterOptions AddProfiles(this MapsterOptions options, params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var profileTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(MapsterProfile)));

            foreach (var profileType in profileTypes)
            {
                options.Configurators.Add(config =>
                {
                    var profile = (MapsterProfile)Activator.CreateInstance(profileType)!;
                    profile.Configure(config);
                });
            }
        }

        return options;
    }
}
