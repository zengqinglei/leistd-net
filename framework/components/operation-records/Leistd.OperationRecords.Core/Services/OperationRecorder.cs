using Leistd.MultiTenancy.Abstractions;
using Leistd.OperationRecords.Abstractions;
using Leistd.OperationRecords.Options;
using Leistd.Security.Users;
using Leistd.Timing;
using Leistd.Tracing.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Leistd.OperationRecords.Services;

// IOperationRecorder 的默认实现：从当前上下文补齐操作人、时间与链路标识。
//
// internal：宿主经 IOperationRecorder 使用或装饰它。公开具体类而只按接口注册，
// 注入具体类能编译通过、运行时才解析失败。
//
// 时间由本类填充，与 EfCoreTenantConnectionConfigurationManager 同型：审计属性的自动填充
// 要宿主自己把 AuditSaveChangesInterceptor 挂到目标 DbContext，漏挂是静默的，
// 得到的会是一张时间全为零的审计表——"什么时间"塌掉，整张表就没用了。
//
// 本类只决定记录"写进哪一层"（TenantId），事务边界由存储按结果执行（见 IOperationRecordStore）。
internal sealed class OperationRecorder(
    IOperationRecordStore store,
    IOperationActionDefinitionManager actionDefinitions,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    ICorrelationIdProvider correlationIdProvider,
    IClock clock,
    IOptions<OperationRecordOptions> options,
    ILogger<OperationRecorder> logger) : IOperationRecorder
{
    public Task RecordSucceededAsync(
        string action,
        OperationTarget target,
        string authorizationBasis,
        CancellationToken cancellationToken = default)
    {
        var definition = GetDefinition(action, authorizationBasis);

        // 宿主可见的成功记录只能写在宿主上下文里：租户上下文的事务连的是租户的库，
        // 记在租户层谁都看不见，挪到宿主层又脱离了业务事务，两头都不对，只能让调用方改。
        if (definition.Visibility == OperationVisibility.Host && currentTenant.Id is { } tenantId)
        {
            throw new InvalidOperationException(
                $"Operation action '{action}' is host-visible but succeeded inside tenant '{tenantId}'. "
                + "Register it as tenant-visible, or record it after switching to the host context.");
        }

        // 成功路径不吞异常：这条记录与它描述的那次变更同处一个边界，
        // 审计写不进去就该让业务一起失败——"发生了但没记"和"记了但没发生"一样不可接受。
        return store.InsertAsync(
            Create(action, target, authorizationBasis, definition.Visibility, currentTenant.Id,
                OperationRecordOutcome.Succeeded, OperationFailure.None),
            cancellationToken);
    }

    public async Task RecordFailedAsync(
        string action,
        OperationTarget target,
        string authorizationBasis,
        OperationFailure failure = default)
    {
        // 校验放在 try 之外：下面那个 catch 吞的是"写库没成功"这类运行期故障，
        // 而参数漏传、动作码未登记是调用方的编码错误，确定性地每次都发生，必须当场响而不是被吞掉。
        var definition = GetDefinition(action, authorizationBasis);

        // 宿主可见的失败记录写进宿主层：留在租户层的话租户读者按可见性看不到、宿主按租户维度查不到。
        // 失败记录本就独立提交，换一层写不牵动任何业务事务。
        var tenantId = definition.Visibility == OperationVisibility.Host ? null : currentTenant.Id;

        try
        {
            // 不可取消：被审计的一方断开连接，不能让这条审计作废。
            await store.InsertAsync(
                Create(action, target, authorizationBasis, definition.Visibility, tenantId,
                    OperationRecordOutcome.Failed, failure),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to record a rejected operation {Action} on {TargetId}; the rejection itself stands",
                action,
                target.Id);
        }
    }

    // 两个字符串由业务给值，框架只存不读，所以"忘了传"只能在这里响。
    // 空串落库之后分不出"这次操作不需要授权依据"和"调用方漏传了"，
    // 而审计表里一个分不清含义的列等于没有这一列。
    //
    // 目标不在这里校验：OperationTarget 把空白吸收成 None（标识为 "-"），
    // 空白标识在类型层面就构造不出来，校验点前移到了值对象里。
    //
    // 动作码必须已登记：记录的可见性取自定义，未登记的码没有可见性可盖——默认给租户看是泄露，
    // 默认只给宿主看又会让租户上下文里写下的记录谁都看不见。与权限未定义同一处理。
    private IOperationActionDefinition GetDefinition(string action, string authorizationBasis)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationBasis);

        return actionDefinitions.GetOrNull(action)
            ?? throw new InvalidOperationException(
                $"Operation action '{action}' is not registered. "
                + "Register it through an IOperationActionDefinitionProvider.");
    }

    // 从当前上下文补齐四问的答案。超长字段在存储侧被截断（列有长度上限），这里提前预警：
    // 截断是静默的，而一条被截断的目标标识既检索不到成功路径写下的那条，又看起来像个真实存在的目标。
    private OperationRecordInfo Create(
        string action,
        OperationTarget target,
        string authorizationBasis,
        OperationVisibility visibility,
        Guid? tenantId,
        OperationRecordOutcome outcome,
        OperationFailure failure)
    {
        var actorName = currentUser.Name ?? currentUser.Username;
        var correlationId = correlationIdProvider.Get();
        var claimTypes = options.Value;

        // 读 claim 原始值而不是 ICurrentUser.Id：后者只在 sub 能解析成 GUID 时有值，
        // 而机器主体（client:<client_id>）与后台作业主体都不是 GUID——只认 Id 会把这两类
        // 操作全部记成无主的，而"什么人"正是这张表的第一问。回落到 Id 覆盖没有 sub claim 的宿主。
        var actorId = currentUser.FindClaim(claimTypes.ActorIdClaimType)?.Value
                      ?? currentUser.Id?.ToString();
        var impersonatorId = currentUser.FindClaim(claimTypes.ImpersonatorIdClaimType)?.Value;
        var impersonatorName = currentUser.FindClaim(claimTypes.ImpersonatorNameClaimType)?.Value;

        WarnIfWouldTruncate(nameof(action), action, OperationRecordInfo.MaxActionLength);
        WarnIfWouldTruncate("targetId", target.Id, OperationRecordInfo.MaxTargetIdLength);
        WarnIfWouldTruncate("targetName", target.Name, OperationRecordInfo.MaxTargetNameLength);
        WarnIfWouldTruncate(nameof(authorizationBasis), authorizationBasis, OperationRecordInfo.MaxAuthorizationBasisLength);
        WarnIfWouldTruncate("failureCode", failure.Code, OperationRecordInfo.MaxFailureCodeLength);
        WarnIfWouldTruncate("failureDetail", failure.Detail, OperationRecordInfo.MaxFailureDetailLength);
        WarnIfWouldTruncate(nameof(actorId), actorId, OperationRecordInfo.MaxActorIdLength);
        WarnIfWouldTruncate(nameof(actorName), actorName, OperationRecordInfo.MaxActorNameLength);
        WarnIfWouldTruncate(nameof(correlationId), correlationId, OperationRecordInfo.MaxCorrelationIdLength);

        return new OperationRecordInfo
        {
            TenantId = tenantId,
            ActorTenantId = currentTenant.Id,
            Action = action,
            TargetId = target.Id,
            TargetName = target.Name,
            AuthorizationBasis = authorizationBasis,
            Outcome = outcome,
            Visibility = visibility,
            FailureCode = failure.Code,
            FailureData = failure.Data,
            FailureDetail = failure.Detail,
            CreationTime = clock.Normalize(clock.Now),
            ActorId = actorId,
            ActorName = actorName,
            ImpersonatorId = impersonatorId,
            ImpersonatorName = impersonatorName,
            CorrelationId = correlationId
        };
    }

    private void WarnIfWouldTruncate(string fieldName, string? value, int maxLength)
    {
        if (value is not null && value.Trim().Length > maxLength)
        {
            logger.LogWarning(
                "Operation record field {Field} is {ActualLength} characters, exceeding the {MaxLength} limit; "
                + "it will be truncated and the audit trail for this event will not be fully accurate.",
                fieldName,
                value.Trim().Length,
                maxLength);
        }
    }
}
