using Microsoft.OpenApi;

namespace ProcessingService.Api.Infrastructure.Swagger;

/// <summary>
/// Swagger configuration for Processing Service (SCRUM-77).
/// Matches MCC pattern: own Swagger UI on /swagger, auth reflected, XML comments rendered,
/// available in Development/Staging, disabled in Production.
/// </summary>
public static class SwaggerExtensions
{
    public static IServiceCollection AddProcessingSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Wonrich Processing Service API",
                Version = "v1",
                Description = "Processing stage records for Wonrich Dairy production batches. Factory intake, lab panel, allocation, heating, cooling and product split."
            });

            // Bearer auth - matches Auth service token from POST /api/auth/login
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Access token from POST /api/auth/login on the auth service. Paste the token only (without Bearer prefix, Swagger adds it)."
            });

            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });

            // XML comments - if GenerateDocumentationFile enabled in csproj
            var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml");
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }

            // Include shared auth XML if present
            var sharedXml = Path.Combine(AppContext.BaseDirectory, "Shared.xml");
            if (File.Exists(sharedXml))
            {
                options.IncludeXmlComments(sharedXml);
            }
        });

        return services;
    }

    public static IApplicationBuilder UseProcessingSwagger(this IApplicationBuilder app, IWebHostEnvironment env)
    {
        // Swagger only in Development/Staging per SCRUM-77 AC: disabled in Production
        if (env.IsProduction())
        {
            return app;
        }

        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Wonrich Processing Service v1");
            options.RoutePrefix = "swagger"; // UI at /swagger, JSON at /swagger/v1/swagger.json - matches MCC
            options.DisplayRequestDuration();
            options.EnableTryItOutByDefault();
        });

        return app;
    }
}
