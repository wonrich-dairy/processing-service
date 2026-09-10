using System.Diagnostics;

namespace ProcessingService.Api.Infrastructure.Observability;

/// <summary>
/// Carries a correlation ID across a single request and onwards to downstream calls and logs (SCRUM-90).
/// The ID is read from X-Correlation-ID / X-Request-ID if the caller supplied one (so a consignment
/// can be followed end-to-end across MCC -> Processing -> other services), otherwise a new one is minted.
/// It is added to the response, to Activity baggage/tags for OpenTelemetry, and to the logger scope
/// so every structured JSON log line contains it for Loki.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    private const string TraceIdItemKey = "CorrelationId";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetOrCreateCorrelationId(context);

        // Make available to the rest of the pipeline (handlers, services, controllers)
        context.Items[TraceIdItemKey] = correlationId;
        context.TraceIdentifier = correlationId; // replaces the default random trace identifier

        // For OpenTelemetry - so metrics/traces can be correlated
        Activity.Current?.SetTag("correlation.id", correlationId);
        Activity.Current?.SetBaggage("correlation-id", correlationId);

        // Response header so caller can continue the chain
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(HeaderName))
            {
                context.Response.Headers[HeaderName] = correlationId;
            }
            return Task.CompletedTask;
        });

        // Logger scope - with JSON console formatter, this becomes a field in every log line (Loki queryable)
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["TraceId"] = correlationId
        }))
        {
            await _next(context);
        }
    }

    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        // Check common header names in order of preference
        if (context.Request.Headers.TryGetValue(HeaderName, out var headerValue) && !string.IsNullOrWhiteSpace(headerValue))
        {
            return headerValue.ToString().Trim();
        }

        if (context.Request.Headers.TryGetValue("X-Request-ID", out var requestId) && !string.IsNullOrWhiteSpace(requestId))
        {
            return requestId.ToString().Trim();
        }

        if (context.Request.Headers.TryGetValue("Correlation-ID", out var alt) && !string.IsNullOrWhiteSpace(alt))
        {
            return alt.ToString().Trim();
        }

        // No ID supplied - mint one. Guid is safe, no PII, sortable enough for tracing.
        return Guid.NewGuid().ToString("N");
    }

    /// <summary>Helper to retrieve the correlation ID from anywhere that has HttpContext.</summary>
    public static string? GetFromContext(HttpContext? context) =>
        context?.Items[TraceIdItemKey] as string
        ?? context?.Request.Headers[HeaderName].ToString()
        ?? Activity.Current?.GetBaggageItem("correlation-id")
        ?? Activity.Current?.GetTagItem("correlation.id") as string;

    /// <summary>Helper for non-HTTP paths (e.g. background workers) that have an Activity.</summary>
    public static string GetOrCreateFromActivity() =>
        Activity.Current?.GetBaggageItem("correlation-id")
        ?? Activity.Current?.GetTagItem("correlation.id") as string
        ?? Guid.NewGuid().ToString("N");
}
