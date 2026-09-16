using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProcessingService.Application.Tanks;
using SRC.Authorization;
using Wonrich.Auth.Tokens;

namespace ProcessingService.Api.Controllers;

/// <summary>
/// Temperature logging for processing tanks (ST and MT) - similar to MCC tanks.
/// Worker can log temperature when needed with optional note, timestamp stored.
/// First field when touching ST/MT card in tanks section.
/// </summary>
[ApiController]
[Route("api/tanks/{tankId:guid}/temperature-logs")]
[Authorize(Roles = $"{WonrichRoles.SystemAdministrator},{WonrichRoles.ProductionManager},{WonrichRoles.ProcessingTechnician},{WonrichRoles.FactoryIntakeOfficer}")]
public sealed class TankTemperatureLogsController : ControllerBase
{
    private readonly ITankTemperatureLogService _logs;

    public TankTemperatureLogsController(ITankTemperatureLogService logs)
    {
        _logs = logs;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<object>>> List(Guid tankId, CancellationToken cancellationToken)
    {
        var result = await _logs.ListAsync(tankId, cancellationToken);
        var dto = result.Select(ToDto).ToList();
        return Ok(dto);
    }

    [HttpGet("last")]
    public async Task<ActionResult<object>> GetLast(Guid tankId, CancellationToken cancellationToken)
    {
        var log = await _logs.GetLastAsync(tankId, cancellationToken);
        return log == null ? NotFound(new { message = $"No temperature logs for tank {tankId}" }) : Ok(ToDto(log));
    }

    [HttpPost]
    public async Task<ActionResult<object>> Log(Guid tankId, [FromBody] CreateTemperatureLogRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = User.UserId() ?? User.Identity?.Name ?? "unknown";
            var log = await _logs.LogAsync(tankId, request.TemperatureC, request.Note, userId, cancellationToken);
            return CreatedAtAction(nameof(GetLast), new { tankId }, ToDto(log));
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(ex.ParamName ?? "", ex.Message);
            return ValidationProblem(ModelState);
        }
    }

    private static object ToDto(ProcessingService.Domain.Entities.TankTemperatureLog log)
    {
        return new
        {
            id = log.Id,
            tankId = log.TankId,
            temperatureC = log.TemperatureC,
            note = log.Note,
            recordedAtUtc = log.RecordedAtUtc,
            recordedBy = log.RecordedBy,
            createdAtUtc = log.CreatedAtUtc
        };
    }
}
