using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProcessingService.Application.QualityTests;
using ProcessingService.Domain.Entities;
using SRC.Authorization;
using Wonrich.Auth.Tokens;

namespace ProcessingService.Api.Controllers;

/// <summary>
/// Mock quality test endpoints - implements REAL lab process per Problem 5:
/// Alcohol cascade 80%->75%->68%->COB sequential, Positive=clotted=BAD, Negative=good
/// KQ 7 colours, calculated SNF/TS/CorrectedCLR, verdict auto-derived
/// Returns DTOs to avoid JSON cycles.
/// </summary>
[ApiController]
[Route("api/quality-tests")]
[Authorize(Roles = $"{WonrichRoles.SystemAdministrator},{WonrichRoles.ProductionManager},{WonrichRoles.ProcessingTechnician},{WonrichRoles.FactoryIntakeOfficer},{WonrichRoles.QualityAnalyst}")]
public sealed class QualityTestsController : ControllerBase
{
    private readonly IQualityTestMockClient _quality;

    public QualityTestsController(IQualityTestMockClient quality)
    {
        _quality = quality;
    }

    [HttpGet("{dispatchNumber}/status")]
    public async Task<ActionResult<object>> GetStatus(string dispatchNumber, CancellationToken cancellationToken)
    {
        var run = await _quality.GetRunAsync(dispatchNumber, cancellationToken);
        if (run == null)
            return NotFound(new { message = $"Dispatch '{dispatchNumber}' not found. Record unload first." });

        var panel = await _quality.GetResultAsync(dispatchNumber, cancellationToken);

        return Ok(new
        {
            dispatchNumber = run.DispatchNumber,
            processingRunId = run.Id,
            storingTankId = run.StoringTankId,
            storingTankCode = run.StoringTank?.Code,
            quantityKg = run.QuantityKg,
            qualityTestStatus = run.QualityTestStatus.ToString(),
            processingState = run.State.ToString(),
            hasResult = panel != null,
            verdict = panel?.Verdict,
            createdAtUtc = run.CreatedAtUtc,
            updatedAtUtc = run.UpdatedAtUtc
        });
    }

    [HttpGet("{dispatchNumber}")]
    public async Task<ActionResult<object>> GetResult(string dispatchNumber, CancellationToken cancellationToken)
    {
        var panel = await _quality.GetResultAsync(dispatchNumber, cancellationToken);
        if (panel == null)
            return NotFound(new { message = $"No quality result for dispatch '{dispatchNumber}'. Status is still pending or in progress." });

        return Ok(ToPanelDto(panel));
    }

    [HttpGet("pending")]
    public async Task<ActionResult<object>> ListPending([FromServices] ProcessingService.Infrastructure.Persistence.ProcessingDbContext db, CancellationToken cancellationToken)
    {
        var runs = await db.ProcessingRuns
            .Include(r => r.StoringTank)
            .Include(r => r.QualityPanel)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var result = runs.Select(r => new
        {
            dispatchNumber = r.DispatchNumber,
            processingRunId = r.Id,
            storingTankCode = r.StoringTank?.Code,
            quantityKg = r.QuantityKg,
            qualityTestStatus = r.QualityTestStatus.ToString(),
            processingState = r.State.ToString(),
            verdict = r.QualityPanel?.Verdict,
            createdAtUtc = r.CreatedAtUtc
        });

        return Ok(result);
    }

    [HttpPost("{dispatchNumber}/start")]
    [Authorize(Roles = $"{WonrichRoles.SystemAdministrator},{WonrichRoles.ProductionManager},{WonrichRoles.QualityAnalyst}")]
    public async Task<ActionResult> StartTest(string dispatchNumber, CancellationToken cancellationToken)
    {
        try
        {
            var userId = User.UserId() ?? User.Identity?.Name ?? "unknown";
            var run = await _quality.StartTestAsync(dispatchNumber, userId, cancellationToken);
            return Ok(new { message = $"Quality test for '{dispatchNumber}' started.", status = run.QualityTestStatus.ToString() });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPost("{dispatchNumber}/result")]
    [Authorize(Roles = $"{WonrichRoles.SystemAdministrator},{WonrichRoles.ProductionManager},{WonrichRoles.QualityAnalyst}")]
    public async Task<ActionResult<object>> SubmitResult(string dispatchNumber, [FromBody] SubmitQualityResultRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = User.UserId() ?? User.Identity?.Name ?? "unknown";
            var panel = await _quality.SubmitResultAsync(dispatchNumber, request, userId, cancellationToken);
            return Ok(ToPanelDto(panel));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    private static object ToPanelDto(QualityPanel panel)
    {
        // Corrected CLR = raw CLR + 0.2 x (temperature - 27) per real process Step 4
        var correctedClr = panel.RawLactometerReading + 0.2m * (panel.TemperatureCelsius - 27m);

        return new
        {
            id = panel.Id,
            processingRunId = panel.ProcessingRunId,
            dispatchNumber = panel.DispatchNumber,
            fatPercent = panel.FatPercent,
            rawLactometerReading = panel.RawLactometerReading,
            temperatureCelsius = panel.TemperatureCelsius,
            waterPercent = panel.WaterPercent,
            correctedClr = Math.Round(correctedClr, 2),
            snf = panel.Snf,
            ts = panel.Ts,
            ph = panel.Ph,
            kqColour = panel.KqColour,
            alcoholOutcomesJson = panel.AlcoholOutcomesJson,
            alcoholResult = panel.AlcoholResult,
            smellOk = panel.SmellOk,
            colourOk = panel.ColourOk,
            tasteOk = panel.TasteOk,
            verdict = panel.Verdict,
            failedParameter = panel.FailedParameter,
            failedValue = panel.FailedValue,
            createdAtUtc = panel.CreatedAtUtc,
            confirmedBy = panel.ConfirmedBy,
            confirmedAtUtc = panel.ConfirmedAtUtc,
            isSmellConfirmed = panel.IsSmellConfirmed,
            isTasteConfirmed = panel.IsTasteConfirmed
        };
    }
}
