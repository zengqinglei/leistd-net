using CompanyName.ProjectName.Application.OperationRecords.Provider;
using CompanyName.ProjectName.Application.Permissions.Provider;
using Leistd.Authorization.Constants;
using Leistd.Authorization.Events;
using Leistd.EventBus.EventHandlers;
#if (LocalIdentity)
using Leistd.MultiTenancy.Events;
#endif
using Leistd.OperationRecords.Abstractions;
using Leistd.Settings.Definitions;
using Leistd.Settings.Events;

namespace CompanyName.ProjectName.Application.OperationRecords.EventHandlers;

/// <summary>
/// 设置变更留痕：租户层与宿主层的写入记一条，个人偏好不记。
/// </summary>
/// <remarks>
/// <para>目标标识带出作用域：同一个设置名在宿主与租户两层各有一行，只记名字会让两层的变更在审计里长得一模一样。</para>
/// <para>个人偏好刻意不记：不改变能力边界也不改变共享数据，却是高频写入——审计表的价值来自密度。</para>
/// <para>在写入提交之后执行（本地事件处理器的默认阶段），回滚了的写入不会留下"记了但没发生"的记录。</para>
/// </remarks>
internal sealed class SettingChangedAuditHandler(IOperationRecorder recorder) : IEventHandler<SettingChangedEvent>
{
    public Task HandleAsync(SettingChangedEvent @event, CancellationToken cancellationToken = default)
        => @event.Scope == SettingScopes.User
            ? Task.CompletedTask
            : recorder.RecordSucceededAsync(
                OperationRecordActions.SettingChanged,
                OperationTarget.For($"{@event.Scope}/{@event.Name}", @event.Name),
                PermissionConstant.Settings.Default,
                cancellationToken);
}

/// <summary>
/// 权限授予整体替换留痕，是整套权限体系里最敏感的写操作之一。
/// </summary>
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
#if (LocalIdentity)

/// <summary>
/// 租户生命周期留痕。事件只在各步都已提交后发布：创建失败并已补偿时不会有"创建成功"的假账。
/// </summary>
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
#endif
