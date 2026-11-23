using Leistd.EventBus.Abstractions;
using Leistd.EventBus.Events;
using Leistd.ExceptionHandling;
using Leistd.Lock.Memory;
using Leistd.Security.Users;
using Leistd.Settings.Checks;
using Leistd.Settings.Definitions;
using Leistd.Settings.Dtos;
using Leistd.Settings.Errors;
using Leistd.Settings.Events;
using Leistd.Settings.Exceptions;
using Leistd.Settings.Management;
using Leistd.Settings.Resolution;
using Leistd.Settings.Stores;
using Leistd.Settings.Tests.TestDoubles;
using Leistd.TestBase.Doubles;
using Leistd.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.Settings.Tests.Core;

/// <summary>设置页给出每层的最后写入人与时间；登记的测试按当前租户保存最近结果，依赖设置被写入或清除即过期。</summary>
public sealed class SettingModificationAndCheckTests
{
    private static readonly Guid AdminId = Guid.NewGuid();

    private readonly InMemorySettingStore _store = new();
    private readonly InMemorySettingCheckStore _checks = new();
    private readonly List<IEvent> _events = [];
    private readonly ProbeCheck _probe = new();

    [Fact]
    public async Task Each_level_reports_who_wrote_it_and_when()
    {
        var services = Services();
        await services.GetRequiredService<ISettingManagementService>()
            .SetForCurrentTenantAsync(new SetSettingInputDto("Export.Endpoint", "https://collector:4318"));

        var endpoint = Single(await services.GetRequiredService<ISettingManagementService>().GetAsync(), "Export.Endpoint");

        Assert.NotNull(endpoint.TenantModification);
        Assert.Equal(AdminId.ToString(), endpoint.TenantModification.ModifierId);
        Assert.Equal("Alice Admin", endpoint.TenantModification.ModifierName);
        Assert.Equal(DateTimeKind.Utc, endpoint.TenantModification.Time.Kind);
        Assert.Null(endpoint.UserModification);
    }

    // 机密值不下发，修改信息照常给出：只写不读之后，排查靠"谁、什么时候改过"
    [Fact]
    public async Task Secret_settings_report_the_modification_without_the_value()
    {
        var services = Services();
        await services.GetRequiredService<ISettingManager>().SetAsync("Export.Headers", "api-key=s3cret", SettingScopes.Tenant);

        var headers = Single(await services.GetRequiredService<ISettingManagementService>().GetAsync(), "Export.Headers");

        Assert.Null(headers.TenantValue);
        Assert.True(headers.HasSecretValue);
        Assert.Equal("Alice Admin", headers.TenantModification?.ModifierName);
    }

    // 无主体写入（迁移、种子、服务账号）只有时间，没有写入人
    [Fact]
    public async Task A_write_without_a_principal_records_only_the_time()
    {
        var services = Services(new FakeCurrentUser());
        await services.GetRequiredService<ISettingManager>().SetAsync("Export.Endpoint", "https://collector:4318", SettingScopes.Tenant);

        var modification = Assert.Single(await _store.GetModificationsAsync(SettingScopes.Tenant, userId: null)).Value;

        Assert.Null(modification.ModifierId);
        Assert.Null(modification.ModifierName);
    }

    [Fact]
    public async Task A_registered_check_runs_and_its_result_is_listed()
    {
        var services = Services();
        _probe.Next = SettingCheckOutcome.Fail(SettingCheckFailureCodes.AuthRejected);

        var result = await services.GetRequiredService<ISettingCheckService>().RunAsync("Export.Headers");
        var listed = Single(await services.GetRequiredService<ISettingManagementService>().GetAsync(), "Export.Headers");

        Assert.Equal(("Export.Headers", SettingScopes.Tenant), (_probe.LastContext!.SettingName, _probe.LastContext.Scope));
        Assert.False(result.Passed);
        Assert.Equal(SettingCheckFailureCodes.AuthRejected, result.FailureCode);
        Assert.Equal("Alice Admin", result.CheckerName);
        Assert.True(listed.IsCheckable);
        Assert.Equal(result, listed.LastCheck);
        Assert.False(Single(await services.GetRequiredService<ISettingManagementService>().GetAsync(), "Export.Endpoint").IsCheckable);
        var completed = Assert.IsType<SettingCheckCompletedEvent>(Assert.Single(_events));
        Assert.Equal(("Export.Headers", false, SettingCheckFailureCodes.AuthRejected), (completed.SettingName, completed.Passed, completed.FailureCode));
    }

    // 主设置或依赖设置被写入、被清除都让结论过期；再测一次即恢复
    [Theory]
    [InlineData("Export.Headers", "api-key=changed")]
    [InlineData("Export.Endpoint", "https://other:4318")]
    [InlineData("Export.Endpoint", null)]
    public async Task Writing_or_clearing_a_covered_setting_marks_the_result_stale(string name, string? value)
    {
        var services = Services();
        var manager = services.GetRequiredService<ISettingManager>();
        await manager.SetAsync("Export.Endpoint", "https://collector:4318", SettingScopes.Tenant);
        await services.GetRequiredService<ISettingCheckService>().RunAsync("Export.Headers");

        await manager.SetAsync(name, value, SettingScopes.Tenant);

        Assert.True((await _checks.GetAllAsync())["Export.Headers"].IsStale);
        await services.GetRequiredService<ISettingCheckService>().RunAsync("Export.Headers");
        Assert.False((await _checks.GetAllAsync())["Export.Headers"].IsStale);
    }

    // 个人偏好不改租户的配置，结论保持
    [Fact]
    public async Task A_personal_override_does_not_mark_the_result_stale()
    {
        var services = Services();
        await services.GetRequiredService<ISettingCheckService>().RunAsync("Export.Headers");

        await services.GetRequiredService<ISettingManager>().SetAsync("Display.TimeZone", "Asia/Tokyo", SettingScopes.User, AdminId.ToString());
        await services.GetRequiredService<ISettingManager>().SetAsync("Export.Endpoint", "https://collector:4318", SettingScopes.User, AdminId.ToString());

        Assert.False((await _checks.GetAllAsync())["Export.Headers"].IsStale);
    }

    // 探针读到解不开的机密不需要自己处理：归一为 secret-unavailable 保存，不抛给调用方
    [Fact]
    public async Task A_secret_that_cannot_be_decrypted_is_recorded_as_unavailable()
    {
        var services = Services();
        _probe.Throw = new SettingDecryptionException("Export.Headers", ["p", "Export.Headers"], keyId: null, new InvalidOperationException());

        var result = await services.GetRequiredService<ISettingCheckService>().RunAsync("Export.Headers");

        Assert.Equal(SettingCheckFailureCodes.SecretUnavailable, result.FailureCode);
    }

    // 探针自身的异常不外抛，按 unexpected-error 保存
    [Fact]
    public async Task A_probe_that_throws_is_recorded_as_an_unexpected_error()
    {
        var services = Services();
        _probe.Throw = new HttpRequestException("boom");

        var result = await services.GetRequiredService<ISettingCheckService>().RunAsync("Export.Headers");

        Assert.Equal(SettingCheckFailureCodes.UnexpectedError, result.FailureCode);
    }

    // 探针运行期间另一个请求改了依赖并提交：结论针对的是旧配置，直接按过期保存，不能显示成当前结论
    [Theory]
    [InlineData("https://other:4318")]
    [InlineData(null)]
    public async Task A_change_committed_while_the_probe_runs_saves_the_result_as_stale(string? value)
    {
        var services = Services();
        await services.GetRequiredService<ISettingManager>().SetAsync("Export.Endpoint", "https://collector:4318", SettingScopes.Tenant);
        var release = new TaskCompletionSource();
        _probe.Pause = release.Task;

        var running = services.GetRequiredService<ISettingCheckService>().RunAsync("Export.Headers");
        await _probe.Entered.Task;
        await Services().GetRequiredService<ISettingManager>().SetAsync("Export.Endpoint", value, SettingScopes.Tenant);
        release.SetResult();

        Assert.True((await running).IsStale);
        Assert.True((await _checks.GetAllAsync())["Export.Headers"].IsStale);
    }

    // 探针读的是测试那一层的值：全租户共享的结论不能来自操作者自己的个人覆盖
    [Fact]
    public async Task The_probe_reads_the_tested_level_rather_than_a_personal_override()
    {
        var services = Services();
        var manager = services.GetRequiredService<ISettingManager>();
        await manager.SetAsync("Export.Endpoint", "https://tenant:4318", SettingScopes.Tenant);
        await manager.SetAsync("Export.Endpoint", "https://mine:4318", SettingScopes.User, AdminId.ToString());
        _probe.Read = "Export.Endpoint";

        await services.GetRequiredService<ISettingCheckService>().RunAsync("Export.Headers");

        Assert.Equal("https://tenant:4318", _probe.ReadValue);
    }

    // 探针外呼期间不能持有调用方的数据库事务
    [Fact]
    public async Task A_check_refuses_to_run_inside_a_transactional_unit_of_work()
    {
        var services = Services();
        var unitOfWork = (RecordingUnitOfWorkManager)services.GetRequiredService<IUnitOfWorkManager>();
        unitOfWork.Current = unitOfWork.Begin(new Leistd.UnitOfWork.Options.UnitOfWorkOptions { IsTransactional = true });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => services.GetRequiredService<ISettingCheckService>().RunAsync("Export.Headers"));
        Assert.Null(_probe.LastContext);
    }

    [Theory]
    [InlineData("Export.Endpoint")]
    [InlineData("Missing.Setting")]
    [InlineData("Display.Personal")]
    public async Task A_setting_without_a_check_is_not_available(string name)
    {
        var error = await Assert.ThrowsAsync<BusinessException>(
            () => Services().GetRequiredService<ISettingCheckService>().RunAsync(name));

        Assert.Equal(SettingErrorCodes.NotAvailable, error.Code);
    }

    [Theory]
    [InlineData("https://collector:4318", false)]
    [InlineData(null, true)]
    public async Task The_change_event_tells_a_write_from_a_clear(string? value, bool cleared)
    {
        var services = Services();
        await services.GetRequiredService<ISettingManager>().SetAsync("Export.Endpoint", "https://first:4318", SettingScopes.Tenant);
        _events.Clear();

        await services.GetRequiredService<ISettingManager>().SetAsync("Export.Endpoint", value, SettingScopes.Tenant);

        Assert.Equal(cleared, Assert.IsType<SettingChangedEvent>(Assert.Single(_events)).IsCleared);
    }

    private static SettingOutputDto Single(IReadOnlyList<SettingOutputDto> settings, string name)
        => Assert.Single(settings, setting => setting.Name == name);

    private IServiceProvider Services(FakeCurrentUser? currentUser = null)
    {
        var services = new ServiceCollection()
            .AddSettingsCore()
            .AddSettingCheck<ProbeCheck>("Export.Headers", "Export.Endpoint")
            // 只允许个人覆盖的设置登记了探针也不能测：没有可管理的层
            .AddSettingCheck<ProbeCheck>("Display.Personal")
            .AddSingleton<ISettingDefinitionProvider, Definitions>()
            .AddSingleton<ISettingStore>(_store)
            .AddSingleton<ISettingCheckStore>(_checks)
            .AddSingleton(_probe)
            .AddSingleton<ICurrentUser>(currentUser ?? new FakeCurrentUser(AdminId, "alice", "Alice Admin"))
            .AddSingleton<ILocalEventBus>(new RecordingEventBus(_events))
            .AddSingleton<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(
                new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider())
            .AddLogging()
            .AddMemoryLocalLock()
            .AddSingleton<IUnitOfWorkManager>(new RecordingUnitOfWorkManager());
        // 测试替身由用例直接控制，替换登记时的作用域探针
        services.AddSingleton<ProbeCheck>(_probe);
        return services.BuildServiceProvider().CreateScope().ServiceProvider;
    }

    private sealed class Definitions : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context)
        {
            context.Add("Display.TimeZone", "UTC", SettingScopes.All).IsVisibleToClients = true;
            context.Add("Display.Personal", scopes: SettingScopes.User).IsVisibleToClients = true;
            context.Add("Export.Endpoint", scopes: SettingScopes.All).IsVisibleToClients = true;
            var headers = context.Add("Export.Headers", scopes: SettingScopes.Tenant);
            headers.IsVisibleToClients = true;
            headers.IsEncrypted = true;
        }
    }

    internal sealed class ProbeCheck : ISettingCheck
    {
        public SettingCheckOutcome Next { get; set; } = SettingCheckOutcome.Pass();

        public Exception? Throw { get; set; }

        public SettingCheckContext? LastContext { get; private set; }

        /// <summary>不为空时探针在这里等待，模拟一次慢的外呼。</summary>
        public Task? Pause { get; set; }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>不为空时探针经上下文读这一项，读到的值记在 <see cref="ReadValue"/>。</summary>
        public string? Read { get; set; }

        public string? ReadValue { get; private set; }

        public async Task<SettingCheckOutcome> ExecuteAsync(SettingCheckContext context, CancellationToken cancellationToken)
        {
            LastContext = context;
            if (Read is not null)
            {
                ReadValue = await context.GetValueAsync(Read, cancellationToken);
            }

            Entered.TrySetResult();
            if (Pause is not null)
            {
                await Pause;
            }

            return Throw is null ? Next : throw Throw;
        }
    }

    private sealed class RecordingEventBus(List<IEvent> events) : ILocalEventBus
    {
        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : IEvent
            => PublishAsync((IEvent)@event, cancellationToken);

        public Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default)
        {
            events.Add(@event);
            return Task.CompletedTask;
        }
    }
}
