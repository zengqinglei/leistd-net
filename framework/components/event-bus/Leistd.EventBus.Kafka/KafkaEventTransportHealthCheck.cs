using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Leistd.EventBus.Kafka;

internal sealed class KafkaEventTransportHealthCheck(KafkaEventTransport transport) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => await Task.Run(transport.CheckHealth, cancellationToken) ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Kafka event transport is disconnected.");
}
