using System.Diagnostics;
using Leistd.AmbientContext;
using Leistd.Tracing.Abstractions;

namespace Leistd.Tracing.AmbientContext;

// 非 HTTP 入口的链路标识维度。
//
// 优先级与入站中间件同口径：有 Activity 就以它的 TraceId 为准，
// 否则用调用方指定值，再否则新建一个 W3C 形态的标识。
// 反过来（先用指定值）会让同一次调用在 APM 与日志里出现两个标识。
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
