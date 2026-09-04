using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProcessingService.Api.Infrastructure;
using ProcessingService.Application.Unloads;

namespace ProcessingService.Controllers;

/// <summary>What an officer records when a bowser is emptied into a storing tank (SCRUM-62).</summary>
public sealed class RecordUnloadRequest
{
    /// <summary>The dispatch note the bowser arrived against, raised at the chilling centre.</summary>
    /// <example>DN-20260904-01</example>
    [Required(ErrorMessage = "A dispatch note reference is required.")]
    [StringLength(40, MinimumLength = 1)]
    public string DispatchNoteReference { get; set; } = string.Empty;

    /// <summary>Code of the storing tank receiving the load.</summary>
    /// <example>ST1</example>
    [Required(ErrorMessage = "A storing tank is required.")]
    [StringLength(10, MinimumLength = 1)]
    public string StoringTankCode { get; set; } = string.Empty;

    /// <summary>
    /// Litres the factory measured off the bowser. This is the factory's own figure, not the one
    /// on the note - recording the note's would leave nothing independent to reconcile against.
    /// </summary>
    /// <example>11800</example>
    [Required(ErrorMessage = "The quantity unloaded is required.")]
    [Range(0.01, 1000000, ErrorMessage = "The quantity unloaded must be greater than zero.")]
    public decimal? QuantityLitres { get; set; }

    /// <summary>Temperature the load arrived at.</summary>
    /// <example>4.2</example>
    [Required(ErrorMessage = "An arrival temperature is required.")]
    [Range(-5, 40, ErrorMessage = "An arrival temperature must be between -5 and 40 °C.")]
    public decimal? TemperatureCelsius { get; set; }

    /// <summary>Wall-clock time at the factory. Omit and the service stamps the current time.</summary>
    /// <example>2026-09-04T08:45:00</example>
    public DateTime? UnloadedAtLocal { get; set; }
}

/// <summary>
/// Bowser loads arriving at the factory and going into a storing tank (SCRUM-62). This is where
/// milk enters the processing service.
/// </summary>
[ApiController]
[Route("api/processing/unloads")]
[Authorize(Policy = ProcessingPolicies.ReadProcessing)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class UnloadsController : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    private readonly IUnloadService _unloads;

    public UnloadsController(IUnloadService unloads)
    {
        _unloads = unloads;
    }

    /// <summary>Lists unloads, newest first, optionally for one factory day.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="date">Restrict to a single unload date.</param>
    /// <response code="200">The matching unloads.</response>
    /// <response code="400"><c>date</c> was supplied but is not a date.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<UnloadView>), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    public async Task<ActionResult<IReadOnlyList<UnloadView>>> List(
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? date = null) =>
        Ok(await _unloads.ListAsync(date, cancellationToken));

    /// <summary>Reads one unload by its factory reference.</summary>
    /// <response code="200">The unload.</response>
    /// <response code="404">No unload carries that reference.</response>
    [HttpGet("{reference}")]
    [ProducesResponseType(typeof(UnloadView), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ProcessingProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<ActionResult<UnloadView>> Get(string reference, CancellationToken cancellationToken)
    {
        var unload = await _unloads.GetAsync(reference, cancellationToken);

        return unload is null
            ? this.ProcessingProblem(
                StatusCodes.Status404NotFound,
                "entity_not_found",
                "Unload not found",
                $"No unload carries the reference '{reference}'.")
            : Ok(unload);
    }

    /// <summary>Records a bowser load going into a storing tank.</summary>
    /// <remarks>
    /// A load is unloaded once. The tank must be a storing tank, in service, and with room for
    /// what is being put into it.
    /// </remarks>
    /// <response code="201">The unload was recorded.</response>
    /// <response code="400">The tank is the wrong kind, out of service, or would overfill.</response>
    /// <response code="422">No tank carries that code.</response>
    /// <response code="409">That dispatch note has already been unloaded.</response>
    [HttpPost]
    [Authorize(Policy = ProcessingPolicies.RecordUnloads)]
    [ProducesResponseType(typeof(UnloadView), StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType(typeof(ProcessingProblemDetails), StatusCodes.Status409Conflict, ProblemJson)]
    [ProducesResponseType(typeof(ProcessingProblemDetails), StatusCodes.Status422UnprocessableEntity, ProblemJson)]
    public async Task<ActionResult<UnloadView>> Record(
        [FromBody] RecordUnloadRequest request,
        CancellationToken cancellationToken)
    {
        var unload = await _unloads.RecordAsync(
            new RecordUnloadCommand(
                request.DispatchNoteReference,
                request.StoringTankCode,
                request.QuantityLitres!.Value,
                request.TemperatureCelsius!.Value,
                request.UnloadedAtLocal,
                User.UserName()),
            cancellationToken);

        return CreatedAtAction(nameof(Get), new { reference = unload.Reference }, unload);
    }
}
