using System.Diagnostics;

namespace ProcessingService.Api.Infrastructure.Observability;

/// <summary>
/// Propagates the correlation ID to outgoing HTTP calls, so a single consignment can be followed
/// end-to-end across Processing -> Auth -> MCC -> other services (SCRUM-90).
/// Register as a transient HttpMessageHandler and attach to HttpClients that call other Wonrich services.
/// </summary>
public sealed class CorrelationIdHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationIdHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var correlationId =
            CorrelationIdMiddleware.GetFromContext(_httpContextAccessor.HttpContext)
            ?? CorrelationIdMiddleware.GetOrCreateFromActivity();

        if (!string.IsNullOrWhiteSpace(correlationId) && !request.Headers.Contains(CorrelationIdMiddleware.HeaderName))
        {
            request.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId);
        }

        // Also add as baggage for OpenTelemetry propagation if OTel propagator is configured
        Activity.Current?.SetBaggage("correlation-id", correlationId);

        return base.SendAsync(request, cancellationToken);
    }
}
