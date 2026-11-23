using Leistd.EventBus.Abstractions;
using Leistd.ExceptionHandling;
using Leistd.Lock.Abstractions;
using Leistd.MultiTenancy.Context;
using Leistd.Security.Users;
using Leistd.Settings.Definitions;
using Leistd.Settings.Dtos;
using Leistd.Settings.Errors;
using Leistd.Settings.Exceptions;
using Leistd.Settings.Management;
using Leistd.Settings.Options;
using Leistd.Settings.Resolution;
using Leistd.Settings.Stores;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Leistd.Settings.Checks;

// 探针在独立的非事务工作单元里执行（可能是一次慢的外呼，不持有数据库事务），只读本次测试那一层的存储值。
// 过期与先后分开判断，都只靠存储里的计数，不依赖时钟或被清除后已不存在的设置行：
// 开始前在写锁内登记，得到运行序号与此刻的配置修订号；写入、清除主设置或依赖设置时同事务推进修订号。
// 保存时修订号变了，结论针对的是旧配置，按过期保存；已有更新的运行保存过结果时不覆盖它（两人同时测同一项）。
// 锁与工作单元只在执行测试时才要求：没有登记测试、或换掉了写入入口的宿主不必注册它们。
internal sealed class SettingCheckService(
    ISettingDefinitionManager definitionManager,
    IEnumerable<SettingCheckRegistration> registrations,
    ISettingStore settingStore,
    IServiceProvider services,
    ICurrentUser currentUser,
    IOptions<SettingManagementOptions> options,
    ILogger<SettingCheckService> logger,
    ISettingCheckStore? checkStore = null,
    ILocalEventBus? eventBus = null,
    TimeProvider? timeProvider = null,
    ICurrentTenant? currentTenant = null,
    IDataProtectionProvider? dataProtectionProvider = null,
    IOptions<SettingEncryptionOptions>? encryptionOptions = null,
    IStringLocalizerFactory? localizerFactory = null,
    IUnitOfWorkManager? unitOfWorkManager = null,
    IDistributedLock? distributedLock = null) : ISettingCheckService
{
    private readonly IDataProtector? _protector =
        DefaultSettingProvider.CreateProtector(encryptionOptions, dataProtectionProvider);

    public async Task<SettingCheckResultDto> RunAsync(string settingName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingName);
        var definition = definitionManager.GetOrNull(settingName);
        var registration = registrations.LastOrDefault(item => item.SettingName == settingName);
        // 与设置页的 IsCheckable 同一判据：只有租户层或宿主层可写的设置能测
        if (definition is not { IsVisibleToClients: true } registered
            || registration is null
            || !(registered.Scopes.HasFlag(SettingScopes.Tenant) || registered.Scopes.HasFlag(SettingScopes.Host)))
        {
            throw new BusinessException(SettingErrorCodes.NotAvailable, $"Setting '{settingName}' has no check.")
                .WithData("Name", settingName);
        }

        var scope = registered.Scopes.HasFlag(SettingScopes.Host) ? SettingScopes.Host : SettingScopes.Tenant;
        if (scope == SettingScopes.Host && !settingStore.CanAccessHostScope)
        {
            var displayName = SettingDisplayNames.Resolve(
                registered, SettingDisplayNames.CreateLocalizer(localizerFactory, options.Value));
            throw new BusinessException(SettingErrorCodes.HostOnly, $"Setting '{displayName}' is process-wide and can only be checked on the host.")
                .WithData("Name", displayName);
        }

        var units = unitOfWorkManager ?? throw new InvalidOperationException(
            $"Running setting checks needs {nameof(IUnitOfWorkManager)}; register the unit of work component.");
        var writeLock = distributedLock ?? throw new InvalidOperationException(
            $"Running setting checks needs {nameof(IDistributedLock)}, the same lock that serializes setting writes.");
        if (units.Current is { Options.IsTransactional: true })
        {
            throw new InvalidOperationException(
                "Setting checks call external systems and must not run inside a transactional unit of work; call ISettingCheckService outside it.");
        }

        var store = checkStore ?? throw new InvalidOperationException(
            $"Setting '{settingName}' has a check but no {nameof(ISettingCheckStore)} is registered; AddSettingsEfCore registers one.");

        var run = await BeginAsync(units, writeLock, store, registration, cancellationToken);
        var outcome = await ProbeAsync(units, registration, scope, cancellationToken);
        var result = await SaveAsync(units, writeLock, store, registration, scope, outcome, run, cancellationToken);
        return SettingCheckResultDto.From(result);
    }

    // 在写锁内登记：之前提交的写入都对随后的读取可见，之后的写入都会作废这次登记
    private async Task<SettingCheckRun> BeginAsync(
        IUnitOfWorkManager units,
        IDistributedLock writeLock,
        ISettingCheckStore store,
        SettingCheckRegistration registration,
        CancellationToken cancellationToken)
    {
        await using var lease = await writeLock.LockAsync(SettingWriteLock.Key(currentTenant), cancellationToken);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.LockLost);
        using var unitOfWork = units.Begin(new UnitOfWorkOptions { IsTransactional = true }, requiresNew: true);
        var run = await store.BeginRunAsync(registration.SettingName, linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        await unitOfWork.CompleteAsync(linked.Token);
        return run;
    }

    private async Task<SettingCheckOutcome> ProbeAsync(
        IUnitOfWorkManager units,
        SettingCheckRegistration registration,
        SettingScopes scope,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = units.Begin(new UnitOfWorkOptions { IsTransactional = false }, requiresNew: true);
        var layer = await settingStore.GetAllAsync(scope, userId: null, cancellationToken);
        var context = new SettingCheckContext(
            registration.SettingName,
            scope,
            (name, _) => Task.FromResult(ReadStored(name, layer)),
            name => definitionManager.GetOrNull(name)?.DefaultValue);

        SettingCheckOutcome outcome;
        try
        {
            var check = (ISettingCheck)services.GetRequiredService(registration.CheckType);
            outcome = await check.ExecuteAsync(context, cancellationToken);
        }
        catch (SettingDecryptionException)
        {
            // 解密点已记过事件
            outcome = SettingCheckOutcome.Fail(SettingCheckFailureCodes.SecretUnavailable);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Setting check for {SettingName} threw; recorded as {FailureCode}.",
                registration.SettingName, SettingCheckFailureCodes.UnexpectedError);
            outcome = SettingCheckOutcome.Fail(SettingCheckFailureCodes.UnexpectedError);
        }

        await unitOfWork.CompleteAsync(cancellationToken);
        return outcome;
    }

    private async Task<SettingCheckResult> SaveAsync(
        IUnitOfWorkManager units,
        IDistributedLock writeLock,
        ISettingCheckStore store,
        SettingCheckRegistration registration,
        SettingScopes scope,
        SettingCheckOutcome outcome,
        SettingCheckRun run,
        CancellationToken cancellationToken)
    {
        await using var lease = await writeLock.LockAsync(SettingWriteLock.Key(currentTenant), cancellationToken);
        // 持锁资格失效时不再提交：否则锁外的写入可能先完成过期标记，再被这里覆盖成未过期
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.LockLost);
        cancellationToken = linked.Token;
        using var unitOfWork = units.Begin(new UnitOfWorkOptions { IsTransactional = true }, requiresNew: true);

        // 修订号变了：探针期间主设置或依赖设置被写入、清除过，结论针对的是旧配置
        var latest = await store.GetLatestAsync(registration.SettingName, cancellationToken);
        var changed = latest.Revision != run.Revision;
        var result = new SettingCheckResult(
            outcome.Passed,
            outcome.FailureCode,
            (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime,
            currentUser.IsAuthenticated ? currentUser.Id?.ToString() : null,
            currentUser.IsAuthenticated ? currentUser.Name ?? currentUser.Username : null,
            IsStale: changed);

        cancellationToken.ThrowIfCancellationRequested();
        // 更新的运行已经保存过：它的结论不比这次旧，不覆盖；本次结果照常返回给调用方
        if (latest.Sequence < run.Sequence)
        {
            await store.SaveAsync(registration.SettingName, run.Sequence, result, cancellationToken);
        }

        if (eventBus is not null)
        {
            await eventBus.PublishAsync(
                new SettingCheckCompletedEvent(registration.SettingName, scope, outcome.Passed, outcome.FailureCode), cancellationToken);
        }

        await unitOfWork.CompleteAsync(cancellationToken);
        return result;
    }

    private string? ReadStored(string name, IReadOnlyDictionary<string, string> layer)
    {
        var definition = definitionManager.GetOrNull(name) ?? throw new UndefinedSettingException(name);
        if (!layer.TryGetValue(name, out var stored))
        {
            return null;
        }

        return definition.IsEncrypted ? DefaultSettingProvider.Decrypt(_protector, name, stored, logger) : stored;
    }
}
