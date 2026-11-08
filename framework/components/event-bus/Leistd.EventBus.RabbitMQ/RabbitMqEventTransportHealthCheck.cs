using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Leistd.EventBus.RabbitMQ;

internal sealed class RabbitMqEventTransportHealthCheck(RabbitMqEventTransport transport) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(transport.IsHealthy ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("RabbitMQ event transport is disconnected."));
}
