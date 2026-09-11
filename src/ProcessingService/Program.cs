using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OpenApi;
using ProcessingService.Api.Infrastructure;
using ProcessingService.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Enums as names for readable API (SCRUM-57 will need this for varchar enums)
builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Persistence - Pomelo MySQL provider (SCRUM-56 AC: Pomelo, not Oracle, connection from config, no secret in source)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is not configured. Copy "
        + "src/ProcessingService/appsettings.Development.template.json to appsettings.Development.json "
        + "for local development, or set ConnectionStrings__DefaultConnection in the environment.");
}

// Pomelo provider - ServerVersion.AutoDetect reads MySQL version from connection
builder.Services.AddDbContext<ProcessingDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

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

// Health + ProblemDetails
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

// Swagger - available in Development/Staging, disabled in Production (SCRUM-77 will refine)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Wonrich Processing Service API",
        Version = "v1",
        Description = "Processing stage records for Wonrich Dairy production batches."
    });
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
    var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

app.UseExceptionHandler();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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

app.MapControllers();

app.Run();

public partial class Program;
