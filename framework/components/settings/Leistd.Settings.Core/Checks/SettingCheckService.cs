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
// 保存时取与设置写入同一把锁，重读主设置与依赖设置的原始值：探针期间有写入或清除提交过，结果直接按过期保存；
// 锁之后的写入会照常把它标记为过期。
internal sealed class SettingCheckService(
    ISettingDefinitionManager definitionManager,
    IEnumerable<SettingCheckRegistration> registrations,
    ISettingStore settingStore,
    IServiceProvider services,
    IUnitOfWorkManager unitOfWorkManager,
    IDistributedLock distributedLock,
    ICurrentUser currentUser,
    IOptions<SettingManagementOptions> options,
    ILogger<SettingCheckService> logger,
    ISettingCheckStore? checkStore = null,
    ILocalEventBus? eventBus = null,
    TimeProvider? timeProvider = null,
    ICurrentTenant? currentTenant = null,
    IDataProtectionProvider? dataProtectionProvider = null,
    IOptions<SettingEncryptionOptions>? encryptionOptions = null,
    IStringLocalizerFactory? localizerFactory = null) : ISettingCheckService
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

        if (unitOfWorkManager.Current is { Options.IsTransactional: true })
        {
            throw new InvalidOperationException(
                "Setting checks call external systems and must not run inside a transactional unit of work; call ISettingCheckService outside it.");
        }

        var store = checkStore ?? throw new InvalidOperationException(
            $"Setting '{settingName}' has a check but no {nameof(ISettingCheckStore)} is registered; AddSettingsEfCore registers one.");

        var (outcome, before) = await ProbeAsync(registration, scope, cancellationToken);
        var result = await SaveAsync(store, registration, scope, outcome, before, cancellationToken);
        return SettingCheckResultDto.From(result);
    }

    private async Task<(SettingCheckOutcome Outcome, IReadOnlyDictionary<string, string?> Before)> ProbeAsync(
        SettingCheckRegistration registration,
        SettingScopes scope,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = unitOfWorkManager.Begin(new UnitOfWorkOptions { IsTransactional = false }, requiresNew: true);
        var layer = await settingStore.GetAllAsync(scope, userId: null, cancellationToken);
        var before = Snapshot(registration, layer);
        var context = new SettingCheckContext(registration.SettingName, scope, (name, _) => Task.FromResult(Read(name, layer)));

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
        ISettingCheckStore store,
        SettingCheckRegistration registration,
        SettingScopes scope,
        SettingCheckOutcome outcome,
        IReadOnlyDictionary<string, string?> before,
        CancellationToken cancellationToken)
    {
        await using var lease = await distributedLock.LockAsync(SettingWriteLock.Key(currentTenant), cancellationToken);
        using var unitOfWork = unitOfWorkManager.Begin(new UnitOfWorkOptions { IsTransactional = true }, requiresNew: true);

        // 探针期间主设置或依赖设置被写入、清除过：结论针对的是旧配置
        var now = Snapshot(registration, await settingStore.GetAllAsync(scope, userId: null, cancellationToken));
        var changed = before.Any(pair => now[pair.Key] != pair.Value);
        var result = new SettingCheckResult(
            outcome.Passed,
            outcome.FailureCode,
            (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime,
            currentUser.IsAuthenticated ? currentUser.Id?.ToString() : null,
            currentUser.IsAuthenticated ? currentUser.Name ?? currentUser.Username : null,
            IsStale: changed);

        await store.SaveAsync(registration.SettingName, result, cancellationToken);
        if (eventBus is not null)
        {
            await eventBus.PublishAsync(
                new SettingCheckCompletedEvent(registration.SettingName, scope, outcome.Passed, outcome.FailureCode), cancellationToken);
        }

        await unitOfWork.CompleteAsync(cancellationToken);
        return result;
    }

    // 主设置与依赖设置在本层的原始存储值（密文不解）；没有值记为 null，清除因此也算变化
    private static Dictionary<string, string?> Snapshot(SettingCheckRegistration registration, IReadOnlyDictionary<string, string> layer)
        => registration.Covers.Distinct(StringComparer.Ordinal)
            .ToDictionary(name => name, name => layer.GetValueOrDefault(name), StringComparer.Ordinal);

    private string? Read(string name, IReadOnlyDictionary<string, string> layer)
    {
        var definition = definitionManager.GetOrNull(name) ?? throw new UndefinedSettingException(name);
        if (!layer.TryGetValue(name, out var stored))
        {
            return definition.DefaultValue;
        }

        return definition.IsEncrypted ? DefaultSettingProvider.Decrypt(_protector, name, stored, logger) : stored;
    }
}
