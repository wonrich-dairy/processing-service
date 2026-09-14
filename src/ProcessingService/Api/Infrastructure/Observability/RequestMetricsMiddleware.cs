using System.Diagnostics;

namespace ProcessingService.Api.Infrastructure.Observability;

/// <summary>
/// Records request count/duration/error per endpoint for Prometheus (SCRUM-90).
/// </summary>
public sealed class RequestMetricsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ProcessingMetrics _metrics;
    private readonly ILogger<RequestMetricsMiddleware> _logger;

    public RequestMetricsMiddleware(RequestDelegate next, ProcessingMetrics metrics, ILogger<RequestMetricsMiddleware> logger)
    {
        _next = next;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var endpoint = context.GetEndpoint()?.DisplayName ?? context.Request.Path.Value ?? "unknown";
        var method = context.Request.Method;

        try
        {
            await _next(context);

            var status = context.Response.StatusCode;
            var tags = new TagList { { "method", method }, { "endpoint", endpoint }, { "status", status.ToString() } };

            _metrics.RequestsTotal.Add(1, tags);
            _metrics.RequestDurationSeconds.Record(sw.Elapsed.TotalSeconds, tags);

            if (status >= 400)
            {
                _metrics.RequestErrorsTotal.Add(1, tags);
            }

            // Structured log per request - JSON with correlation ID, no PII/connection strings
            _logger.LogInformation("HTTP {Method} {Endpoint} {StatusCode} {DurationMs}ms CorrelationId={CorrelationId}",
                method, endpoint, status, sw.ElapsedMilliseconds, context.Items[CorrelationIdMiddleware.HeaderName]);
        }
        catch (Exception ex)
        {
            var tags = new TagList { { "method", method }, { "endpoint", endpoint }, { "status", "500" } };
            _metrics.RequestsTotal.Add(1, tags);
            _metrics.RequestErrorsTotal.Add(1, tags);
            _metrics.RequestDurationSeconds.Record(sw.Elapsed.TotalSeconds, tags);

            _logger.LogError(ex, "HTTP {Method} {Endpoint} failed after {DurationMs}ms CorrelationId={CorrelationId}",
                method, endpoint, sw.ElapsedMilliseconds, context.Items[CorrelationIdMiddleware.HeaderName]);
            throw;
        }
    }
}

public static class RequestMetricsMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestMetrics(this IApplicationBuilder app) => app.UseMiddleware<RequestMetricsMiddleware>();
}
