using CompanyName.ProjectName.Application.OperationRecords.Provider;
using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Application.Settings.Provider;
using Leistd.Authorization.Constants;
using Leistd.Authorization.Events;
using Leistd.EventBus.EventHandlers;
#if (LocalIdentity && IncludeMultiTenancy)
using Leistd.MultiTenancy.Management.Events;
#endif
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Recording;
using Leistd.Settings.Definitions;
using Leistd.Settings.Events;
#if (IncludeWebhooks)
using CompanyName.ProjectName.Application.Webhooks.Errors;
using Leistd.Webhooks.Events;
using Leistd.Webhooks.Management;
#endif

#if (IncludeDistributedEvents)
using Leistd.EventBus.Distributed.Events;
using Leistd.EventBus.Distributed.Stores;
using System.Text.Json;
using Microsoft.Extensions.Logging;
#endif

namespace CompanyName.ProjectName.Application.OperationRecords.EventHandlers;

/// <summary>设置变更留痕：租户层与宿主层的写入记一条，个人偏好不记。</summary>
/// <remarks>
/// <para>目标标识带出作用域：同一个设置名在宿主与租户两层各有一行，只记名字会让两层的变更在审计里长得一模一样。</para>
/// <para>个人偏好刻意不记：不改变能力边界也不改变共享数据，却是高频写入——审计表的价值来自密度。</para>
/// <para>在写入提交之后执行（本地事件处理器的默认阶段），回滚了的写入不会留下"记了但没发生"的记录。</para>
/// </remarks>
internal sealed class SettingChangedAuditHandler(
    IOperationRecorder recorder,
    ISettingDefinitionManager definitions) : IEventHandler<SettingChangedEvent>
{
    public Task HandleAsync(SettingChangedEvent @event, CancellationToken cancellationToken = default)
        => @event.Scope == SettingScopes.User
            ? Task.CompletedTask
            : recorder.RecordSucceededAsync(
                OperationRecordActions.SettingChanged,
                OperationTarget.For($"{@event.Scope}/{@event.Name}", @event.Name),
                // 凭什么：该设置所在分组的管理子权限；未登记分组的只要求父权限
                definitions.GetOrNull(@event.Name)?.Group is { } group
                && SettingGroupPermissions.All.TryGetValue(group, out var permission)
                    ? permission
                    : PermissionConstant.Settings.Default,
                cancellationToken);
}

/// <summary>权限授予整体替换留痕，是整套权限体系里最敏感的写操作之一。</summary>
/// <remarks>
/// 目标标识是 <c>{类型}/{Key}</c>，与端点上补记被拒尝试的元数据拼法逐字一致，按目标检索才查得全。
/// 授权依据按主体类型判定，不能写死成角色那个权限：给用户直授时那会在审计表里留下一条撒谎的"凭什么"。
/// </remarks>
internal sealed class PermissionGrantsReplacedAuditHandler(IOperationRecorder recorder) : IEventHandler<PermissionGrantsReplacedEvent>
{
    public Task HandleAsync(PermissionGrantsReplacedEvent @event, CancellationToken cancellationToken = default)
        => recorder.RecordSucceededAsync(
            OperationRecordActions.PermissionGrantsReplaced,
            OperationTarget.For($"{@event.ProviderName}/{@event.ProviderKey}", @event.SubjectDisplayName),
            @event.ProviderName == PermissionGrantProviderNames.User
                ? OperationRecordAuthorizations.DirectUserGrant
                : PermissionConstant.Roles.ManagePermissions,
            cancellationToken);
}
#if (LocalIdentity && IncludeMultiTenancy)

/// <summary>租户生命周期留痕。事件只在各步都已提交后发布：创建失败并已补偿时不会有"创建成功"的假账。</summary>
internal sealed class TenantChangedAuditHandler(IOperationRecorder recorder) : IEventHandler<TenantChangedEvent>
{
    public Task HandleAsync(TenantChangedEvent @event, CancellationToken cancellationToken = default)
    {
        var (action, basis) = @event.Change switch
        {
            TenantChangeKind.Created => (OperationRecordActions.TenantCreated, PermissionConstant.Tenants.Create),
            TenantChangeKind.Updated => (OperationRecordActions.TenantUpdated, PermissionConstant.Tenants.Update),
            // 启停是 Critical：它改变的是"这一整批人还能不能进来"
            TenantChangeKind.ActivationChanged => (OperationRecordActions.TenantActivationChanged, PermissionConstant.Tenants.Update),
            TenantChangeKind.Deleted => (OperationRecordActions.TenantDeleted, PermissionConstant.Tenants.Delete),
            _ => throw new ArgumentOutOfRangeException(nameof(@event), @event.Change, "Unknown tenant change.")
        };

        return recorder.RecordSucceededAsync(action, OperationTarget.For(@event.TenantId, @event.DisplayName), basis, cancellationToken);
    }
}

/// <summary>租户连接登记的变化记成操作记录。</summary>
/// <remarks>
/// <para>连接决定租户数据落在哪个库，改动必须留痕。事件里没有连接串（它是凭据），
/// 记录只写租户与连接名；变化类型体现在动作码上，登记、改写、删除各一个。
/// 事件带的版本号没有记——操作记录没有放它的位置，而目前也没有按版本检索的需求，
/// 不为存一个版本号去扩展通用的成功记录模型。</para>
/// <para>目标标识是 <c>{租户标识}/{连接名}</c>，与设置、权限两个处理器的拼法一致：
/// 同一个租户的每个连接名各是一个可被改动的对象，只写租户标识会让它们在审计里混成一行。
/// 目标名是<b>租户显示名</b>而不是连接名——界面按目标名展示，写连接名会显示成
/// "为租户 default 登记了数据库连接"。</para>
/// </remarks>
internal sealed class TenantConnectionChangedAuditHandler(IOperationRecorder recorder)
    : IEventHandler<TenantConnectionChangedEvent>
{
    /// <inheritdoc />
    public Task HandleAsync(TenantConnectionChangedEvent @event, CancellationToken cancellationToken = default)
        => recorder.RecordSucceededAsync(
            ActionFor(@event.Change),
            OperationTarget.For($"{@event.TenantId}/{@event.Name}", @event.TenantDisplayName),
            PermissionConstant.Tenants.Update,
            cancellationToken);

    // 事件带着变化类型，记录也要带：合成一个码之后，事后分不出是新增落点、换库还是退回宿主库。
    // 三个分支都显式列出，不用 _ 兜底：组件将来给枚举加值时，兜底会把它静默归成"改写"，
    // 审计表里多出一批张冠李戴的记录，而且不报错。宁可在这里炸，逼人补一个动作码。
    private static string ActionFor(TenantConnectionChangeKind change) => change switch
    {
        TenantConnectionChangeKind.Registered => OperationRecordActions.TenantConnectionRegistered,
        TenantConnectionChangeKind.Changed => OperationRecordActions.TenantConnectionChanged,
        TenantConnectionChangeKind.Removed => OperationRecordActions.TenantConnectionRemoved,
        _ => throw new ArgumentOutOfRangeException(
            nameof(change), change, "This connection change kind has no operation action code yet.")
    };
}
#endif
#if (IncludeWebhooks)

/// <summary>Webhook 订阅的创建、修改、轮换密钥、启停与删除留痕：它们决定业务数据推往哪个外部地址、凭哪把密钥验签。</summary>
/// <remarks>
/// <para>管理员的操作按权限记成功。投递链路的自动停用（端点已注销、连续失败）同样改变了订阅状态，照账号锁定的写法记：
/// 单独的动作码、失败结果、原因码写进失败原因；没有主体，操作人为空，授权依据是投递策略。</para>
/// <para>自动停用的事件在投递链路的工作单元提交后分发，此时处于订阅所属租户的上下文；失败记录走独立事务，不受投递事务影响。</para>
/// <para>测试事件与手动重发不改变"谁能收到、凭什么验签"，不记。</para>
/// <para>目标是订阅标识与名称快照，与端点上补记被拒尝试的标识（路由里的 <c>id</c>）一致。</para>
/// </remarks>
internal sealed class WebhookSubscriptionChangedAuditHandler(IOperationRecorder recorder)
    : IEventHandler<WebhookSubscriptionChangedEvent>
{
    /// <inheritdoc />
    public Task HandleAsync(WebhookSubscriptionChangedEvent @event, CancellationToken cancellationToken = default)
    {
        var target = OperationTarget.For(@event.SubscriptionId, @event.SubscriptionName);
        if (@event.Change == WebhookSubscriptionChangeKind.Disabled && @event.DisabledReason != WebhookDisabledReason.Manual)
        {
            return recorder.RecordFailedAsync(
                OperationRecordActions.WebhookSubscriptionAutoDisabled,
                target,
                OperationRecordAuthorizations.WebhookDeliveryPolicy,
                OperationFailure.FromCode(AutoDisabledReasonCode(@event.DisabledReason)));
        }

        return recorder.RecordSucceededAsync(
            ActionFor(@event.Change),
            target,
            PermissionConstant.Webhooks.Manage,
            cancellationToken);
    }

    // 同样不兜底：组件加了停用原因时要补一个原因码，而不是把它记成别的原因
    private static string AutoDisabledReasonCode(WebhookDisabledReason? reason) => reason switch
    {
        WebhookDisabledReason.EndpointGone => AppWebhookErrorCodes.EndpointGone,
        WebhookDisabledReason.ConsecutiveFailures => AppWebhookErrorCodes.ConsecutiveFailures,
        _ => throw new ArgumentOutOfRangeException(
            nameof(reason), reason, "This automatic disable reason has no operation record reason code yet.")
    };

    // 不用 _ 兜底成某个已有动作码：组件将来加变更类别时宁可在这里炸，逼人补一个动作码
    private static string ActionFor(WebhookSubscriptionChangeKind change) => change switch
    {
        WebhookSubscriptionChangeKind.Created => OperationRecordActions.WebhookSubscriptionCreated,
        WebhookSubscriptionChangeKind.Updated => OperationRecordActions.WebhookSubscriptionUpdated,
        WebhookSubscriptionChangeKind.SecretRotated => OperationRecordActions.WebhookSubscriptionSecretRotated,
        WebhookSubscriptionChangeKind.Enabled => OperationRecordActions.WebhookSubscriptionEnabled,
        WebhookSubscriptionChangeKind.Disabled => OperationRecordActions.WebhookSubscriptionDisabled,
        WebhookSubscriptionChangeKind.Deleted => OperationRecordActions.WebhookSubscriptionDeleted,
        _ => throw new ArgumentOutOfRangeException(
            nameof(change), change, "This webhook subscription change kind has no operation action code yet.")
    };
}
#endif

#if (IncludeDistributedEvents)
/// <summary>逐个已提交批次记录实际恢复数量；完整选择条件通过关联日志保留。</summary>
internal sealed class DistributedEventManagedAuditHandler(
    IOperationRecorder recorder, ILogger<DistributedEventManagedAuditHandler> logger)
    : IEventHandler<DistributedEventManagedEvent>
{
    public Task HandleAsync(DistributedEventManagedEvent @event, CancellationToken cancellationToken = default)
    {
        var action = @event.Action switch
        {
            DistributedEventManagementAction.Redelivered => OperationRecordActions.DistributedEventRedelivered,
            DistributedEventManagementAction.Replayed => OperationRecordActions.DistributedEventReplayed,
            DistributedEventManagementAction.Redecoded => OperationRecordActions.DistributedEventRedecoded,
            DistributedEventManagementAction.Discarded => OperationRecordActions.DistributedEventDiscarded,
            _ => throw new ArgumentOutOfRangeException(nameof(@event), @event.Action, "Unknown event management action.")
        };
        var selection = @event.FilterJson ?? JsonSerializer.Serialize(@event.Ids);
        logger.LogInformation("Distributed event management {Action}: {AffectedCount} records in {Database}; selector {Selection}; batch {BatchId}",
            action, @event.AffectedCount, @event.Database, selection, @event.EventId);
        return recorder.RecordSucceededAsync(action,
            OperationTarget.For(@event.EventId, Summarize(@event)),
            PermissionConstant.DistributedEvents.Manage, cancellationToken);
    }

    private static string Summarize(DistributedEventManagedEvent @event)
    {
        var state = @event.Action switch
        {
            DistributedEventManagementAction.Redelivered => "deadLettered",
            DistributedEventManagementAction.Replayed => "failed/processed",
            _ => "pending"
        };
        var fields = new List<string> { $"count={@event.AffectedCount}", $"status={state}" };
        if (@event.FilterJson is { } json)
        {
            using var document = JsonDocument.Parse(json);
            var filter = document.RootElement;
            if (@event.Action == DistributedEventManagementAction.Replayed
                && filter.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Number)
                fields[1] = "status=" + (status.GetInt32() == (int)DistributedEventInboxStatus.Failed ? "failed" : "processed");
            foreach (var key in new[] { "errorCode", "eventName", "reason", "before" })
                if (filter.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                    fields.Add($"{key}={value.GetString()}");
        }
        else fields.Add($"selected={@event.Ids?.Count ?? 0}");
        return string.Join("; ", fields);
    }
}
#endif
