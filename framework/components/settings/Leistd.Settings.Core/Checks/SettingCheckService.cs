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
// 保存时取与设置写入同一把锁，重读主设置与依赖设置的修订（原始值加最后写入时间）：每次写入都刷新写入时间，
// 写回同值、清除后又写回原值也能看出来；探针期间有过写入或清除，结果直接按过期保存。锁之后的写入照常把它标记为过期。
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

        var (outcome, before) = await ProbeAsync(units, registration, scope, cancellationToken);
        var result = await SaveAsync(units, writeLock, store, registration, scope, outcome, before, cancellationToken);
        return SettingCheckResultDto.From(result);
    }

    private async Task<(SettingCheckOutcome Outcome, IReadOnlyDictionary<string, Revision> Before)> ProbeAsync(
        IUnitOfWorkManager units,
        SettingCheckRegistration registration,
        SettingScopes scope,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = units.Begin(new UnitOfWorkOptions { IsTransactional = false }, requiresNew: true);
        var layer = await settingStore.GetAllAsync(scope, userId: null, cancellationToken);
        var before = await RevisionsAsync(registration, scope, layer, cancellationToken);
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
        return (outcome, before);
    }

    private async Task<SettingCheckResult> SaveAsync(
        IUnitOfWorkManager units,
        IDistributedLock writeLock,
        ISettingCheckStore store,
        SettingCheckRegistration registration,
        SettingScopes scope,
        SettingCheckOutcome outcome,
        IReadOnlyDictionary<string, Revision> before,
        CancellationToken cancellationToken)
    {
        await using var lease = await writeLock.LockAsync(SettingWriteLock.Key(currentTenant), cancellationToken);
        // 持锁资格失效时不再提交：否则锁外的写入可能先完成过期标记，再被这里覆盖成未过期
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.LockLost);
        cancellationToken = linked.Token;
        using var unitOfWork = units.Begin(new UnitOfWorkOptions { IsTransactional = true }, requiresNew: true);

        // 探针期间主设置或依赖设置被写入、清除过：结论针对的是旧配置
        var now = await RevisionsAsync(
            registration, scope, await settingStore.GetAllAsync(scope, userId: null, cancellationToken), cancellationToken);
        var changed = before.Any(pair => now[pair.Key] != pair.Value);
        var result = new SettingCheckResult(
            outcome.Passed,
            outcome.FailureCode,
            (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime,
            currentUser.IsAuthenticated ? currentUser.Id?.ToString() : null,
            currentUser.IsAuthenticated ? currentUser.Name ?? currentUser.Username : null,
            IsStale: changed);

        cancellationToken.ThrowIfCancellationRequested();
        await store.SaveAsync(registration.SettingName, result, cancellationToken);
        if (eventBus is not null)
        {
            await eventBus.PublishAsync(
                new SettingCheckCompletedEvent(registration.SettingName, scope, outcome.Passed, outcome.FailureCode), cancellationToken);
        }

        await unitOfWork.CompleteAsync(cancellationToken);
        return result;
    }

    // 主设置与依赖设置在本层的修订：原始存储值（密文不解）与最后写入时间；没有值记为空修订。
    // 每次写入都刷新写入时间，因此写回同值、清除后又写回原值都会改变修订
    private async Task<Dictionary<string, Revision>> RevisionsAsync(
        SettingCheckRegistration registration,
        SettingScopes scope,
        IReadOnlyDictionary<string, string> layer,
        CancellationToken cancellationToken)
    {
        var modifications = await settingStore.GetModificationsAsync(scope, userId: null, cancellationToken);
        return registration.Covers.Distinct(StringComparer.Ordinal).ToDictionary(
            name => name,
            name => new Revision(layer.GetValueOrDefault(name), modifications.GetValueOrDefault(name)?.Time),
            StringComparer.Ordinal);
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

    private readonly record struct Revision(string? Value, DateTime? WrittenAt);
}
