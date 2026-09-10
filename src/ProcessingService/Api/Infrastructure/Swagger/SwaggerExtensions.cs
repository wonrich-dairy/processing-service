using Microsoft.OpenApi;

namespace ProcessingService.Api.Infrastructure.Swagger;

/// <summary>
/// Swagger / OpenAPI configuration for Processing Service (SCRUM-77).
/// Mirrors the pattern used in MCC Intake Service (SCRUM-49) so the template is consistent:
/// - Bearer JWT security scheme declared, so Swagger UI can send a token
/// - XML documentation comments included (requires GenerateDocumentationFile=true in csproj)
/// - UI exposed on /swagger, with explicit endpoint
/// - Disabled in Production (caller checks env, same as MCC)
/// 
/// Designed to be moved to the shared service template (Wonrich.ServiceTemplate) later.
/// </summary>
public static class SwaggerExtensions
{
    public static IServiceCollection AddProcessingSwagger(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Wonrich Processing Service API",
                Version = "v1",
                Description = "Processing stage records for Wonrich Dairy production batches. "
                              + "Milk is unloaded from bowser to storing tank after sensory checks, "
                              + "then lab panel result gates allocation to mixing tanks, followed by "
                              + "heating/homogeniser/pasteuriser stages, cooling and product split."
            });

            // Every route is [Authorize], and Swagger UI cannot send a token for a scheme the document
            // does not declare, so endpoints would be visible but not exercisable without this.
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

            // XML comments: picks up /// summaries on controllers and models, rendered in Swagger UI
            var xmlPath = Path.Combine(
                AppContext.BaseDirectory,
                $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml");

            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }

            // Ensure enums are documented as strings (since we use JsonStringEnumConverter)
            options.UseAllOfToExtendReferenceSchemas();
        });

        return services;
    }

    public static IApplicationBuilder UseProcessingSwaggerUI(this IApplicationBuilder app)
    {
        // Match MCC pattern: explicit endpoint and RoutePrefix = swagger
        // UI will be at /swagger, JSON at /swagger/v1/swagger.json
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Wonrich Processing Service API v1");
            options.RoutePrefix = "swagger";
            options.DisplayRequestDuration();
            options.EnableTryItOutByDefault();
        });

        return app;
    }
}
