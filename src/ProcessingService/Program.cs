using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProcessingService.Api.Infrastructure;
using ProcessingService.Api.Infrastructure.Observability;
using ProcessingService.Api.Infrastructure.Swagger;
using ProcessingService.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Enums as names for readable API (SCRUM-57 will need this for varchar enums)
builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Persistence - Pomelo MySQL provider (SCRUM-56 AC: Pomelo, not Oracle, connection from config, no secret in source)
// SCRUM-71: Dedicated MySQL, credentials scoped, migrations independent, remote DB for local dev per new AC
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is not configured. Copy "
        + "src/ProcessingService/appsettings.Development.template.json to appsettings.Development.json "
        + "for local development, or set ConnectionStrings__DefaultConnection in the environment, "
        + "or create .env from .env.example for Docker compose.");
}

// Pomelo fork Microting.EntityFrameworkCore.MySql - explicit version to avoid needing live connection at startup for AutoDetect
// Supports EF Core 10, official Pomelo 9.0.0 only supports EF Core 9
var serverVersion = new MySqlServerVersion(new Version(8, 4, 0));
builder.Services.AddDbContext<ProcessingDbContext>(options =>
    options.UseMySql(connectionString, serverVersion));

// Authentication and authorization (SCRUM-56 AC: shared auth library, 401 for unauthenticated except /health)
builder.Services.AddProcessingAuthentication(builder.Configuration);
builder.Services.AddProcessingAuthorization();
builder.Services.AddProcessingPolicies();
builder.Services.AddProcessingCors(builder.Configuration);

builder.Services
    .AddOptions<FactoryOptions>()
    .Bind(builder.Configuration.GetSection(FactoryOptions.SectionName));

builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IFactoryClock, FactoryClock>();

// Observability (SCRUM-90: metrics, structured logging, correlation ID)
builder.Services.AddProcessingObservability(builder.Configuration);

// Health + ProblemDetails + Swagger (SCRUM-77: own Swagger UI, auth reflected, XML comments, disabled in prod)
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
builder.Services.AddProcessingSwagger();

var app = builder.Build();

app.UseExceptionHandler();

// Auto-apply pending EF migrations in Development and Staging only (SCRUM-71 DOD: runs migrations without manual steps)
// Skipped in Production and Testing - Production uses controlled deploy, Testing uses InMemory/SQLite and has no MySQL
// Guarded on MySQL provider: migrations are MySQL-specific
if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();
        if (db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true)
        {
            db.Database.Migrate();
        }
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "Auto-migrate skipped - database not reachable at startup. Will be applied via dotnet ef database update or on next restart when DB is reachable.");
    }
}

app.UseProcessingSwagger(app.Environment);

app.UseProcessingObservability();

app.UseProcessingCors();
app.UseAuthentication();
app.UseAuthorization();

// Health is anonymous (SCRUM-56: container runtime probes before token)
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new { status = entry.Value.Status.ToString(), description = entry.Value.Description }),
        }));
    }
}).AllowAnonymous();

// Metrics is anonymous for Prometheus scraping (SCRUM-90)
app.MapProcessingMetrics();

app.MapControllers();

app.Run();

public partial class Program;