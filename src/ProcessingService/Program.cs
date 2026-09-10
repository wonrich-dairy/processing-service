using ProcessingService.Application.Unloads;
using ProcessingService.Application.Tanks;
using ProcessingService.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProcessingService.Api.Infrastructure;
using ProcessingService.Api.Infrastructure.Swagger;
using ProcessingService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddProcessingSwagger();

var app = builder.Build();
app.UseExceptionHandler();

if (!app.Environment.IsProduction())
{
    app.UseProcessingSwaggerUI();
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

app.MapControllers();
app.Run();
public partial class Program;