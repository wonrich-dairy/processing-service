using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ProcessingService.Infrastructure.Persistence;

public sealed class ProcessingDbContextFactory : IDesignTimeDbContextFactory<ProcessingDbContext>
{
    private const string DesignTimeConnectionString =
        "Server=your-remote-mysql-host;Port=3306;Database=processing;User Id=processing_user;Password=your-secure-password";

    public ProcessingDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? DesignTimeConnectionString;

        var serverVersion = new MySqlServerVersion(new Version(8, 4, 0));

        var options = new DbContextOptionsBuilder<ProcessingDbContext>()
            .UseMySql(connectionString, serverVersion)
            .Options;

        return new ProcessingDbContext(options);
    }
}