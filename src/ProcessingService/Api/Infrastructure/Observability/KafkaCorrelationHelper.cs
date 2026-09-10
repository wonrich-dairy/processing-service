namespace ProcessingService.Api.Infrastructure.Observability;

/// <summary>
/// Carries correlation ID across Kafka messages so a single consignment can be followed end-to-end
/// across service calls AND Kafka messages (SCRUM-90).
/// Kafka is deferred (SCRUM-58/68), but the helper is here so when producers/consumers land,
/// the ID propagation is already proven and no log line needs to be revisited.
/// Supports both generic dictionary headers and Confluent.Kafka.Headers via overloads that work
/// with byte[] values to avoid taking a hard dependency on Confluent.Kafka in this commit.
/// </summary>
public static class KafkaCorrelationHelper
{
    public const string HeaderKey = "x-correlation-id";

    /// <summary>Inject correlation ID into outgoing Kafka message headers (dictionary form).</summary>
    public static void Inject(IDictionary<string, byte[]> headers, string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId)) return;

        headers[HeaderKey] = System.Text.Encoding.UTF8.GetBytes(correlationId);
        headers[CorrelationIdMiddleware.HeaderName] = System.Text.Encoding.UTF8.GetBytes(correlationId);
    }

    /// <summary>Inject using string dictionary (for test doubles / in-memory bus).</summary>
    public static void Inject(IDictionary<string, string> headers, string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId)) return;

        headers[HeaderKey] = correlationId;
        headers[CorrelationIdMiddleware.HeaderName] = correlationId;
    }

    /// <summary>Extract correlation ID from incoming Kafka message headers (dictionary form).</summary>
    public static string? Extract(IDictionary<string, byte[]> headers)
    {
        if (headers.TryGetValue(HeaderKey, out var value) && value is { Length: > 0 })
        {
            return System.Text.Encoding.UTF8.GetString(value);
        }

        if (headers.TryGetValue(CorrelationIdMiddleware.HeaderName, out var alt) && alt is { Length: > 0 })
        {
            return System.Text.Encoding.UTF8.GetString(alt);
        }

        return null;
    }

    /// <summary>Extract from string dictionary.</summary>
    public static string? Extract(IDictionary<string, string> headers)
    {
        if (headers.TryGetValue(HeaderKey, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (headers.TryGetValue(CorrelationIdMiddleware.HeaderName, out var alt) && !string.IsNullOrWhiteSpace(alt))
        {
            return alt;
        }

        return null;
    }

    /// <summary>Resolve the ID to use for an outgoing message from current context.</summary>
    public static string ResolveFromCurrentContext(HttpContext? httpContext = null)
    {
        return CorrelationIdMiddleware.GetFromContext(httpContext)
               ?? CorrelationIdMiddleware.GetOrCreateFromActivity();
    }
}
