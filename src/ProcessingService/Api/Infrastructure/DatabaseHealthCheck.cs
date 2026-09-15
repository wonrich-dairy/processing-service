using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ProcessingService.Infrastructure.Persistence;

namespace ProcessingService.Api.Infrastructure;

/// <summary>
/// Health check that reports DB connectivity (SCRUM-56: /health returns 200 + DB healthy) and
/// whether the schema matches this build (SCRUM-72).
/// </summary>
/// <remarks>
/// Connectivity alone is not enough to call the service healthy. Production runs with
/// <c>ASPNETCORE_ENVIRONMENT=Production</c>, where <c>Program.cs</c> deliberately does not apply
/// migrations on startup, so a freshly provisioned environment can open a connection to a database
/// that has none of this build's tables in it. Reporting that as healthy would let a deployment
/// gate pass over a schema that cannot serve a single request.
///
/// Pending migrations are reported as Degraded rather than Unhealthy: the process is alive and the
/// database is reachable, so restarting it would not help — someone has to apply the migrations.
/// Degraded still returns HTTP 200, so a container probe does not begin a restart loop, while the
/// body says <c>"status":"Degraded"</c> and the deployment check in CI looks for
/// <c>"status":"Healthy"</c>.
/// </remarks>
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

            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("Database not reachable");
            }

            // Only a relational provider tracks migrations; the tests run on InMemory.
            if (!db.Database.IsRelational())
            {
                return HealthCheckResult.Healthy("Database reachable");
            }

            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
            if (pending.Length > 0)
            {
                return HealthCheckResult.Degraded(
                    $"Database reachable, but {pending.Length} migration(s) have not been applied: {string.Join(", ", pending)}");
            }

            return HealthCheckResult.Healthy("Database reachable and schema up to date");
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
