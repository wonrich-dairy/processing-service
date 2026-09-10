using ProcessingService.Application.Unloads;
using ProcessingService.Application.Tanks;
using ProcessingService.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OpenApi;
using ProcessingService.Api.Infrastructure;
using ProcessingService.Api.Infrastructure.Observability;
using ProcessingService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Observability first (SCRUM-90)
builder.AddProcessingObservability();

builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is not configured. Copy "
        + "src/ProcessingService/appsettings.Development.template.json to appsettings.Development.json "
        + "for local development, or set ConnectionStrings__DefaultConnection in the environment.");
}
builder.Services.AddDbContext<ProcessingDbContext>(options => options.UseMySQL(connectionString));

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
builder.Services.AddSingleton<ProcessingMetrics>();

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
    if (File.Exists(xmlPath)) options.IncludeXmlComments(xmlPath);
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseCorrelationId();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseProcessingCors();
app.UseAuthentication();
app.UseAuthorization();

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

app.MapPrometheusScrapingEndpoint().AllowAnonymous();
app.MapControllers();
app.Run();
public partial class Program;