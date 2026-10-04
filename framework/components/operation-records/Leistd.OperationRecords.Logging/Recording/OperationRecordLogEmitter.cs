using Leistd.OperationRecords.Logging.Constants;
using Leistd.OperationRecords.Models;
using Microsoft.Extensions.Logging;

namespace Leistd.OperationRecords.Logging.Recording;

// 把一条已冻结的记录写成一条结构化日志。字段与数据库存储逐一对应：
// 日志模式只换去处，不换"记什么"——采集端按字段筛选取证，与审计表的独立筛选同一要求。
internal sealed class OperationRecordLogEmitter(ILoggerFactory loggerFactory)
{
    private const string MessageTemplate =
        "Operation {OperationAction} {OperationOutcome} on {OperationTargetId} ({OperationTargetName}) "
        + "by {OperationActorId} ({OperationActorName}) of tenant {OperationActorTenantId} in tenant {OperationTenantId}; "
        + "basis {OperationAuthorizationBasis}, visibility {OperationVisibility}, impersonator {OperationImpersonatorId} ({OperationImpersonatorName}), "
        + "failure {OperationFailureCode} {OperationFailureData} {OperationFailureDetail}, "
        + "at {OperationTime:O}, correlation {OperationCorrelationId}, record {OperationRecordId}";

    private readonly ILogger _logger = loggerFactory.CreateLogger(OperationRecordLogging.CategoryName);
    private readonly ILogger _deliveryLogger = loggerFactory.CreateLogger(OperationRecordLogging.DeliveryCategoryName);

    public bool IsEnabled => _logger.IsEnabled(LogLevel.Information);

    public void Emit(OperationRecordInfo record)
    {
        var succeeded = record.Outcome == OperationRecordOutcome.Succeeded;
        _logger.Log(
            succeeded ? LogLevel.Information : LogLevel.Warning,
            new EventId(succeeded ? OperationRecordLogging.SucceededEventId : OperationRecordLogging.FailedEventId, "OperationRecord"),
            MessageTemplate,
            record.Action,
            record.Outcome,
            record.TargetId,
            record.TargetName,
            record.ActorId,
            record.ActorName,
            record.ActorTenantId,
            record.TenantId,
            record.AuthorizationBasis,
            record.Visibility,
            record.ImpersonatorId,
            record.ImpersonatorName,
            record.FailureCode,
            record.FailureData,
            record.FailureDetail,
            record.CreationTime,
            record.CorrelationId,
            record.Id);
    }

    // 业务已提交之后的写出：失败不能再改变业务结果（回滚已不可能），也不能让调用方把它当成业务失败再补记一条失败记录。
    // 只报告记录标识与动作码，不重放其余字段——输出端本身就是坏的，换个类别尽力报一次。
    public void EmitCommitted(OperationRecordInfo record)
    {
        try
        {
            Emit(record);
        }
        catch (Exception exception)
        {
            try
            {
                _deliveryLogger.LogCritical(
                    new EventId(OperationRecordLogging.DeliveryFailedEventId, "OperationRecordDeliveryFailed"),
                    exception,
                    "The operation {OperationAction} was committed but its record {OperationRecordId} could not be written to the log",
                    record.Action,
                    record.Id);
            }
            catch
            {
                // 两个类别都写不出去：日志管道整体不可用，再抛只会把已提交的业务报成失败。
            }
        }
    }
}
