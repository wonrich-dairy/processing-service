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
