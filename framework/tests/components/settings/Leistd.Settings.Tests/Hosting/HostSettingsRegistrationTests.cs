using Leistd.BackgroundJobs.Recurring;
using Leistd.EventBus.EventHandlers;
using Leistd.Settings.Definitions;
using Leistd.Settings.Events;
using Leistd.Settings.Hosting;
using Leistd.Settings.Hosting.Configuration;
using Leistd.Settings.Hosting.Options;
using Leistd.Settings.Hosting.Runtime;
using Leistd.TestBase.Assertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.Settings.Tests.Hosting;

/// <summary>
/// <c>AddHostSettings</c> 的注册面：服务与刷新任务只登记一次、绑定累加、刷新周期按实际配置节校验。
/// </summary>
public sealed class HostSettingsRegistrationTests
{
    private static IServiceCollection Base(IDictionary<string, string?>? settings = null) =>
        new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build());

    private static void Bind(HostSettingBindingBuilder bindings) => bindings.Bind("Demo.Level", ["Demo:Level"]);

    [Fact]
    public void Registration_exposes_the_configuration_source_applier_and_refresh_job()
    {
        var services = Base().AddHostSettings(Bind);

        services.AssertSingle<HostSettingsConfigurationProvider>(ServiceLifetime.Singleton);
        services.AssertSingle<HostSettingApplier>(ServiceLifetime.Scoped);
        Assert.Single(services, d => d.ServiceType == typeof(IEventHandler<SettingChangedEvent>));
        Assert.Single(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(HostSettingStartupService));
        Assert.Single(services.Select(d => d.ImplementationInstance).OfType<RecurringJobDefinition>());
    }

    // AssertIdempotent 不看验证器与选项配置；这里另外核对：验证器只有一个、刷新任务与托管服务不叠加
    [Fact]
    public void Repeated_registration_adds_no_services_validators_or_jobs()
    {
        ServiceCollectionAssertions.AssertIdempotent(services => services.AddHostSettings(Bind));

        var services = Base().AddHostSettings(Bind).AddHostSettings(Bind);

        Assert.Single(services, d => d.ServiceType == typeof(IValidateOptions<HostSettingOptions>));
        Assert.Single(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(HostSettingStartupService));
        Assert.Single(services.Select(d => d.ImplementationInstance).OfType<RecurringJobDefinition>());
    }

    // 组合根拆分时各模块各声明自己的绑定，后一次调用不能把前一次的绑定吞掉
    [Fact]
    public void Repeated_registration_accumulates_the_bindings()
    {
        using var provider = Base()
            .AddHostSettings(bindings => bindings.Bind("Demo.Level", ["Demo:Level"]))
            .AddHostSettings(bindings => bindings.Bind("Demo.Days", ["Demo:Days"]))
            .BuildServiceProvider();

        var bound = provider.GetRequiredService<IOptions<HostSettingBindingCollection>>().Value.Bindings;
        Assert.Equal(["Demo.Level", "Demo.Days"], bound.Select(b => b.SettingName));
    }

    [Theory]
    [InlineData(null, HostSettingOptions.SectionName)]
    [InlineData("Ops:HostSettings", "Ops:HostSettings")]
    public void Refresh_interval_validation_names_the_actual_section(string? configSectionPath, string expectedSection)
    {
        var services = Base(new Dictionary<string, string?> { [$"{expectedSection}:RefreshInterval"] = "00:00:00.5" });
        if (configSectionPath is null)
        {
            services.AddHostSettings(Bind);
        }
        else
        {
            services.AddHostSettings(Bind, configSectionPath);
        }

        using var provider = services.BuildServiceProvider();

        var failure = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<HostSettingOptions>>().Value);
        Assert.StartsWith($"{expectedSection}:RefreshInterval", Assert.Single(failure.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public void The_refresh_interval_binds_from_a_custom_section()
    {
        using var provider = Base(new Dictionary<string, string?> { ["Ops:HostSettings:RefreshInterval"] = "00:02:00" })
            .AddHostSettings(Bind, "Ops:HostSettings")
            .BuildServiceProvider();

        Assert.Equal(TimeSpan.FromMinutes(2), provider.GetRequiredService<IOptions<HostSettingOptions>>().Value.RefreshInterval);
    }

    // 刷新周期只有一份：第二次换路径时若静默接受，校验消息会报一个没人配置的键
    [Fact]
    public void Repeated_registration_with_another_section_is_rejected()
    {
        var services = Base().AddHostSettings(Bind);

        Assert.Throws<InvalidOperationException>(() => services.AddHostSettings(Bind, "Ops:HostSettings"));
    }

    // 宿主自己提供的定义不受影响：宿主级设置只追加一个定义提供程序，与宿主的并存
    [Fact]
    public void Host_definition_providers_coexist_with_the_defaults_provider()
    {
        var services = Base();
        services.AddSingleton<ISettingDefinitionProvider, HostDefinitions>();

        services.AddHostSettings(Bind);

        Assert.Equal(2, services.Count(d => d.ServiceType == typeof(ISettingDefinitionProvider)));
        Assert.Contains(services, d => d.ImplementationType == typeof(HostDefinitions));
    }

    private sealed class HostDefinitions : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context)
        {
        }
    }
}
