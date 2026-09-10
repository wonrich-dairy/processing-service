using ProcessingService.Application.Unloads;
using ProcessingService.Application.Tanks;
using ProcessingService.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OpenApi;
using ProcessingService.Api.Infrastructure;
using ProcessingService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Enums travel as their names so the API stays readable and does not break when a new stage is
// inserted into an enumeration.
builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Persistence (SCRUM-56). The connection string is supplied per environment; the local development
// value lives in appsettings.Development.json, which is not committed.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    // Failing here names the setting. Registering the context conditionally would instead leave
    // everything that depends on it unsatisfiable, and start-up would fail with a DI resolution
    // dump that never mentions the real cause.
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is not configured. Copy "
        + "src/ProcessingService/appsettings.Development.template.json to appsettings.Development.json "
        + "for local development, or set ConnectionStrings__DefaultConnection in the environment.");
}

builder.Services.AddDbContext<ProcessingDbContext>(options => options.UseMySQL(connectionString));

// Authentication and authorization (SCRUM-34). Tokens are issued by the auth service and validated
// here independently, so processing does not call out to authenticate a request.
builder.Services.AddProcessingAuthentication(builder.Configuration);
builder.Services.AddProcessingAuthorization();
builder.Services.AddProcessingPolicies();
builder.Services.AddProcessingCors(builder.Configuration);

builder.Services
    .AddOptions<FactoryOptions>()
    .Bind(builder.Configuration.GetSection(FactoryOptions.SectionName));

builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IFactoryClock, FactoryClock>();
builder.Services.AddScoped<ITankService, TankService>();
builder.Services.AddScoped<IUnloadService, UnloadService>();

// Domain rule violations become ProblemDetails rather than 500s.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Wonrich Processing Service API",
        Version = "v1",
        Description = "Processing stage records for Wonrich Dairy production batches."
    });

    // Every route is authorised, and Swagger UI cannot send a token for a scheme the document does
    // not declare, so the endpoints would be visible but not exercisable.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Access token from POST /api/auth/login on the auth service. Paste the token only."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });

    var xmlPath = Path.Combine(
        AppContext.BaseDirectory,
        $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml");

    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

app.UseExceptionHandler();

// Auto-apply pending EF migrations outside Production (SCRUM-71), so a freshly provisioned
// database builds its own schema on first start rather than needing `dotnet ef database update`
// run by hand against it. The intake and auth services already do this; without it, deploying
// against an empty database leaves a service running with no tables to read.
//
// Guarded on the provider: the migrations are MySQL-specific, and the tests host this same
// pipeline over a different provider, where they cannot be applied.
if (!app.Environment.IsProduction())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();

    if (db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true)
    {
        db.Database.Migrate();
    }
}

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Ahead of authentication: a preflight carries no Authorization header.
app.UseProcessingCors();

app.UseAuthentication();
app.UseAuthorization();

/*
 * Health is deliberately anonymous: the container runtime and the load balancer probe it before
 * anyone holds a token, and a probe that needs credentials cannot do its job. It reports the
 * database because a process that is running but cannot reach its data is not ready to serve.
 */
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
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description
                }),
        }));
    }
}).AllowAnonymous();

app.MapControllers();

app.Run();

/// <summary>Exposed so the integration tests can host this same pipeline.</summary>
public partial class Program;
