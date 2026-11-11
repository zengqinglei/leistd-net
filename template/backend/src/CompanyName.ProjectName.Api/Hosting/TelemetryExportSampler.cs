using CompanyName.ProjectName.Api.Options;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

namespace CompanyName.ProjectName.Api.Hosting;

/// <summary>追踪采样：导出不活动时一律丢弃，活动时与 SDK 默认一致（跟随上游，根 span 全采）。</summary>
/// <remarks>
/// 在 span 开始时判断，丢弃的 span 不记录属性、不进批处理；SDK 对丢弃的处理是只保留传播所需的上下文，
/// 下游调用与日志里的 TraceId 照常。导出打开后新开始的 span 立即采样，不必重启。
/// </remarks>
internal sealed class TelemetryExportSampler(IOptionsMonitor<TelemetryExportOptions> options) : Sampler
{
    private static readonly Sampler Active = new ParentBasedSampler(new AlwaysOnSampler());

    /// <inheritdoc />
    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
        => options.CurrentValue.IsActive
            ? Active.ShouldSample(samplingParameters)
            : new SamplingResult(SamplingDecision.Drop);
}
