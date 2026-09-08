using Leistd.ObjectMapping.Abstractions;
using Leistd.ObjectMapping.Mapster;
using Leistd.ObjectMapping.Mapster.Mapping;
using Leistd.ObjectMapping.Mapster.Options;
using Leistd.ObjectMapping.Mapster.Services;
using Leistd.TestBase.Assertions;
using global::Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.ObjectMapping.Tests.Mapster;

/// <summary>
/// <c>AddProfiles</c> 的程序集扫描与注册面。
/// </summary>
/// <remarks>
/// 这是全框架**唯一**一处 <c>assembly.GetTypes()</c>。程序集扫描一旦进入每次容器构建的
/// 公共路径，测试与启动的成本就会随业务类型数增长，因此这里既要钉住"能扫到该扫的"，
/// 也要钉住"只在显式调用 AddProfiles 时扫"——扫描绝不能悄悄进入 AddMapsterObjectMapper。
/// </remarks>
public class MapsterProfileDiscoveryTests
{
    private sealed record Order(string Code, decimal Total);
    private sealed class OrderDto
    {
        public string Code { get; set; } = "";
        public decimal Total { get; set; }
        public string Display { get; set; } = "";
    }

    /// <summary>被扫描发现的具体 Profile。</summary>
    public sealed class OrderProfile : MapsterProfile
    {
        protected override void ConfigureMappings() =>
            CreateMap<Order, OrderDto>().Map(d => d.Display, s => $"{s.Code}:{s.Total}");
    }

    /// <summary>抽象派生：必须被跳过，否则 Activator.CreateInstance 会在启动期抛。</summary>
    public abstract class AbstractProfile : MapsterProfile;

    /// <summary>不继承 MapsterProfile：不得被拾取。</summary>
    public sealed class NotAProfile
    {
        public static bool Constructed { get; private set; }
        public NotAProfile() => Constructed = true;
    }

    [Fact]
    public void Profiles_in_the_scanned_assembly_are_applied()
    {
        using var provider = Build(o => o.AddProfiles(typeof(OrderProfile).Assembly));

        var dto = provider.GetRequiredService<IObjectMapper>().Map<Order, OrderDto>(new Order("A-1", 9.5m));

        Assert.Equal("A-1:9.5", dto.Display);
    }

    // 抽象派生与非 Profile 类型都必须被过滤掉：前者会让 Activator 抛，
    // 后者会把无关类型实例化——两者都在启动期发作，且错误信息离现场很远。
    [Fact]
    public void Abstract_profiles_and_unrelated_types_are_skipped()
    {
        using var provider = Build(o => o.AddProfiles(typeof(AbstractProfile).Assembly));

        // 能解析出映射器即说明没有在构造配置时抛
        Assert.NotNull(provider.GetRequiredService<IObjectMapper>());
        Assert.False(NotAProfile.Constructed);
    }

    // 扫描只发生在显式 AddProfiles 时。若它渗进 AddMapsterObjectMapper，
    // 每个测试类建容器都会遍历全部程序集类型——正是要规避的那条曲线。
    [Fact]
    public void Registration_alone_scans_nothing()
    {
        using var provider = new ServiceCollection().AddLogging()
            .AddMapsterObjectMapper()
            .BuildServiceProvider();

        // 没调用过 AddProfiles，配置器列表就必须是空的——扫描不能藏在注册里
        Assert.Empty(provider.GetRequiredService<IOptions<MapsterOptions>>().Value.Configurators);
    }

    [Fact]
    public void AddProfiles_returns_the_same_options_for_chaining()
    {
        var options = new MapsterOptions();

        Assert.Same(options, options.AddProfiles(typeof(OrderProfile).Assembly));
    }

    [Fact]
    public void AddProfiles_without_assemblies_adds_nothing()
    {
        Assert.Empty(new MapsterOptions().AddProfiles().Configurators);
    }

    // ValidateMappings 把未配置的映射提前到启动期暴露；关闭时保持惰性编译。
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validate_mappings_flag_does_not_change_mapping_results(bool validate)
    {
        using var provider = Build(o =>
        {
            o.ValidateMappings = validate;
            o.AddProfiles(typeof(OrderProfile).Assembly);
        });

        Assert.Equal("A-2:1", provider.GetRequiredService<IObjectMapper>()
            .Map<Order, OrderDto>(new Order("A-2", 1m)).Display);
    }

    [Fact]
    public void Registration_exposes_the_mapper_as_singleton_and_keeps_the_mapster_mapper_available()
    {
        var services = new ServiceCollection().AddLogging().AddMapsterObjectMapper();

        services.AssertSingle<IObjectMapper>(ServiceLifetime.Singleton);
        services.AssertSingle<IMapper>(ServiceLifetime.Singleton);
        services.AssertResolvesTo<IObjectMapper, MapsterObjectMapper>();
    }

    // 宿主重复调用 AddMapsterObjectMapper 是常态（组合根拆分）。
    // 不幂等会让 IObjectMapper 被建成两份单例，配置各自独立。
    [Fact]
    public void Registration_is_idempotent()
    {
        ServiceCollectionAssertions.AssertIdempotent(services =>
        {
            services.AddLogging();
            services.AddMapsterObjectMapper();
        });
    }

    private static ServiceProvider Build(Action<MapsterOptions> configure) =>
        new ServiceCollection().AddLogging().AddMapsterObjectMapper(configure).BuildServiceProvider();
}
