using Prometheus;

namespace ProcessingService.Api.Infrastructure.Observability;

/// <summary>
/// Observability wiring for Processing Service (SCRUM-90).
/// - JSON logs with correlation ID across HTTP+Kafka
/// - Log level per env (Information in Dev, Warning in Prod via appsettings)
/// - Prometheus /metrics endpoint
/// - Request count/duration/error per endpoint
/// - Custom metrics: allocations, stages, holds
/// - Outbox backlog gauge (Pending/Poisoned) for Grafana, SCRUM-111 (review fix #11)
/// - No PII/connection strings in logs
/// </summary>
public static class ObservabilityExtensions
{
    public static IServiceCollection AddProcessingObservability(this IServiceCollection services, IConfiguration configuration)
    {
        // Metrics
        services.AddMetrics();
        services.AddSingleton<ProcessingMetrics>();
        // Copies what ProcessingMetrics records into prometheus-net so /metrics reports real values
        services.AddHostedService<PrometheusMeterBridge>();

        // HttpContextAccessor needed for CorrelationIdHandler
        services.AddHttpContextAccessor();
        services.AddTransient<CorrelationIdHandler>();

        // Logging: JSON console in Production, simple in Development (configured via appsettings.json Logging section)
        // Log level per env: appsettings.json Logging:LogLevel:Default = Information, Production overrides to Warning
        services.AddLogging(logging =>
        {
            // Ensure no PII: filter out ConnectionStrings and Auth:SigningKey from logs
            logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Connection", LogLevel.Warning);
            logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
        });

        return services;
    }

    public static IApplicationBuilder UseProcessingObservability(this IApplicationBuilder app)
    {
        app.UseCorrelationId();
        app.UseRequestMetrics();
        return app;
    }

    public static IEndpointRouteBuilder MapProcessingMetrics(this IEndpointRouteBuilder endpoints)
    {
        // Prometheus exposition format, anonymous so Prometheus needs no token (SCRUM-90 AC).
        // The values come from PrometheusMeterBridge, which copies what the service records on its meter.
        endpoints.MapMetrics("/metrics").AllowAnonymous();
        return endpoints;
    }
}
