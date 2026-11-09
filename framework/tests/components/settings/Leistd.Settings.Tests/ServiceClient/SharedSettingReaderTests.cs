using System.Net;
using System.Net.Http.Json;
using Leistd.MultiTenancy.Context;
using Leistd.ServiceClient.Exceptions;
using Leistd.Settings.Dtos;
using Leistd.Settings.ServiceClient;
using Leistd.Settings.ServiceClient.Options;
using Leistd.Settings.ServiceClient.Reading;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Leistd.Settings.Tests.ServiceClient;

/// <summary>
/// 共享设置读取器：新鲜期内用缓存、同键并发回源合并、过期后带 ETag 重验证；提供方暂不可达时用保留期内的旧值并节流，
/// 明确失败清掉旧值；读不到时返回使用方声明的兜底值并标出来源。
/// </summary>
public sealed class SharedSettingReaderTests : IAsyncDisposable
{
    private const string Provider = "IdentitySharedSettings";
    private static readonly CurrentTenant Tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly FakeProvider _remote = new();
    private readonly BudgetClock _clock = new();
    private ServiceProvider? _services;

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }
    }

    [Fact]
    public async Task Fresh_values_are_served_from_cache()
    {
        _remote.Respond(Ok(new() { ["Security.StepUp.Enabled"] = "false", ["Security.StepUp.WindowMinutes"] = "30" }, "\"v1\""));
        var reader = Reader();

        Assert.Equal(new SharedSettingValue("false", SharedSettingSource.Current), await reader.GetAsync(Provider, "Security.StepUp.Enabled"));
        _clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(30, await reader.GetAsync<int>(Provider, "Security.StepUp.WindowMinutes"));
        Assert.False(await reader.GetAsync<bool>(Provider, "Security.StepUp.Enabled"));

        var request = Assert.Single(_remote.Requests);
        Assert.Equal("/api/v1/internal/shared-settings/host", request.Path);
        Assert.Null(request.IfNoneMatch);
    }

    [Fact]
    public async Task The_current_tenant_selects_the_cache_entry_and_the_path()
    {
        var tenant = Guid.NewGuid();
        _remote.Respond(Ok(new() { ["Security.StepUp.Enabled"] = "false" }));
        _remote.Respond(Ok(new() { ["Security.StepUp.Enabled"] = "true" }));
        var reader = Reader();

        Assert.Equal("false", (await reader.GetAsync(Provider, "Security.StepUp.Enabled")).Value);
        using (Tenant.Change(tenant))
        {
            Assert.Equal("true", (await reader.GetAsync(Provider, "Security.StepUp.Enabled")).Value);
        }

        Assert.Equal(["/api/v1/internal/shared-settings/host", $"/api/v1/internal/shared-settings/{tenant}"],
            _remote.Requests.Select(request => request.Path));
    }

    /// <summary>
    /// 宿主把租户上下文换成请求作用域的实现时，各作用域读到各自的租户；回源仍在进程级缓存里进行，
    /// 不借用调用方作用域里的实例。
    /// </summary>
    [Fact]
    public async Task Each_scope_reads_its_own_tenant_with_a_scoped_tenant_accessor()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        _remote.Respond(Ok(new() { ["Security.StepUp.Enabled"] = "false", ["Security.StepUp.WindowMinutes"] = "15" }));
        _remote.Respond(Ok(new() { ["Security.StepUp.Enabled"] = "true", ["Security.StepUp.WindowMinutes"] = "15" }));
        var services = Services(new Dictionary<string, string?>());
        services.AddScoped<ICurrentTenantAccessor, ScopedTenantAccessor>();
        services.AddTransient<ICurrentTenant, CurrentTenant>();
        _services = Build(services);

        async Task<string?> ReadAsync(Guid tenant)
        {
            await using var scope = _services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICurrentTenantAccessor>().Current = new BasicTenantInfo(tenant);
            return (await scope.ServiceProvider.GetRequiredService<ISharedSettingReader>().GetAsync(Provider, "Security.StepUp.Enabled")).Value;
        }

        Assert.Equal("false", await ReadAsync(first));
        Assert.Equal("true", await ReadAsync(second));
        Assert.Equal("false", await ReadAsync(first));
        Assert.Equal([$"/api/v1/internal/shared-settings/{first}", $"/api/v1/internal/shared-settings/{second}"],
            _remote.Requests.Select(request => request.Path));
    }

    [Fact]
    public async Task Concurrent_reads_share_one_remote_call()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _remote.Respond(async () =>
        {
            await gate.Task;
            return await Ok(new() { ["Security.StepUp.Enabled"] = "false" })();
        });
        var reader = Reader();

        var reads = Enumerable.Range(0, 5).Select(_ => reader.GetAsync(Provider, "Security.StepUp.Enabled")).ToArray();
        await WaitUntilAsync(() => _remote.Requests.Count == 1);
        gate.SetResult();

        Assert.All(await Task.WhenAll(reads), value => Assert.Equal("false", value.Value));
        Assert.Single(_remote.Requests);
    }

    /// <summary>新鲜期过后带上一次的 ETag 重验证；304 续用原值并重新计新鲜期。</summary>
    [Fact]
    public async Task Expired_values_are_revalidated_with_the_previous_etag()
    {
        _remote.Respond(Ok(new() { ["Security.StepUp.Enabled"] = "false" }, "\"v1\""));
        _remote.Respond(NotModified);
        var reader = Reader();
        await reader.GetAsync(Provider, "Security.StepUp.Enabled");

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(new SharedSettingValue("false", SharedSettingSource.Current), await reader.GetAsync(Provider, "Security.StepUp.Enabled"));
        _clock.Advance(TimeSpan.FromSeconds(59));
        await reader.GetAsync(Provider, "Security.StepUp.Enabled");

        Assert.Equal([null, "\"v1\""], _remote.Requests.Select(request => request.IfNoneMatch));
    }

    /// <summary>暂不可达时用旧值并节流 30 秒，每个节流周期一条 Warning；旧值超过保留上限后改用兜底值。</summary>
    [Fact]
    public async Task An_unavailable_provider_serves_stale_values_until_they_expire()
    {
        _remote.Respond(Ok(new() { ["Security.StepUp.Enabled"] = "true", ["Security.StepUp.WindowMinutes"] = "30" }));
        _remote.Fail(() => new HttpRequestException(HttpRequestError.ConnectionError, "connection refused"));
        _remote.Respond(Status(HttpStatusCode.ServiceUnavailable));
        _remote.Respond(Status(HttpStatusCode.TooManyRequests));
        var reader = Reader(options => options.MaxStaleness = TimeSpan.FromMinutes(2));
        await reader.GetAsync(Provider, "Security.StepUp.WindowMinutes");

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(new SharedSettingValue("30", SharedSettingSource.Stale), await reader.GetAsync(Provider, "Security.StepUp.WindowMinutes"));
        _clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(SharedSettingSource.Stale, (await reader.GetAsync(Provider, "Security.StepUp.WindowMinutes")).Source);
        Assert.Equal(2, _remote.Requests.Count);
        Assert.Single(Logs(LogLevel.Warning));

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(SharedSettingSource.Stale, (await reader.GetAsync(Provider, "Security.StepUp.WindowMinutes")).Source);
        Assert.Equal(2, Logs(LogLevel.Warning).Count);

        // 确认时刻 + 新鲜期 1 分钟 + 保留 2 分钟之后，旧值不再可用
        _clock.Advance(TimeSpan.FromMinutes(1.5));
        Assert.Equal(new SharedSettingValue("15", SharedSettingSource.Fallback), await reader.GetAsync(Provider, "Security.StepUp.WindowMinutes"));
        Assert.Equal(4, _remote.Requests.Count);
    }

    /// <summary>没有旧值时暂不可达：返回兜底值，不缓存失败，下一次照常回源。</summary>
    [Fact]
    public async Task Without_earlier_values_an_unavailable_provider_yields_the_fallbacks()
    {
        _remote.Respond(Status(HttpStatusCode.BadGateway));
        _remote.Respond(Ok(new() { ["Security.StepUp.Enabled"] = "false", ["Security.StepUp.WindowMinutes"] = "15" }));
        var reader = Reader();

        Assert.Equal(new SharedSettingValue("true", SharedSettingSource.Fallback), await reader.GetAsync(Provider, "Security.StepUp.Enabled"));
        Assert.Equal(new SharedSettingValue("false", SharedSettingSource.Current), await reader.GetAsync(Provider, "Security.StepUp.Enabled"));
        Assert.Single(Logs(LogLevel.Warning));
    }

    /// <summary>
    /// 机器身份被拒（取令牌被拒、401、403）或响应无效：清掉旧值，记 Error，返回兜底值，不抛异常；
    /// 之后再遇暂时故障也不恢复旧值。
    /// </summary>
    [Theory]
    [InlineData("token")]
    [InlineData("401")]
    [InlineData("403")]
    [InlineData("invalid")]
    public async Task A_rejection_drops_earlier_values_and_yields_the_fallbacks(string failure)
    {
        _remote.Respond(Ok(new() { ["Security.StepUp.Enabled"] = "false" }));
        switch (failure)
        {
            case "token":
                _remote.Fail(() => new ServiceClientException("invalid_client", failureKind: ServiceClientFailureKind.RemoteFailure));
                break;
            case "invalid":
                _remote.Respond(() => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null") }));
                break;
            default:
                _remote.Respond(Status((HttpStatusCode)int.Parse(failure)));
                break;
        }

        _remote.Respond(Status(HttpStatusCode.ServiceUnavailable));
        var reader = Reader();
        await reader.GetAsync(Provider, "Security.StepUp.Enabled");

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(new SharedSettingValue("true", SharedSettingSource.Fallback), await reader.GetAsync(Provider, "Security.StepUp.Enabled"));
        Assert.Single(Logs(LogLevel.Error));
        Assert.Equal(new SharedSettingValue("true", SharedSettingSource.Fallback), await reader.GetAsync(Provider, "Security.StepUp.Enabled"));
        Assert.Equal(3, _remote.Requests.Count);
    }

    [Fact]
    public async Task An_unknown_tenant_yields_the_fallbacks()
    {
        _remote.Respond(Status(HttpStatusCode.NotFound));
        var reader = Reader();

        Assert.Equal(new SharedSettingValue("true", SharedSettingSource.Fallback), await reader.GetAsync(Provider, "Security.StepUp.Enabled"));
        Assert.Empty(Logs(LogLevel.Error));
        Assert.Single(Logs(LogLevel.Warning));
    }

    /// <summary>对方没有共享某项已声明的设置：该项用兜底值，回源时记一条 Warning 指出是哪几项；提供方的空值照样交出。</summary>
    [Fact]
    public async Task A_declared_setting_the_provider_does_not_share_yields_its_fallback()
    {
        _remote.Respond(Ok(new() { ["Security.StepUp.Enabled"] = null }));
        var reader = Reader();

        Assert.Equal(new SharedSettingValue(null, SharedSettingSource.Current), await reader.GetAsync(Provider, "Security.StepUp.Enabled"));
        Assert.Equal(new SharedSettingValue("15", SharedSettingSource.Fallback), await reader.GetAsync(Provider, "Security.StepUp.WindowMinutes"));
        Assert.Contains("Security.StepUp.WindowMinutes", Assert.Single(Logs(LogLevel.Warning)).Message, StringComparison.Ordinal);
        Assert.Single(_remote.Requests);
    }

    /// <summary>有旧值时单次回源最多等 1 秒：超时先交旧值，原回源继续，结果写回缓存。</summary>
    [Fact]
    public async Task A_slow_provider_is_waited_for_at_most_one_second_when_stale_values_exist()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _remote.Respond(Ok(new() { ["Security.StepUp.Enabled"] = "false" }));
        _remote.Respond(async () =>
        {
            await release.Task;
            return await Ok(new() { ["Security.StepUp.Enabled"] = "true" })();
        });
        var reader = Reader();
        await reader.GetAsync(Provider, "Security.StepUp.Enabled");
        _clock.Advance(TimeSpan.FromMinutes(1));

        var read = reader.GetAsync(Provider, "Security.StepUp.Enabled");
        await _clock.BudgetStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        _clock.Advance(TimeSpan.FromMilliseconds(999));
        Assert.False(read.IsCompleted);
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(new SharedSettingValue("false", SharedSettingSource.Stale), await read.WaitAsync(TimeSpan.FromSeconds(2)));

        // 节流期内不再回源；原回源完成后写回的新值随即可读
        release.SetResult();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while ((await reader.GetAsync(Provider, "Security.StepUp.Enabled")).Value != "true")
        {
            await Task.Delay(5, timeout.Token);
        }

        Assert.Equal(2, _remote.Requests.Count);
    }

    [Fact]
    public async Task Reading_an_undeclared_setting_or_provider_fails()
    {
        var reader = Reader();

        var undeclared = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetAsync(Provider, "Display.Language"));
        Assert.Contains("'Display.Language' is not declared", undeclared.Message, StringComparison.Ordinal);
        var unknown = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetAsync("Billing", "Security.StepUp.Enabled"));
        Assert.Contains("'Billing' is not registered", unknown.Message, StringComparison.Ordinal);
        Assert.Empty(_remote.Requests);
    }

    [Fact]
    public void A_provider_is_registered_once_with_at_least_one_setting()
    {
        var services = new ServiceCollection();
        services.AddSharedSettingsClient<IdentityOptions>(Provider, shared => shared.Add("Security.StepUp.Enabled", "true"));

        var duplicate = Assert.Throws<InvalidOperationException>(() =>
            services.AddSharedSettingsClient<IdentityOptions>(Provider, shared => shared.Add("Security.StepUp.WindowMinutes", "15")));
        Assert.Contains("already registered", duplicate.Message, StringComparison.Ordinal);
        var empty = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddSharedSettingsClient<IdentityOptions>(Provider, _ => { }));
        Assert.Contains("declares no settings", empty.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => new SharedSettingDeclarations().Add("A", "1").Add("A", "2"));
    }

    [Theory]
    [InlineData("FreshFor", "00:00:00")]
    [InlineData("MaxStaleness", "-00:00:01")]
    [InlineData("Path", " ")]
    public void Invalid_options_are_reported_with_their_configuration_keys(string key, string value)
    {
        var services = Services(new Dictionary<string, string?> { [$"Leistd:ServiceClients:Identity:{key}"] = value });
        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<IdentityOptions>>().Value);

        Assert.Contains($"Leistd:ServiceClients:Identity:{key}", error.Message, StringComparison.Ordinal);
    }

    // 开启作用域验证，读取器从作用域里取，与请求里的用法一致
    private ISharedSettingReader Reader(Action<IdentityOptions>? configure = null)
    {
        var services = Services(new Dictionary<string, string?>(), configure);
        services.AddSingleton<ICurrentTenant>(Tenant);
        _services = Build(services);
        return _services.CreateScope().ServiceProvider.GetRequiredService<ISharedSettingReader>();
    }

    private static ServiceProvider Build(IServiceCollection services)
        => services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    private IServiceCollection Services(Dictionary<string, string?> configuration, Action<IdentityOptions>? configure = null)
    {
        configuration["Leistd:ServiceClients:Identity:BaseAddress"] = "https://identity.test/";
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
        services.AddFakeLogging();
        services.AddSingleton<TimeProvider>(_clock);
        services.Configure<MemoryCacheOptions>(options => options.Clock = new CacheClock(_clock));
        services.AddSharedSettingsClient(
                Provider,
                shared => shared.Add("Security.StepUp.Enabled", "true").Add("Security.StepUp.WindowMinutes", "15"),
                configure,
                "Leistd:ServiceClients:Identity")
            .ConfigurePrimaryHttpMessageHandler(() => _remote);
        return services;
    }

    private List<FakeLogRecord> Logs(LogLevel level) =>
        [.. _services!.GetFakeLogCollector().GetSnapshot().Where(record => record.Level == level && record.Category!.Contains("SharedSetting", StringComparison.Ordinal))];

    private static Func<Task<HttpResponseMessage>> Ok(Dictionary<string, string?> values, string? etag = null) => () =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new SharedSettingsDto(values)) };
        if (etag is not null)
        {
            response.Headers.ETag = new(etag);
        }

        return Task.FromResult(response);
    };

    private static Task<HttpResponseMessage> NotModified() => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));

    private static Func<Task<HttpResponseMessage>> Status(HttpStatusCode status) => () => Task.FromResult(new HttpResponseMessage(status));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    public sealed class IdentityOptions : SharedSettingsClientOptions;

    // 按登记顺序逐个应答，记下每次请求的路径与 If-None-Match
    private sealed class FakeProvider : HttpMessageHandler
    {
        private readonly Queue<Func<Task<HttpResponseMessage>>> _responses = new();

        public List<(string Path, string? IfNoneMatch)> Requests { get; } = [];

        public void Respond(Func<Task<HttpResponseMessage>> response) => _responses.Enqueue(response);

        public void Fail(Func<Exception> failure) => _responses.Enqueue(() => Task.FromException<HttpResponseMessage>(failure()));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Func<Task<HttpResponseMessage>> next;
            lock (Requests)
            {
                Requests.Add((request.RequestUri!.AbsolutePath, request.Headers.IfNoneMatch.SingleOrDefault()?.ToString()));
                next = _responses.Dequeue();
            }

            return await next();
        }
    }

    // 等待预算的计时器一建立就通知测试，再推进时间
    private sealed class BudgetClock : FakeTimeProvider
    {
        public TaskCompletionSource BudgetStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            BudgetStarted.TrySetResult();
            return timer;
        }
    }

    private sealed class CacheClock(TimeProvider clock) : ISystemClock
    {
        public DateTimeOffset UtcNow => clock.GetUtcNow();
    }

    private sealed class ScopedTenantAccessor : ICurrentTenantAccessor
    {
        public BasicTenantInfo? Current { get; set; }
    }
}
