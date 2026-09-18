using Leistd.MultiTenancy.Abstractions;
using Leistd.OperationRecords.Abstractions;
using Leistd.OperationRecords.Options;
using Leistd.Security.Users;
using Leistd.Timing;
using Leistd.Tracing.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Leistd.OperationRecords.Services;

/// <summary>
/// <see cref="IOperationRecorder"/> 的默认实现：从当前上下文补齐操作人、时间与链路标识。
/// </summary>
/// <remarks>
/// <para><b>时间由本类填充</b>，与 <c>EfCoreTenantConnectionConfigurationManager</c> 同型：
/// 审计属性的自动填充要宿主自己把 <c>AuditSaveChangesInterceptor</c> 挂到目标 DbContext，
/// 漏挂是静默的，得到的会是一张时间全为零的审计表——"什么时间"塌掉，整张表就没用了。</para>
/// <para><b>本类不碰事务。</b>写入落在调用方所处的边界里，由存储原样提交或跟随回滚。
/// "这条记录要不要扛过外层回滚"只有宿主清楚它的锁分布；组件替它开第二个事务，
/// 会与外层未提交的写入互相加锁——那正是工作单元组件对 <c>requiresNew</c> 的告诫。</para>
/// </remarks>
/// <param name="store">持久化存储。</param>
/// <param name="actionDefinitions">动作定义索引，写入时据它给记录盖上可见性。</param>
/// <param name="currentTenant">当前租户上下文。</param>
/// <param name="currentUser">当前用户，操作人信息取自它。</param>
/// <param name="correlationIdProvider">链路标识提供器。</param>
/// <param name="clock">时钟。</param>
/// <param name="options">配置，提供识别真实操作人的 claim 类型。</param>
/// <param name="logger">日志。</param>
public class OperationRecorder(
    IOperationRecordStore store,
    IOperationActionDefinitionManager actionDefinitions,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    ICorrelationIdProvider correlationIdProvider,
    IClock clock,
    IOptions<OperationRecordOptions> options,
    ILogger<OperationRecorder> logger) : IOperationRecorder
{
    /// <inheritdoc />
    public Task RecordSucceededAsync(
        string action,
        OperationTarget target,
        string authorizationBasis,
        CancellationToken cancellationToken = default)
    {
        Validate(action, authorizationBasis);

        // 成功路径不吞异常：这条记录与它描述的那次变更同处一个边界，
        // 审计写不进去就该让业务一起失败——"发生了但没记"和"记了但没发生"一样不可接受。
        return store.InsertAsync(
            Create(action, target, authorizationBasis, OperationRecordOutcome.Succeeded, OperationFailure.None),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task RecordFailedAsync(
        string action,
        OperationTarget target,
        string authorizationBasis,
        OperationFailure failure = default,
        CancellationToken cancellationToken = default)
    {
        // 校验放在 try 之外：下面那个 catch 吞的是"写库没成功"这类运行期故障，
        // 而参数漏传是调用方的编码错误，确定性地每次都发生，必须当场响而不是被吞掉。
        Validate(action, authorizationBasis);

        try
        {
            await store.InsertAsync(
                Create(action, target, authorizationBasis, OperationRecordOutcome.Failed, failure),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
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
    private static void Validate(string action, string authorizationBasis)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationBasis);
    }

    // 从当前上下文补齐四问的答案。超长字段在存储侧被截断（列有长度上限），这里提前预警：
    // 截断是静默的，而一条被截断的目标标识既检索不到成功路径写下的那条，又看起来像个真实存在的目标。
    private OperationRecordInfo Create(
        string action,
        OperationTarget target,
        string authorizationBasis,
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
            TenantId = currentTenant.Id,
            Action = action,
            TargetId = target.Id,
            TargetName = target.Name,
            AuthorizationBasis = authorizationBasis,
            Outcome = outcome,
            // 未登记的动作码盖最严格的一档（Host），不是盖"租户可见"：
            // 这张表由租户管理员直接阅读，未知来源的记录默认可见给租户就是默认泄露。
            // 反过来最坏只是租户暂时看不到某些记录，补登记即可修复。
            Visibility = actionDefinitions.GetOrNull(action)?.Visibility ?? OperationVisibility.Host,
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
