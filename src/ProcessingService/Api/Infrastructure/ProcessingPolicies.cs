using Microsoft.AspNetCore.Authorization;

namespace ProcessingService.Api.Infrastructure;

/// <summary>
/// The Wonrich roles, as the auth service issues them (SCRUM-45). Duplicated here for the reason
/// <see cref="ProcessingAuth"/> duplicates the validation contract: the shared library has no
/// package feed to be consumed from yet. These are names on a token, so drift shows up as a
/// refused request rather than a wrong answer.
/// </summary>
public static class WonrichRoles
{
    public const string SystemAdministrator = "SystemAdministrator";
    public const string MccManager = "MccManager";
    public const string IntakeOfficer = "IntakeOfficer";
    public const string QualityAnalyst = "QualityAnalyst";
    public const string FactoryIntakeOfficer = "FactoryIntakeOfficer";
    public const string ProductionManager = "ProductionManager";
}

/// <summary>What each processing endpoint asks for.</summary>
public static class ProcessingPolicies
{
    /// <summary>Add a factory tank, rename one, or take one out of service (SCRUM-61).</summary>
    public const string ManageTanks = "ManageTanks";

    /// <summary>Record a bowser load into a storing tank (SCRUM-62).</summary>
    public const string RecordUnloads = "RecordUnloads";

    /// <summary>Read the processing record without being able to change it.</summary>
    public const string ReadProcessing = "ReadProcessing";
}

public static class ProcessingAuthorizationExtensions
{
    /// <summary>
    /// Registers the processing policies. Tanks are plant, so configuring them is the production
    /// manager's; unloading is the factory intake officer's, who is at the bay when the bowser
    /// arrives. Reading is open to anyone with a reason to look, the quality analyst included.
    /// </summary>
    public static IServiceCollection AddProcessingPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(
                ProcessingPolicies.ManageTanks,
                policy => policy.RequireRole(
                    WonrichRoles.SystemAdministrator,
                    WonrichRoles.ProductionManager))
            .AddPolicy(
                ProcessingPolicies.RecordUnloads,
                policy => policy.RequireRole(
                    WonrichRoles.SystemAdministrator,
                    WonrichRoles.ProductionManager,
                    WonrichRoles.FactoryIntakeOfficer))
            .AddPolicy(
                ProcessingPolicies.ReadProcessing,
                policy => policy.RequireRole(
                    WonrichRoles.SystemAdministrator,
                    WonrichRoles.ProductionManager,
                    WonrichRoles.FactoryIntakeOfficer,
                    WonrichRoles.QualityAnalyst));

        return services;
    }
}

/// <summary>
/// The browser origin policy the SPA is served under. The client is a separate origin from every
/// service it calls, so each one answers its own preflight.
/// </summary>
/// <remarks>
/// The MCC service shipped without this and the SPA could not sign in at all; the browser reports
/// a failure carrying no status and no headers, which reads as an unreachable service rather than
/// a misconfigured one (SCRUM-92). Third service, same lesson - it goes in with the first
/// endpoint a browser will call.
/// </remarks>
public static class ProcessingCorsExtensions
{
    public const string PolicyName = "frontend";

    public const string SectionName = "Cors";

    public static IServiceCollection AddProcessingCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var origins = configuration.GetSection($"{SectionName}:AllowedOrigins").Get<string[]>() ?? [];

        return services.AddCors(options => options.AddPolicy(PolicyName, policy =>
        {
            if (origins.Length == 0)
            {
                return;
            }

            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
        }));
    }

    /// <summary>
    /// Puts the policy in the pipeline. Before authentication: a preflight carries no
    /// Authorization header, so it has to be answered before anything tries to authenticate it.
    /// </summary>
    public static IApplicationBuilder UseProcessingCors(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseCors(PolicyName);
    }
}
