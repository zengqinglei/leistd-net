using AutoMapper;
using Leistd.ObjectMapping.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Leistd.ObjectMapping.AutoMapper;

public static class DependencyInjection
{
    /// <summary>
    /// 添加 AutoMapper 对象映射器。
    /// </summary>
    /// <remarks>
    /// 已弃用：AutoMapper 13.0.1 存在高危 DoS 漏洞（GHSA-rvv3-g6hj-g44x / CVE-2026-32933），
    /// 官方仅在改为商业授权的 15.1.1+ 修复。请改用 <c>Leistd.ObjectMapping.Mapster</c> 的
    /// <c>AddMapsterObjectMapper</c>（同一 <c>IObjectMapper</c> 抽象，零授权成本、无该漏洞）。
    /// </remarks>
    [Obsolete("AutoMapper 组件已弃用（AutoMapper 13.0.1 存在 GHSA-rvv3-g6hj-g44x 高危 DoS，14.x 不会修复、15.x+ 改商业授权）。请改用 Leistd.ObjectMapping.Mapster 的 AddMapsterObjectMapper。")]
    public static IServiceCollection AddAutoMapperObjectMapper(
        this IServiceCollection services,
        Action<AutoMapperOptions>? configure = null)
    {
        if (configure != null)
        {
            services.Configure(configure);
        }

        services.AddSingleton<IMapper>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AutoMapperOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<AutoMapperObjectMapper>>();

            var config = new MapperConfiguration(cfg =>
            {
                cfg.ConstructServicesUsing(sp.GetService);

                foreach (var configurator in options.Configurators)
                {
                    configurator(cfg);
                }
            });

            if (options.ValidateMappings)
            {
                logger.LogInformation("验证 AutoMapper 映射配置");
                config.AssertConfigurationIsValid();
            }

            return config.CreateMapper();
        });

        services.AddSingleton<IObjectMapper, AutoMapperObjectMapper>();

        return services;
    }
}
