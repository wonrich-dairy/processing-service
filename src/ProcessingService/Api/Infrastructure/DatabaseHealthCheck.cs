using Microsoft.Extensions.Diagnostics.HealthChecks;
using ProcessingService.Infrastructure.Persistence;

namespace ProcessingService.Api.Infrastructure;

/// <summary>
/// Health check that reports DB connectivity (SCRUM-56: /health returns 200 + DB healthy).
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;

    public DatabaseHealthCheck(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Database reachable")
                : HealthCheckResult.Unhealthy("Database not reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database check failed", ex);
        }
    }
}

public sealed class DomainExceptionHandler : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
{
    private readonly ILogger<DomainExceptionHandler> _logger;

    public DomainExceptionHandler(ILogger<DomainExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // For scaffold, treat all exceptions as 500 except we log. Real domain exceptions handled in later sprints.
        _logger.LogError(exception, "Unhandled exception");
        return false;
    }
}

public sealed class FactoryOptions
{
    public const string SectionName = "Factory";
    public string TimeZoneId { get; set; } = "Asia/Colombo";
}

public interface IFactoryClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class FactoryClock : IFactoryClock
{
    private readonly TimeProvider _timeProvider;
    public FactoryClock(TimeProvider timeProvider) => _timeProvider = timeProvider;
    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();
}
