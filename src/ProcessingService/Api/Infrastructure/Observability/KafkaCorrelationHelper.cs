namespace ProcessingService.Api.Infrastructure.Observability;

/// <summary>
/// Helper to propagate correlation ID via Kafka headers (SCRUM-90, Kafka deferred per standing instruction).
/// When Kafka is re-enabled, use this to set header on produced messages and read on consumed.
/// </summary>
public static class KafkaCorrelationHelper
{
    public const string KafkaHeaderName = "x-correlation-id";

    public static void SetCorrelationId(IDictionary<string, byte[]> headers, string correlationId)
    {
        headers[KafkaHeaderName] = System.Text.Encoding.UTF8.GetBytes(correlationId);
    }

    public static string? GetCorrelationId(IDictionary<string, byte[]> headers)
    {
        if (headers.TryGetValue(KafkaHeaderName, out var bytes))
        {
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        return null;
    }

    public static string? GetCurrentCorrelationId(HttpContext? context)
    {
        if (context?.Items.TryGetValue(CorrelationIdMiddleware.HeaderName, out var id) == true && id is string correlationId)
        {
            return correlationId;
        }
        return null;
    }
}
