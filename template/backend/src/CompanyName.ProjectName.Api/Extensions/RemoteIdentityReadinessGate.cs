#if (RemoteTokenAuth)
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CompanyName.ProjectName.Api.Extensions;

/// <summary>
/// 资源服务的一次性启动门禁：确认远端 Identity 可达之后才接流量
/// </summary>
/// <remarks>
/// <para>资源服务只有取得远端 OIDC 元数据与签名密钥后才可接收流量。</para>
/// <para>门禁成功后锁存；Identity 短暂故障不会摘除已具备缓存的实例。</para>
/// <para>确认失败时实例保持 not ready 并持续重试，liveness 仍只反映进程状态，
/// 避免外部依赖故障触发实例重启。</para>
/// </remarks>
internal sealed class RemoteIdentityReadinessGate
{
    private volatile bool _isReady;

    /// <summary>启动依赖是否已确认通过（锁存，一经置位不再回落）</summary>
    public bool IsReady => _isReady;

    /// <summary>标记启动依赖确认通过</summary>
    public void MarkReady() => _isReady = true;
}

/// <summary>
/// readiness 探针：只反映 <see cref="RemoteIdentityReadinessGate"/> 的锁存状态
/// </summary>
internal sealed class RemoteIdentityReadinessCheck(RemoteIdentityReadinessGate gate) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(gate.IsReady
            ? HealthCheckResult.Healthy("Remote identity metadata was confirmed at startup.")
            : HealthCheckResult.Unhealthy(
                "Remote identity metadata has not been confirmed yet; refusing traffic."));
}
#endif
