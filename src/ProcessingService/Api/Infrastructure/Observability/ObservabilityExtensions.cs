using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using System.Text.Json;

namespace ProcessingService.Api.Infrastructure.Observability;

/// <summary>
/// Shared observability setup for Wonrich services (SCRUM-90).
/// Designed to be moved to the shared service template (Wonrich.ServiceTemplate) so later services
/// inherit it rather than repeating the work. For now lives in ProcessingService and can be copied.
/// 
/// What it does:
/// - Structured JSON logging with correlation ID in every line (Loki queryable)
/// - Log level configurable per environment via appsettings Logging:LogLevel
/// - No PII or connection strings written to logs (enforced by never logging config values, only keys)
/// - Prometheus /metrics endpoint with:
///   * Request count, duration, error count per endpoint (via AspNetCore instrumentation)
///   * Runtime metrics (GC, threadpool, etc.)
///   * Custom business metrics (allocations, stages, holds, etc. via ProcessingMetrics)
/// - Correlation ID carried across HTTP (via CorrelationIdHandler) and Kafka (via KafkaCorrelationHelper)
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>
    /// Call from Program.cs as builder.AddProcessingObservability().
    /// Must be called early, before AddControllers etc., so logging is configured before anything else logs.
    /// </summary>
    public static WebApplicationBuilder AddProcessingObservability(this WebApplicationBuilder builder)
    {
        var serviceName = builder.Configuration["Observability:ServiceName"] ?? "processing-service";
        var serviceVersion = typeof(ObservabilityExtensions).Assembly.GetName().Version?.ToString() ?? "1.0.0";

        // 1. Logging: structured JSON to console (Loki scrapes console). IncludeScopes true so CorrelationId scope becomes a field.
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
            options.JsonWriterOptions = new JsonWriterOptions
            {
                Indented = false
            };
        });

        // Also keep simple console in Development for human readability if needed - JSON is still primary for Loki.
        // The JSON formatter is what satisfies "Logs emitted as structured JSON" in AC.
        // Log level is controlled by appsettings Logging:LogLevel per environment (AC: configurable per env).

        // 2. HttpContextAccessor needed for CorrelationIdHandler to read current correlation ID
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTransient<CorrelationIdHandler>();

        // 3. OpenTelemetry Metrics with Prometheus exporter
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = builder.Environment.EnvironmentName.ToLowerInvariant()
                }))
            .WithMetrics(metrics =>
            {
                metrics
                    // Built-in ASP.NET Core metrics: http.server.request.duration, http.server.active_requests, etc.
                    // These give us request count, duration and error count per endpoint automatically.
                    .AddAspNetCoreInstrumentation()
                    // Outgoing HTTP calls (to Auth, MCC, etc.) - duration, count
                    .AddHttpClientInstrumentation()
                    // Runtime: GC, CPU, memory, threadpool
                    .AddRuntimeInstrumentation()
                    // Our custom business meter
                    .AddMeter(ProcessingMetrics.MeterName)
                    // Also include standard hosting and Kestrel meters for request count
                    .AddMeter("Microsoft.AspNetCore.Hosting")
                    .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
                    // Prometheus exporter - exposes /metrics in Prometheus format
                    .AddPrometheusExporter();
            });

        return builder;
    }

    /// <summary>
    /// Must be called early in the pipeline, before auth and before any logging, so every log line
    /// carries the correlation ID and every response returns it.
    /// </summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CorrelationIdMiddleware>();
    }

    /// <summary>
    /// Helper to attach CorrelationIdHandler to HttpClients that call other Wonrich services.
    /// Usage: builder.Services.AddHttpClient("mcc", c => c.BaseAddress = ...).AddHttpMessageHandler of CorrelationIdHandler.
    /// This ensures the correlation ID is traced across Processing and one other service (DOD).
    /// </summary>
    public static IHttpClientBuilder WithCorrelationId(this IHttpClientBuilder builder)
    {
        return builder.AddHttpMessageHandler<CorrelationIdHandler>();
    }
}
