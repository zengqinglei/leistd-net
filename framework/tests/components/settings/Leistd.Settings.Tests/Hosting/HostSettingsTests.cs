using Leistd.BackgroundJobs.Recurring;
using Leistd.EventBus.EventHandlers;
using Leistd.Security.Users;
using Leistd.Settings.Definitions;
using Leistd.Settings.Stores;
using Leistd.Settings.Events;
using Leistd.Settings.Hosting;
using Leistd.Settings.Hosting.Runtime;
using Leistd.Settings.Tests.Core;
using Leistd.TestBase.Doubles;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Xunit;
using Leistd.Settings.Tests.TestDoubles;
using Leistd.Lock.Memory;
using Leistd.UnitOfWork;

namespace Leistd.Settings.Tests.Hosting;

/// <summary>宿主级设置经配置源覆盖部署配置：消费方读 <c>IOptionsMonitor</c>，默认值是部署基线，整组不合规时沿用上一组。</summary>
public sealed class HostSettingsTests : IDisposable
{
    private readonly FakeSettingStore _store = new();
    private readonly EphemeralDataProtectionProvider _dataProtection = new();
    private readonly IHost _host;

    public HostSettingsTests()
    {
        _host = Build(attach: true);
    }

    public void Dispose() => _host.Dispose();

    /// <summary>默认值取部署配置并按定义归一：清除设置要回到部署基线，而不是回到一个写死的值。</summary>
    [Fact]
    public void Bound_settings_default_to_the_normalized_deployment_baseline()
    {
        var definitions = _host.Services.GetRequiredService<ISettingDefinitionManager>();

        Assert.Equal("Warning", definitions.GetOrNull("Demo.Level")!.DefaultValue);
        Assert.Equal("true", definitions.GetOrNull("Demo.Enabled")!.DefaultValue);
        Assert.Equal("90", definitions.GetOrNull("Demo.Days")!.DefaultValue);
        Assert.Equal("fallback", definitions.GetOrNull("Demo.Missing")!.DefaultValue);
        // 口令不该作为默认值下发到界面
        Assert.Null(definitions.GetOrNull("Demo.Password")!.DefaultValue);
    }

    [Fact]
    public async Task A_stored_value_overrides_the_bound_configuration_key()
    {
        _store.Host["Demo.Level"] = "Debug";

        await ApplyAsync();

        Assert.Equal("Debug", Options.Level);
        // 基线不受覆盖值影响：否则界面上的默认值会随当前值变化
        Assert.Equal("Warning", _host.Services.GetRequiredService<ISettingDefinitionManager>().GetOrNull("Demo.Level")!.DefaultValue);
    }

    [Fact]
    public async Task Clearing_the_setting_returns_to_the_deployment_configuration()
    {
        _store.Host["Demo.Level"] = "Debug";
        await ApplyAsync();
        _store.Host.Remove("Demo.Level");

        await ApplyAsync();

        Assert.Equal("warning", Options.Level);
    }

    /// <summary>新值让选项校验不过时整组不生效：消费方始终取到合规的值，而不是每次取值都抛异常。</summary>
    [Fact]
    public async Task A_set_that_fails_options_validation_keeps_the_previous_values()
    {
        _store.Host["Demo.Level"] = "Debug";
        await ApplyAsync();

        _store.Host["Demo.Level"] = "Information";
        _store.Host["Demo.Days"] = "10";
        await ApplyAsync();

        Assert.Equal(("Debug", 90), (Options.Level, Options.Days));
    }

    // 校验失败整组回落时记固定事件号的 Error，可以接告警
    [Fact]
    public async Task A_rejected_set_is_logged_with_its_event_id()
    {
        _store.Host["Demo.Days"] = "10";

        await ApplyAsync();

        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, Assert.Single(Logged(SettingLogEvents.HostSettingsRejected)).Level);
    }

    // 一项解不开只影响该项：它沿用当前生效的值，其余照常应用；解密失败在解密点记一次事件
    [Fact]
    public async Task A_secret_that_cannot_be_decrypted_keeps_its_current_value_and_the_others_apply()
    {
        _store.Host["Demo.Password"] = Protect(_dataProtection, "Demo.Password", "p1");
        await ApplyAsync();
        Assert.Equal("p1", Options.Password);

        _store.Host["Demo.Password"] = Protect(new EphemeralDataProtectionProvider(), "Demo.Password", "p2");
        _store.Host["Demo.Level"] = "Debug";
        await ApplyAsync();

        Assert.Equal(("p1", "Debug"), (Options.Password, Options.Level));
        var logged = Assert.Single(Logged(SettingLogEvents.DecryptionFailed));
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, logged.Level);
        Assert.Contains("Demo.Password", logged.Message);
    }

    // 从未生效过的机密解不开时回落到部署配置，不阻止其他项
    [Fact]
    public async Task A_secret_never_applied_falls_back_to_the_deployment_configuration()
    {
        _store.Host["Demo.Password"] = Protect(new EphemeralDataProtectionProvider(), "Demo.Password", "p2");
        _store.Host["Demo.Level"] = "Debug";

        await ApplyAsync();

        Assert.Equal(("from-config", "Debug"), (Options.Password, Options.Level));
    }

    // 部署值不含系统设置的覆盖：设置测试要用这一次读到的存储值，缺项才取部署配置，不能取进程里已叠加覆盖的配置
    [Fact]
    public async Task The_deployment_baseline_ignores_host_setting_overrides()
    {
        _store.Host["Demo.Password"] = Protect(_dataProtection, "Demo.Password", "p1");
        _store.Host["Demo.Level"] = "Debug";
        await ApplyAsync();
        var baseline = _host.Services.GetRequiredService<IHostSettingBaseline>();

        Assert.Equal(("p1", "Debug"), (Options.Password, Options.Level));
        Assert.Equal("from-config", baseline.GetDeploymentValue("Demo.Password"));
        Assert.Equal("warning", baseline.GetDeploymentValue("Demo.Level"));
        Assert.Null(baseline.GetDeploymentValue("Demo.Unbound"));
    }

    // 高优先级配置源显式置空：正式配置读到的是空值，部署值也是空值，不能回落到低优先级源里的旧凭据
    [Fact]
    public async Task An_explicit_empty_value_in_a_higher_source_is_the_deployment_value()
    {
        await ApplyAsync();
        var configuration = _host.Services.GetRequiredService<IConfiguration>();
        ((IConfigurationBuilder)configuration).AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:Password"] = "" });

        Assert.Equal("", configuration["Demo:Password"]);
        Assert.Equal("", _host.Services.GetRequiredService<IHostSettingBaseline>().GetDeploymentValue("Demo.Password"));
    }

    // 部署配置运行期间重载：部署值是现读的，不是启动时的快照
    [Fact]
    public async Task The_deployment_value_follows_a_configuration_reload()
    {
        await ApplyAsync();
        var root = (IConfigurationRoot)_host.Services.GetRequiredService<IConfiguration>();
        root.Providers.OfType<Microsoft.Extensions.Configuration.Memory.MemoryConfigurationProvider>()
            .Single(provider => provider.TryGet("Demo:Level", out _)).Set("Demo:Level", "error");

        Assert.Equal("error", _host.Services.GetRequiredService<IHostSettingBaseline>().GetDeploymentValue("Demo.Level"));
    }

    [Fact]
    public async Task A_committed_host_write_is_applied_to_this_process()
    {
        _store.Host["Demo.Level"] = "Debug";

        await HandleAsync(new SettingChangedEvent("Demo.Level", SettingScopes.Host, null));

        Assert.Equal("Debug", Options.Level);
    }

    [Fact]
    public async Task Writes_to_unbound_or_non_host_settings_are_ignored()
    {
        _store.Host["Demo.Level"] = "Debug";

        await HandleAsync(new SettingChangedEvent("Demo.Unbound", SettingScopes.Host, null));
        await HandleAsync(new SettingChangedEvent("Demo.Level", SettingScopes.Tenant, null));

        Assert.Equal("warning", Options.Level);
    }

    // 租户上下文读不到宿主行，"读不到"不能当成"都没设"推进配置
    [Fact]
    public async Task Nothing_is_applied_when_the_host_scope_is_unreachable()
    {
        _store.Host["Demo.Level"] = "Debug";
        await ApplyAsync();
        _store.CanAccessHostScope = false;
        _store.Host.Clear();

        await ApplyAsync();

        Assert.Equal("Debug", Options.Level);
    }

    // 最早的请求与日志要用设置里的值：首次应用必须在宿主开始接收请求之前完成，而不是在后台慢慢跑
    [Fact]
    public async Task Stored_values_are_applied_before_the_host_has_started()
    {
        _store.Host["Demo.Level"] = "Debug";

        await _host.StartAsync();
        try
        {
            Assert.Equal("Debug", Options.Level);
        }
        finally
        {
            await _host.StopAsync();
        }
    }

    [Fact]
    public void Refresh_runs_on_every_instance()
    {
        var job = Assert.Single(_host.Services.GetServices<RecurringJobDefinition>());

        Assert.Equal((HostSettingRefreshJob.Name, RecurringJobScope.EveryInstance), (job.Name, job.Scope));
    }

    [Fact]
    public async Task Starting_without_attaching_the_configuration_source_fails()
    {
        using var host = Build(attach: false);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

        Assert.Contains("UseHostSettings()", error.Message);
    }

    [Fact]
    public void Binding_a_setting_that_is_not_process_wide_fails()
    {
        using var host = Build(attach: true, extraBinding: "Demo.Layered");

        var error = Assert.Throws<InvalidOperationException>(
            () => host.Services.GetRequiredService<ISettingDefinitionManager>().GetAll());

        Assert.Contains("not host-scoped", error.Message);
    }

    // 机密设置按读取器的用途链加密；换一个密钥环加密的密文本进程解不开
    private static string Protect(IDataProtectionProvider provider, string name, string value)
        => provider.CreateProtector("Leistd.Settings.EncryptedValue.v1").CreateProtector(name).Protect(value);

    private IReadOnlyList<FakeLogRecord> Logged(int eventId)
        => [.. _host.Services.GetFakeLogCollector().GetSnapshot().Where(record => record.Id.Id == eventId)];

    private DemoOptions Options => _host.Services.GetRequiredService<IOptionsMonitor<DemoOptions>>().CurrentValue;

    private async Task ApplyAsync()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<HostSettingApplier>().ApplyAsync();
    }

    private async Task HandleAsync(SettingChangedEvent @event)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IEventHandler<SettingChangedEvent>>().HandleAsync(@event);
    }

    private IHost Build(bool attach, string? extraBinding = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Demo:Level"] = "warning",
            ["Demo:Enabled"] = "True",
            ["Demo:Days"] = "90",
            ["Demo:Password"] = "from-config",
        });

        builder.Services
            .AddSettingsCore()
            .AddSingleton<ISettingStore>(_store)
            .AddMemoryLocalLock()
            .AddSingleton<IUnitOfWorkManager>(new RecordingUnitOfWorkManager())
            .AddSingleton<ICurrentUser>(new FakeCurrentUser())
            .AddSingleton<ISettingDefinitionProvider, Definitions>()
            .AddSingleton<IDataProtectionProvider>(_dataProtection)
            .AddFakeLogging()
            .AddHostSettings(bindings =>
            {
                bindings
                    .BindOption<DemoOptions>("Demo.Level", "Demo", nameof(DemoOptions.Level))
                    .BindOption<DemoOptions>("Demo.Enabled", "Demo", nameof(DemoOptions.Enabled))
                    .BindOption<DemoOptions>("Demo.Days", "Demo", nameof(DemoOptions.Days))
                    .BindOption<DemoOptions>("Demo.Password", "Demo", nameof(DemoOptions.Password))
                    .Bind("Demo.Missing", ["Demo:Missing"], fallback: "fallback");
                if (extraBinding is not null)
                {
                    bindings.Bind(extraBinding, ["Demo:Layered"]);
                }
            });
        builder.Services.AddOptions<DemoOptions>()
            .BindConfiguration("Demo")
            .Validate(options => options.Days >= 30, "Days must be at least 30.");

        var host = builder.Build();
        return attach ? host.UseHostSettings() : host;
    }

    private sealed class DemoOptions
    {
        public string Level { get; set; } = "Information";

        public bool Enabled { get; set; }

        public int Days { get; set; } = 365;

        public string? Password { get; set; }
    }

    private sealed class Definitions : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context)
        {
            context.Add("Demo.Level", scopes: SettingScopes.Host).WithAllowedValues("Debug", "Information", "Warning");
            context.Add("Demo.Enabled", scopes: SettingScopes.Host).AsBoolean();
            context.Add("Demo.Days", scopes: SettingScopes.Host).AsInteger(30, 3650);
            context.Add("Demo.Password", scopes: SettingScopes.Host).IsEncrypted = true;
            context.Add("Demo.Missing", scopes: SettingScopes.Host);
            context.Add("Demo.Unbound", scopes: SettingScopes.Host);
            context.Add("Demo.Layered", scopes: SettingScopes.Tenant);
        }
    }
}
