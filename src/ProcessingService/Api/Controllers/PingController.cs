using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SRC.Authorization;
using Wonrich.Auth.Tokens;

namespace ProcessingService.Api.Controllers;

/// <summary>
/// Throwaway endpoint to prove SCRUM-56 auth ACs (QA feedback) + new ProcessingTechnician role.
/// GET /api/ping requires Bearer token, returns user id and role using EXACT shared WonrichClaims.
/// Allows QA to verify: valid JWT -> authorized + user id/role available, no token -> 401.
/// ProcessingTechnician endpoint proves role-based auth works with new role.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class PingController : ControllerBase
{
    /// <summary>
    /// Returns caller's identity from JWT. Proves auth AC: valid JWT -> authorized and user id/role available.
    /// Uses shared WonrichPrincipalExtensions (exact copy from Auth service).
    /// </summary>
    [HttpGet]
    [Authorize]
    public IActionResult Get()
    {
        var userId = User.UserId() ?? User.Identity?.Name ?? "unknown";
        var userName = User.UserName();
        var role = User.Role();
        var facility = User.Facility();
        var allRoles = User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToArray();

        return Ok(new
        {
            message = "pong - auth works",
            userId,
            userName,
            role,
            facility,
            roles = allRoles,
            allConfiguredRoles = WonrichRoles.All,
            timestampUtc = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Proves ProcessingTechnician role works - only users with ProcessingTechnician, ProductionManager or SystemAdministrator can access.
    /// </summary>
    [HttpGet("processing-technician")]
    [Authorize(Roles = WonrichRoles.ProcessingTechnician)]
    public IActionResult GetProcessingTechnicianOnly()
    {
        return Ok(new
        {
            message = "pong - processing technician role works",
            userId = User.UserId(),
            role = User.Role(),
            requiredRole = WonrichRoles.ProcessingTechnician,
            timestampUtc = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Proves policy-based auth works - ProcessingTechnician policy allows ProcessingTechnician, ProductionManager, SystemAdministrator.
    /// </summary>
    [HttpGet("policy")]
    [Authorize(Policy = "ProcessingTechnician")]
    public IActionResult GetPolicy()
    {
        return Ok(new
        {
            message = "pong - ProcessingTechnician policy works",
            userId = User.UserId(),
            role = User.Role(),
            policy = "ProcessingTechnician",
            allowedRoles = new[] { WonrichRoles.ProcessingTechnician, WonrichRoles.ProductionManager, WonrichRoles.SystemAdministrator },
            timestampUtc = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Anonymous ping for comparison - proves /health-style anonymous vs authorized.
    /// </summary>
    [HttpGet("anonymous")]
    [AllowAnonymous]
    public IActionResult GetAnonymous()
    {
        return Ok(new { message = "pong - anonymous", timestampUtc = DateTimeOffset.UtcNow });
    }
}
