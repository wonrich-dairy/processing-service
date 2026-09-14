namespace ProcessingService.Api.Infrastructure.Observability;

/// <summary>
/// DelegatingHandler that propagates X-Correlation-ID to downstream HTTP calls (SCRUM-90).
/// Used when Processing calls other services (MCC, Auth) - correlation traced across 2 services.
/// </summary>
public sealed class CorrelationIdHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationIdHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context != null && context.Items.TryGetValue(CorrelationIdMiddleware.HeaderName, out var correlationId) && correlationId is string id)
        {
            request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, id);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
