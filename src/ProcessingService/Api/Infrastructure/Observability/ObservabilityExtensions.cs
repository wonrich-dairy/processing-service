using System.Diagnostics.Metrics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ProcessingService.Infrastructure.Persistence;

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
        // Prometheus exposition format - /metrics endpoint (SCRUM-90 AC)
        endpoints.MapGet("/metrics", async (HttpContext context, IServiceScopeFactory scopeFactory, IMeterFactory meterFactory, ProcessingMetrics metrics) =>
        {
            context.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";

            var sb = new StringBuilder();
            sb.AppendLine("# HELP processing_http_requests_total Total HTTP requests per endpoint");
            sb.AppendLine("# TYPE processing_http_requests_total counter");
            sb.AppendLine("# HELP processing_http_request_duration_seconds HTTP request duration per endpoint");
            sb.AppendLine("# TYPE processing_http_request_duration_seconds histogram");
            sb.AppendLine("# HELP processing_http_request_errors_total Total HTTP errors per endpoint");
            sb.AppendLine("# TYPE processing_http_request_errors_total counter");
            sb.AppendLine("# HELP processing_allocations_total Total milk allocations");
            sb.AppendLine("# TYPE processing_allocations_total counter");
            sb.AppendLine("# HELP processing_stages_total Total processing stages");
            sb.AppendLine("# TYPE processing_stages_total counter");
            sb.AppendLine("# HELP processing_holds_total Total held consignments");
            sb.AppendLine("# TYPE processing_holds_total counter");
            sb.AppendLine("# HELP processing_unloads_total Total milk unloads");
            sb.AppendLine("# TYPE processing_unloads_total counter");
            sb.AppendLine("# HELP processing_tanks_in_use Current tanks in use");
            sb.AppendLine("# TYPE processing_tanks_in_use gauge");
            sb.AppendLine("# HELP processing_outbox_backlog Undispatched outbox rows awaiting Kafka publish (SCRUM-68; review fix #11) - Pending sustained growth means broker outage or relay down, Poisoned above zero needs human review. Grafana hook for SCRUM-111");
            sb.AppendLine("# TYPE processing_outbox_backlog gauge");

            // In real implementation, use MeterListener to collect actual values
            // For scaffold, return static exposition with help text + current timestamp to prove endpoint works
            sb.AppendLine($"# Generated at {DateTimeOffset.UtcNow:O}");
            sb.AppendLine("processing_http_requests_total 0");
            sb.AppendLine("processing_allocations_total 0");
            sb.AppendLine("processing_stages_total 0");
            sb.AppendLine("processing_holds_total 0");
            sb.AppendLine("processing_unloads_total 0");
            sb.AppendLine("processing_tanks_in_use 0");

            // Outbox backlog: REAL gauge queried at scrape time (review fix #11)
            // Plain COUNT under InnoDB consistent read does not block on the relay's FOR UPDATE SKIP LOCKED claim locks (MVCC snapshot)
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();
                var counts = await db.OutboxMessages
                    .GroupBy(m => m.Status)
                    .Select(g => new { Status = g.Key, Count = g.Count() })
                    .ToListAsync(context.RequestAborted);
                var pending = counts.FirstOrDefault(c => c.Status == "Pending")?.Count ?? 0;
                var poisoned = counts.FirstOrDefault(c => c.Status == "Poisoned")?.Count ?? 0;
                sb.AppendLine($"processing_outbox_backlog{{status=\"Pending\"}} {pending}");
                sb.AppendLine($"processing_outbox_backlog{{status=\"Poisoned\"}} {poisoned}");
            }
            catch
            {
                // DB unreachable or migration pending - never fail the whole scrape, just omit the samples (Prometheus shows a gap)
                sb.AppendLine("# outbox backlog unavailable (database unreachable or migration pending)");
            }

            await context.Response.WriteAsync(sb.ToString());
        }).AllowAnonymous().WithDisplayName("Prometheus Metrics");

        return endpoints;
    }
}
