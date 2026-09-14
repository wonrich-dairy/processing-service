using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using SRC.Authorization;

namespace ProcessingService.Api.Infrastructure;

/// <summary>
/// Authentication and authorization wiring using shared Auth Service (SCRUM-56).
/// Tokens issued by Auth Service, validated here independently (no call-out).
/// Uses EXACT shared files from shared/Auth (WonrichRoles, WonrichClaims) to avoid role name drift.
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
                    // Auth service sets ClockSkew = Zero (see WonrichJwtOptions.cs) - match it exactly
                    ClockSkew = TimeSpan.Zero,
                    // CONFIRMED by AccessTokenIssuer.cs: uses ClaimTypes.NameIdentifier, ClaimTypes.Name, ClaimTypes.Role, WonrichClaims.Facility
                    RoleClaimType = ClaimTypes.Role,
                    NameClaimType = ClaimTypes.NameIdentifier
                };
            });

        return services;
    }

    public static IServiceCollection AddProcessingAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            // Policy for user management (same as Auth service)
            options.AddPolicy("ManageUsers", policy =>
                policy.RequireRole(WonrichRoles.SystemAdministrator));

            // Processing-specific policies
            options.AddPolicy("ProcessingTechnician", policy =>
                policy.RequireRole(WonrichRoles.ProcessingTechnician, WonrichRoles.SystemAdministrator, WonrichRoles.ProductionManager));

            options.AddPolicy("FactoryIntake", policy =>
                policy.RequireRole(WonrichRoles.FactoryIntakeOfficer, WonrichRoles.ProcessingTechnician, WonrichRoles.SystemAdministrator));

            options.AddPolicy("QualityAnalyst", policy =>
                policy.RequireRole(WonrichRoles.QualityAnalyst, WonrichRoles.SystemAdministrator));
        });
        return services;
    }

    public static IServiceCollection AddProcessingPolicies(this IServiceCollection services)
    {
        // Kept for backward compat, real policies now in AddProcessingAuthorization
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
