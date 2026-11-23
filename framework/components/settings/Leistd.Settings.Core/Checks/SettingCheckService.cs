using Leistd.EventBus.Abstractions;
using Leistd.ExceptionHandling;
using Leistd.Security.Users;
using Leistd.Settings.Definitions;
using Leistd.Settings.Dtos;
using Leistd.Settings.Errors;
using Leistd.Settings.Exceptions;
using Leistd.Settings.Options;
using Leistd.Settings.Stores;
using Leistd.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Leistd.Settings.Checks;

// 探针在工作单元之外执行（可能是一次慢的外呼），只有结果的保存与事件进入一个短事务
internal sealed class SettingCheckService(
    ISettingDefinitionManager definitionManager,
    IEnumerable<SettingCheckRegistration> registrations,
    ISettingStore settingStore,
    ISettingCheckStore checkStore,
    IServiceProvider services,
    IUnitOfWorkManager unitOfWorkManager,
    ICurrentUser currentUser,
    IOptions<SettingManagementOptions> options,
    ILogger<SettingCheckService> logger,
    ILocalEventBus? eventBus = null,
    TimeProvider? timeProvider = null,
    IStringLocalizerFactory? localizerFactory = null) : ISettingCheckService
{
    public async Task<SettingCheckResultDto> RunAsync(string settingName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingName);
        var definition = definitionManager.GetOrNull(settingName);
        var registration = registrations.LastOrDefault(item => item.SettingName == settingName);
        if (definition is not { IsVisibleToClients: true } || registration is null)
        {
            throw new BusinessException(SettingErrorCodes.NotAvailable, $"Setting '{settingName}' has no check.")
                .WithData("Name", settingName);
        }

        var scope = definition.Scopes.HasFlag(SettingScopes.Host) ? SettingScopes.Host : SettingScopes.Tenant;
        if (scope == SettingScopes.Host && !settingStore.CanAccessHostScope)
        {
            var displayName = SettingDisplayNames.Resolve(
                definition, SettingDisplayNames.CreateLocalizer(localizerFactory, options.Value));
            throw new BusinessException(SettingErrorCodes.HostOnly, $"Setting '{displayName}' is process-wide and can only be checked on the host.")
                .WithData("Name", displayName);
        }

        var outcome = await ProbeAsync(registration, new SettingCheckContext(settingName, scope), cancellationToken);
        var result = new SettingCheckResult(
            outcome.Passed,
            outcome.FailureCode,
            (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime,
            currentUser.IsAuthenticated ? currentUser.Id?.ToString() : null,
            currentUser.IsAuthenticated ? currentUser.Name ?? currentUser.Username : null,
            IsStale: false);

        using (var unitOfWork = unitOfWorkManager.Begin(requiresNew: true))
        {
            await checkStore.SaveAsync(settingName, result, cancellationToken);
            if (eventBus is not null)
            {
                await eventBus.PublishAsync(
                    new SettingCheckCompletedEvent(settingName, scope, outcome.Passed, outcome.FailureCode), cancellationToken);
            }

            await unitOfWork.CompleteAsync(cancellationToken);
        }

        return SettingCheckResultDto.From(result);
    }

    private async Task<SettingCheckOutcome> ProbeAsync(
        SettingCheckRegistration registration,
        SettingCheckContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var check = (ISettingCheck)services.GetRequiredService(registration.CheckType);
            return await check.ExecuteAsync(context, cancellationToken);
        }
        catch (SettingDecryptionException)
        {
            // 解密点已记过事件
            return SettingCheckOutcome.Fail(SettingCheckFailureCodes.SecretUnavailable);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Setting check for {SettingName} threw; recorded as {FailureCode}.",
                context.SettingName, SettingCheckFailureCodes.UnexpectedError);
            return SettingCheckOutcome.Fail(SettingCheckFailureCodes.UnexpectedError);
        }
    }
}
