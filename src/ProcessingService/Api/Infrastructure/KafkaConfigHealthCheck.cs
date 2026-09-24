using Microsoft.Extensions.Diagnostics.HealthChecks;
using ProcessingService.Application.Kafka;

namespace ProcessingService.Api.Infrastructure;

/// <summary>
/// Health check for Kafka producer wiring (SCRUM-68 review point 1: a "Kafka not configured, keep running" fallback must report degraded).
/// Reports Degraded when the NoOp producer is registered (Kafka:BootstrapServers missing) - service keeps running,
/// outbox rows stay Pending (never marked sent), events publish once Kafka is configured.
/// </summary>
public sealed class KafkaConfigHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;

    public KafkaConfigHealthCheck(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IKafkaProducer>();

            if (producer is NoOpKafkaProducer)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    "Kafka not configured (Kafka:BootstrapServers missing) - events stay PENDING in outbox_messages and publish once Kafka is configured"));
            }

            return Task.FromResult(HealthCheckResult.Healthy("Kafka producer configured"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Kafka producer check failed", ex));
        }
    }
}
