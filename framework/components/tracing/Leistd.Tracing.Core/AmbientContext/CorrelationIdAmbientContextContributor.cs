using System.Diagnostics;
using Leistd.AmbientContext;
using Leistd.Tracing.Abstractions;

namespace Leistd.Tracing.AmbientContext;

// 非 HTTP 入口的链路标识维度。
//
// 优先复用 Activity.TraceId，再取指定值或生成新值，保持日志与 APM 标识一致。
internal sealed class CorrelationIdAmbientContextContributor(
    ICorrelationIdProvider correlationIdProvider) : IAmbientContextContributor
{
    /// <inheritdoc />
    public IDisposable? Enter(AmbientContextEnterContext context)
    {
        var correlationId =
            Activity.Current?.TraceId.ToHexString()
            ?? context.CorrelationId
            ?? correlationIdProvider.Create();

        return correlationIdProvider.Change(correlationId);
    }
}
