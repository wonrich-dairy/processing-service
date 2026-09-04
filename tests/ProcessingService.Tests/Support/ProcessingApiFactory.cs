using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using ProcessingService.Infrastructure.Persistence;

namespace ProcessingService.Tests.Support;

/// <summary>
/// Hosts the real application pipeline — routing, authentication, the health endpoint — over
/// SQLite, so the HTTP contract can be exercised without a MySQL server.
/// </summary>
internal sealed class ProcessingApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// The host requires a connection string to start, so supply one it can parse. Nothing ever
    /// connects with it: the MySQL registration is replaced with SQLite below.
    /// </summary>
    private const string PlaceholderConnectionString =
        "Server=localhost;Port=3308;Database=processing_tests;User Id=tests;Password=tests";

    public const string SigningKey = "wonrich-processing-test-signing-key-0123456789";

    public const string Issuer = "wonrich-auth-tests";

    public const string Audience = "wonrich-services-tests";

    /// <summary>The one browser origin the hosted service is configured to accept.</summary>
    public const string AllowedTestOrigin = "http://localhost:5173";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    /// <summary>
    /// When false the context is pointed at a database that genuinely cannot be opened. Merely
    /// closing the in-memory connection is not enough — EF reopens it, and SQLite obligingly
    /// creates a fresh empty database, which reports healthy.
    /// </summary>
    public bool DatabaseReachable { get; init; } = true;

    private const string UnreachableConnectionString =
        @"Data Source=C:\wonrich-no-such-directory\missing.db;Mode=ReadWrite";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("ConnectionStrings:DefaultConnection", PlaceholderConnectionString);
        builder.UseSetting("Auth:SigningKey", SigningKey);
        builder.UseSetting("Auth:Issuer", Issuer);
        builder.UseSetting("Auth:Audience", Audience);
        builder.UseSetting("Cors:AllowedOrigins:0", AllowedTestOrigin);

        builder.ConfigureServices(services =>
        {
            RemoveDbContextRegistration(services);

            if (!DatabaseReachable)
            {
                services.AddDbContext<ProcessingDbContext>(
                    options => options.UseSqlite(UnreachableConnectionString));

                return;
            }

            _connection.Open();
            services.AddDbContext<ProcessingDbContext>(options => options.UseSqlite(_connection));

            using var scope = services.BuildServiceProvider().CreateScope();
            scope.ServiceProvider.GetRequiredService<ProcessingDbContext>().Database.EnsureCreated();
        });
    }

    /// <summary>
    /// Strips the MySQL context registration the host set up, so the SQLite one that follows is
    /// the only provider in play rather than competing with it.
    /// </summary>
    private static void RemoveDbContextRegistration(IServiceCollection services)
    {
        var registrations = services
            .Where(descriptor =>
                descriptor.ServiceType == typeof(DbContextOptions<ProcessingDbContext>)
                || descriptor.ServiceType == typeof(DbContextOptions)
                || descriptor.ServiceType == typeof(ProcessingDbContext)
                || (descriptor.ServiceType.IsGenericType
                    && descriptor.ServiceType.GetGenericTypeDefinition().Name.StartsWith(
                        "IDbContextOptionsConfiguration", StringComparison.Ordinal)))
            .ToList();

        foreach (var registration in registrations)
        {
            services.Remove(registration);
        }
    }

    /// <summary>A client bearing a genuine JWT, signed with the key the host validates against.</summary>
    public HttpClient CreateClientAs(string role, string userId = "user-1", string userName = "k.perera")
    {
        var client = CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TokenFor(role, userId, userName));

        return client;
    }

    public static string TokenFor(
        string role,
        string userId = "user-1",
        string userName = "k.perera",
        string? signingKey = null,
        string? issuer = null)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer ?? Issuer,
            audience: Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId),
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, userName),
                new Claim(ClaimTypes.Role, role)
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
