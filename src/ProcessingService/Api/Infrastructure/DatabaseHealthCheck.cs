using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ProcessingService.Infrastructure.Persistence;

namespace ProcessingService.Api.Infrastructure;

/// <summary>
/// Reports whether the service can actually reach its database (SCRUM-56).
/// </summary>
/// <remarks>
/// A health check that only says the process is running answers the wrong question: the container
/// is up long before it can serve a request that touches data. This opens a connection, so a
/// missing password or a firewall rule shows as unhealthy rather than as failures later.
/// </remarks>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly ProcessingDbContext _dbContext;

    public DatabaseHealthCheck(ProcessingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("The database is reachable.")
                : HealthCheckResult.Unhealthy("The database refused the connection.");
        }
        catch (Exception exception)
        {
            // The reason is logged and reported, but never the connection string it came from.
            return HealthCheckResult.Unhealthy("The database could not be reached.", exception);
        }
    }
}
