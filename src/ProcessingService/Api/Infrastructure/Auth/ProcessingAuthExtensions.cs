using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace ProcessingService.Api.Infrastructure;

/// <summary>
/// Authentication and authorization wiring using shared Auth Service (SCRUM-56).
/// Tokens issued by Auth Service, validated here independently (no call-out).
/// </summary>
public static class ProcessingAuthExtensions
{
    public static IServiceCollection AddProcessingAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var issuer = configuration["Auth:Issuer"] ?? "wonrich-auth";
        var audience = configuration["Auth:Audience"] ?? "wonrich-services";
        var signingKey = configuration["Auth:SigningKey"] ?? "replace-with-the-shared-signing-key-at-least-32-characters";

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });

        return services;
    }

    public static IServiceCollection AddProcessingAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddProcessingPolicies(this IServiceCollection services)
    {
        // Real implementation registers WonrichRoles-based policies.
        // For scaffold, keep empty - policies added later.
        return services;
    }

    public static IServiceCollection AddProcessingCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                      ?? new[] { "http://localhost:5173", "http://127.0.0.1:5173" };

        services.AddCors(options =>
        {
            options.AddPolicy("ProcessingCors", policy =>
            {
                policy.WithOrigins(origins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
        });

        return services;
    }

    public static IApplicationBuilder UseProcessingCors(this IApplicationBuilder app)
    {
        return app.UseCors("ProcessingCors");
    }
}

public static class WonrichRoles
{
    public const string SystemAdministrator = "SystemAdministrator";
    public const string ProductionManager = "ProductionManager";
    public const string FactoryIntakeOfficer = "FactoryIntakeOfficer";
    public const string QualityAnalyst = "QualityAnalyst";
    public const string IntakeOfficer = "IntakeOfficer";
    public const string MccManager = "MccManager";
}
