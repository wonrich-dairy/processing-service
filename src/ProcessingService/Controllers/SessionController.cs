using Microsoft.AspNetCore.Mvc;
using ProcessingService.Api.Infrastructure;

namespace ProcessingService.Controllers;

/// <summary>Who the bearer token says the caller is.</summary>
/// <param name="UserId">The user's stable identifier, from the token subject.</param>
/// <param name="UserName">Sign-in name, carried so records can say who acted.</param>
/// <param name="Role">The single role the user holds (SCRUM-45).</param>
public sealed record CallerView(string? UserId, string? UserName, string? Role);

/// <summary>
/// The caller's own identity, as this service reads it off the shared token (SCRUM-34).
/// </summary>
/// <remarks>
/// The scaffold has no processing endpoints yet — the stage records are SCRUM-13 onwards. This
/// exists so the authentication wiring is exercisable and provable now rather than on trust: it is
/// guarded like every other route, and it answers with the identity a later endpoint would stamp
/// onto a record.
/// </remarks>
[ApiController]
[Route("api/session")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public class SessionController : ControllerBase
{
    /// <summary>Returns the caller's identity as read from the bearer token.</summary>
    /// <response code="200">The token was accepted and its claims are readable.</response>
    [HttpGet("me")]
    [ProducesResponseType(typeof(CallerView), StatusCodes.Status200OK, "application/json")]
    public ActionResult<CallerView> Me() =>
        Ok(new CallerView(User.UserId(), User.UserName(), User.Role()));
}
