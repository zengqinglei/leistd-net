using Leistd.Localization.AspNetCore;
using Leistd.Localization.Core.Json;
using Leistd.Localization.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.Localization.Tests;

/// <summary>
/// 组合工厂路由验证：已登记 JSON 资源程序集的类型走 JSON，其余委派官方 RESX，避免全局接管宿主本地化。
/// </summary>
public class CompositeStringLocalizerFactoryTests
{
    // 属于“已登记 JSON 资源程序集”（本测试程序集）的类型代表。
    private sealed class RegisteredResource;

    private static CompositeStringLocalizerFactory CreateFactory()
    {
        var options = Options.Create(new JsonLocalizationOptions());
        // 仅登记本测试程序集为 JSON 资源来源。
        options.Value.ResourceAssemblies.Add(typeof(CompositeStringLocalizerFactoryTests).Assembly);

        var json = new JsonStringLocalizerFactory(new JsonLocalizationResourceReader(options), options);
        var fallback = new ResourceManagerStringLocalizerFactory(
            Options.Create(new LocalizationOptions()),
            NullLoggerFactory.Instance);

        return new CompositeStringLocalizerFactory(json, fallback, options);
    }

    [Fact]
    public void Registered_assembly_type_routes_to_json_localizer()
    {
        var factory = CreateFactory();

        var localizer = factory.Create(typeof(RegisteredResource));

        Assert.IsType<JsonStringLocalizer>(localizer);
    }

    [Fact]
    public void Unregistered_assembly_type_falls_back_to_resource_manager()
    {
        var factory = CreateFactory();

        // string 属于 System.Private.CoreLib —— 未登记为 JSON 资源程序集，应委派官方工厂。
        var localizer = factory.Create(typeof(string));

        Assert.IsNotType<JsonStringLocalizer>(localizer);
        Assert.IsType<ResourceManagerStringLocalizer>(localizer);
    }

    [Fact]
    public void Create_by_location_routes_registered_assembly_name_to_json()
    {
        var factory = CreateFactory();
        var assemblyName = typeof(CompositeStringLocalizerFactoryTests).Assembly.GetName().Name!;

        var localizer = factory.Create("AnyBaseName", assemblyName);

        Assert.IsType<JsonStringLocalizer>(localizer);
    }

    // ---- 完整 DI 注册（AddJsonLocalization）解析验证 ----
    // 回归守卫：无参 IStringLocalizer 是框架全局 JSON 词条视图（业务/框架键），必须走 JSON。
    // 若误经组合工厂按 typeof(object) 路由，会因 object 属 CoreLib 落入 RESX 分支 → 业务错误消息无法本地化。

    [Fact]
    public void Parameterless_localizer_resolves_to_json_not_resource_manager()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddJsonLocalization()
            .BuildServiceProvider();

        var localizer = provider.GetRequiredService<IStringLocalizer>();

        Assert.IsType<JsonStringLocalizer>(localizer);
    }

    [Fact]
    public void Typed_localizer_for_unregistered_host_type_falls_back_to_resource_manager()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddJsonLocalization()
            .BuildServiceProvider();

        // string 属 CoreLib，未登记为 JSON 资源程序集 → typed IStringLocalizer<string> 应走官方 RESX，不被吞进 JSON。
        var localizer = provider.GetRequiredService<IStringLocalizer<string>>();

        Assert.IsNotType<JsonStringLocalizer>(localizer);
    }
}
