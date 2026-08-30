using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace ProcessingService.Api.Infrastructure;

/// <summary>
/// Token settings, bound from the "Auth" section. These are the settings the auth service issues
/// against (SCRUM-34), so the values must match the ones the other services are given.
/// </summary>
public sealed class ProcessingJwtOptions
{
    public const string SectionName = "Auth";

    public const int MinimumSigningKeyLength = 32;

    [Required]
    public string Issuer { get; set; } = "wonrich-auth";

    [Required]
    public string Audience { get; set; } = "wonrich-services";

    /// <summary>Supplied per environment and never committed.</summary>
    [Required(ErrorMessage = "Auth:SigningKey is required.")]
    [MinLength(
        MinimumSigningKeyLength,
        ErrorMessage = "Auth:SigningKey must be at least 32 characters so HMAC-SHA256 is not weakened.")]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Deliberately zero: the default five minutes would let an expired token keep working past
    /// its stated expiry.
    /// </summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.Zero;
}

/// <summary>
/// Validates the tokens the auth service issues (SCRUM-34). Validation is local — signature,
/// issuer, audience and expiry — so this service never calls out to authenticate a request.
/// </summary>
/// <remarks>
/// <para>
/// <b>This duplicates <c>Wonrich.Auth.WonrichAuthExtensions</c> and should not stay that way.</b>
/// The shared library lives in the mcc-intake-service repository as a project reference, and no
/// package feed is configured for any service to consume it from — <c>Wonrich.QualityPanel</c>'s
/// csproj notes that publishing is the pipeline's job (SCRUM-37), but the pipeline shipped without
/// one. Until a feed exists, a separate repository has nothing to reference.
/// </para>
/// <para>
/// What is duplicated here is the validation contract, not business logic: issuer, audience, key
/// and which claim carries the role. Those are the four things the token itself fixes, so drift
/// shows up immediately as a rejected token rather than as a wrong answer. Replacing this with
/// the published package is a one-line change and wants its own ticket.
/// </para>
/// </remarks>
public static class ProcessingAuth
{
    public static IServiceCollection AddProcessingAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<ProcessingJwtOptions>()
            .Bind(configuration.GetSection(ProcessingJwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = new ProcessingJwtOptions();
        configuration.GetSection(ProcessingJwtOptions.SectionName).Bind(options);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = SigningKeyFor(options),
                    ValidateLifetime = true,
                    ClockSkew = options.ClockSkew,
                    RoleClaimType = ClaimTypes.Role,
                    NameClaimType = ClaimTypes.Name
                };

                bearer.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        // Logged without the token itself: a rejected token is worth noticing, and
                        // echoing it into the log would store a live credential.
                        context.HttpContext
                            .RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger(nameof(ProcessingAuth))
                            .LogWarning(
                                "Token rejected for {Method} {Path} from {Source}: {Reason}",
                                context.Request.Method,
                                context.Request.Path,
                                context.HttpContext.Connection.RemoteIpAddress,
                                context.Exception.GetType().Name);

                        return Task.CompletedTask;
                    }
                };
            });

        return services;
    }

    /// <summary>
    /// Every endpoint requires an authenticated caller unless it opts out, so a new controller is
    /// guarded by default rather than by remembering an attribute.
    /// </summary>
    public static IServiceCollection AddProcessingAuthorization(this IServiceCollection services)
    {
        services
            .AddAuthorizationBuilder()
            .SetFallbackPolicy(
                new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build());

        return services;
    }

    private static SymmetricSecurityKey SigningKeyFor(ProcessingJwtOptions options) =>
        new(Encoding.UTF8.GetBytes(options.SigningKey));
}

/// <summary>Reads the Wonrich claims off an authenticated principal.</summary>
public static class ProcessingPrincipalExtensions
{
    public static string? UserId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier);

    public static string? UserName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Name);

    /// <summary>The single role the user holds. Users carry exactly one role (SCRUM-45).</summary>
    public static string? Role(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Role);
}
