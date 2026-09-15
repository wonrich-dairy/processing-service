using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProcessingService.Domain.Entities;
using ProcessingService.Infrastructure.Persistence;

namespace ProcessingService.Application.QualityTests;

/// <summary>
/// Mock quality test client - implements REAL lab process per Problem 5 spec + PARTIAL UNLOAD support:
/// - Alcohol cascade sequential: 80% -> 75% -> 68% -> COB (COB only if all 3 failed)
/// - Positive = clotted = BAD, Negative = not clotted = GOOD
/// - KQ 7 colours independent
/// - Calculated values: Corrected CLR, SNF, TS derived never manual
/// - Final verdict: sensory fail OR COB Positive OR physical out of range => Reject else Accept (COB Positive overrides everything)
/// - Partial unload: same dispatch can be split across multiple tanks, quality test status applies to ALL runs with same dispatch
/// </summary>
public sealed class MockQualityTestClient : IQualityTestMockClient
{
    private readonly ProcessingDbContext _db;
    private readonly TimeProvider _time;

    public MockQualityTestClient(ProcessingDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<QualityTestStatus> GetStatusAsync(string dispatchNumber, CancellationToken cancellationToken)
    {
        var run = await _db.ProcessingRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.DispatchNumber == dispatchNumber, cancellationToken);
        return run?.QualityTestStatus ?? QualityTestStatus.Pending;
    }

    public async Task<QualityPanel?> GetResultAsync(string dispatchNumber, CancellationToken cancellationToken)
    {
        return await _db.QualityPanels.AsNoTracking()
            .FirstOrDefaultAsync(q => q.DispatchNumber == dispatchNumber, cancellationToken);
    }

    public async Task<ProcessingRun?> GetRunAsync(string dispatchNumber, CancellationToken cancellationToken)
    {
        return await _db.ProcessingRuns
            .Include(r => r.StoringTank)
            .Include(r => r.QualityPanel)
            .FirstOrDefaultAsync(r => r.DispatchNumber == dispatchNumber, cancellationToken);
    }

    public async Task<ProcessingRun> StartTestAsync(string dispatchNumber, string userId, CancellationToken cancellationToken)
    {
        var runs = await _db.ProcessingRuns
            .Where(r => r.DispatchNumber == dispatchNumber)
            .ToListAsync(cancellationToken);

        if (runs.Count == 0)
            throw new InvalidOperationException($"Dispatch '{dispatchNumber}' not found. Unload must be recorded first.");

        var firstPending = runs.FirstOrDefault(r => r.QualityTestStatus == QualityTestStatus.Pending);
        if (firstPending == null && runs.Any(r => r.QualityTestStatus != QualityTestStatus.Pending))
        {
            var existingStatus = runs.First().QualityTestStatus;
            if (existingStatus != QualityTestStatus.Pending)
                throw new InvalidOperationException($"Dispatch '{dispatchNumber}' already in {existingStatus} state. Cannot start again.");
        }

        var now = _time.GetUtcNow().UtcDateTime;
        foreach (var run in runs)
        {
            if (run.QualityTestStatus == QualityTestStatus.Pending)
            {
                run.QualityTestStatus = QualityTestStatus.InProgress;
                run.UpdatedAtUtc = now;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return runs.First();
    }

    public async Task<QualityPanel> SubmitResultAsync(string dispatchNumber, SubmitQualityResultRequest request, string userId, CancellationToken cancellationToken)
    {
        var runs = await _db.ProcessingRuns
            .Include(r => r.QualityPanel)
            .Where(r => r.DispatchNumber == dispatchNumber)
            .ToListAsync(cancellationToken);

        if (runs.Count == 0)
            throw new InvalidOperationException($"Dispatch '{dispatchNumber}' not found.");

        var firstRun = runs.First();
        if (firstRun.QualityTestStatus != QualityTestStatus.InProgress && firstRun.QualityTestStatus != QualityTestStatus.Pending)
            throw new InvalidOperationException($"Dispatch '{dispatchNumber}' is in {firstRun.QualityTestStatus} state. Start test first.");

        var cascade = ParseAndValidateCascade(request.AlcoholOutcomesJson);
        ValidateKqColour(request.KqColour);

        var correctedClr = CalculateCorrectedClr(request.RawLactometerReading, request.TemperatureCelsius);
        var snf = CalculateSnf(request.FatPercent, correctedClr);
        var ts = CalculateTs(snf, request.FatPercent);

        var verdictResult = DetermineVerdict(
            smellOk: request.SmellOk,
            colourOk: request.ColourOk,
            tasteOk: request.TasteOk,
            cascade: cascade,
            fatPercent: request.FatPercent,
            snf: snf,
            waterPercent: request.WaterPercent,
            correctedClr: correctedClr,
            kqColour: request.KqColour
        );

        // Remove existing panels for this dispatch (re-test)
        var existingPanels = await _db.QualityPanels
            .Where(q => q.DispatchNumber == dispatchNumber)
            .ToListAsync(cancellationToken);
        if (existingPanels.Count > 0)
            _db.QualityPanels.RemoveRange(existingPanels);

        var now = _time.GetUtcNow().UtcDateTime;
        var alcoholResult = DeriveAlcoholResult(cascade);

        // Create panel linked to first run (quality is per dispatch, not per tank split)
        var panel = new QualityPanel
        {
            Id = Guid.NewGuid(),
            ProcessingRunId = firstRun.Id,
            DispatchNumber = dispatchNumber,
            FatPercent = Math.Round(request.FatPercent, 2),
            RawLactometerReading = Math.Round(request.RawLactometerReading, 2),
            TemperatureCelsius = Math.Round(request.TemperatureCelsius, 2),
            WaterPercent = Math.Round(request.WaterPercent, 2),
            KqColour = request.KqColour,
            AlcoholOutcomesJson = JsonSerializer.Serialize(cascade),
            AlcoholResult = alcoholResult,
            SmellOk = request.SmellOk,
            ColourOk = request.ColourOk,
            TasteOk = request.TasteOk,
            Verdict = verdictResult.Verdict,
            FailedParameter = verdictResult.FailedParameter,
            FailedValue = verdictResult.FailedValue,
            Snf = Math.Round(snf, 2),
            Ts = Math.Round(ts, 2),
            Ph = Math.Round(request.Ph, 2),
            CreatedAtUtc = now,
            IsSmellConfirmed = request.SmellOk,
            IsTasteConfirmed = request.TasteOk,
            ConfirmedBy = userId,
            ConfirmedAtUtc = now
        };

        _db.QualityPanels.Add(panel);

        // Update ALL runs with same dispatch (partial unload support)
        foreach (var run in runs)
        {
            if (verdictResult.Verdict == "Accept")
            {
                run.QualityTestStatus = QualityTestStatus.Passed;
                run.State = ProcessingRunState.ReleasedForAllocation;
                run.HoldReason = null;
            }
            else
            {
                run.QualityTestStatus = QualityTestStatus.Failed;
                run.State = ProcessingRunState.OnHold;
                run.HoldReason = verdictResult.FailedParameter != null
                    ? $"Failed: {verdictResult.FailedParameter} {verdictResult.FailedValue}"
                    : "Failed quality test";
            }
            run.UpdatedAtUtc = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return panel;
    }

    private sealed class AlcoholCascade
    {
        public string? Alcohol80 { get; set; }
        public string? Alcohol75 { get; set; }
        public string? Alcohol68 { get; set; }
        public string? Cob { get; set; }
    }

    private static AlcoholCascade ParseAndValidateCascade(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Alcohol cascade outcomes required. Must test 80% first.");

        AlcoholCascade? cascade;
        try
        {
            cascade = JsonSerializer.Deserialize<AlcoholCascade>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            throw new InvalidOperationException("Invalid alcohol cascade JSON. Expected {\"Alcohol80\":\"Negative\",\"Alcohol75\":\"Positive\",...}");
        }

        if (cascade == null)
            throw new InvalidOperationException("Invalid alcohol cascade.");

        string? Norm(string? v) => v == null ? null : v.Trim();
        cascade.Alcohol80 = Norm(cascade.Alcohol80);
        cascade.Alcohol75 = Norm(cascade.Alcohol75);
        cascade.Alcohol68 = Norm(cascade.Alcohol68);
        cascade.Cob = Norm(cascade.Cob);

        bool IsValidOutcome(string? v) => v == "Negative" || v == "Positive";

        if (string.IsNullOrEmpty(cascade.Alcohol80) || !IsValidOutcome(cascade.Alcohol80))
            throw new InvalidOperationException("Alcohol 80% test is required and must be Negative (good) or Positive (bad/clotted).");

        if (cascade.Alcohol80 == "Negative")
        {
            if (!string.IsNullOrEmpty(cascade.Alcohol75) || !string.IsNullOrEmpty(cascade.Alcohol68) || !string.IsNullOrEmpty(cascade.Cob))
                throw new InvalidOperationException("Alcohol cascade violation: 80% Negative means STOP - 75%,68%,COB must not be tested.");
            return cascade;
        }

        if (string.IsNullOrEmpty(cascade.Alcohol75) || !IsValidOutcome(cascade.Alcohol75))
            throw new InvalidOperationException("Alcohol cascade: 80% Positive (clotted) requires retest at 75%.");

        if (cascade.Alcohol75 == "Negative")
        {
            if (!string.IsNullOrEmpty(cascade.Alcohol68) || !string.IsNullOrEmpty(cascade.Cob))
                throw new InvalidOperationException("Alcohol cascade violation: 75% Negative means STOP - 68%,COB must not be tested.");
            return cascade;
        }

        if (string.IsNullOrEmpty(cascade.Alcohol68) || !IsValidOutcome(cascade.Alcohol68))
            throw new InvalidOperationException("Alcohol cascade: 75% Positive requires retest at 68%.");

        if (cascade.Alcohol68 == "Negative")
        {
            if (!string.IsNullOrEmpty(cascade.Cob))
                throw new InvalidOperationException("Alcohol cascade violation: 68% Negative means STOP - COB must not be tested.");
            return cascade;
        }

        if (string.IsNullOrEmpty(cascade.Cob) || !IsValidOutcome(cascade.Cob))
            throw new InvalidOperationException("Alcohol cascade: 80%,75%,68% all Positive requires COB (Clot-on-Boiling) as final test - COB is ONLY performed after all three failed.");

        return cascade;
    }

    private static void ValidateKqColour(string kqColour)
    {
        var valid = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Blue", "Light Blue", "Purple", "Purple Pink", "Light Pink", "Pink", "White"
        };
        if (string.IsNullOrWhiteSpace(kqColour) || !valid.Contains(kqColour.Trim()))
            throw new InvalidOperationException($"Invalid KQ colour '{kqColour}'. Must be one of 7: Blue (Excellent), Light Blue (Very Good), Purple (Good), Purple Pink (Fair), Light Pink (Poor), Pink (Very Poor), White (Fail).");
    }

    private static decimal CalculateCorrectedClr(decimal rawClr, decimal temperature) => rawClr + 0.2m * (temperature - 27m);
    private static decimal CalculateSnf(decimal fat, decimal correctedClr) => (fat * 0.22m) + (correctedClr * 0.25m) + 0.72m;
    private static decimal CalculateTs(decimal snf, decimal fat) => snf + fat;

    private static string DeriveAlcoholResult(AlcoholCascade cascade)
    {
        if (cascade.Alcohol80 == "Negative") return "Passed 80%";
        if (cascade.Alcohol75 == "Negative") return "Passed 75%";
        if (cascade.Alcohol68 == "Negative") return "Passed 68%";
        if (cascade.Cob == "Negative") return "Passed COB";
        if (cascade.Cob == "Positive") return "Failed COB";
        return "Unknown";
    }

    private sealed class VerdictResult
    {
        public string Verdict { get; set; } = "Accept";
        public string? FailedParameter { get; set; }
        public string? FailedValue { get; set; }
    }

    private static VerdictResult DetermineVerdict(
        bool smellOk,
        bool colourOk,
        bool tasteOk,
        AlcoholCascade cascade,
        decimal fatPercent,
        decimal snf,
        decimal waterPercent,
        decimal correctedClr,
        string kqColour)
    {
        if (!smellOk)
            return new VerdictResult { Verdict = "Reject", FailedParameter = "Smell", FailedValue = "Not OK" };
        if (!colourOk)
            return new VerdictResult { Verdict = "Reject", FailedParameter = "Colour", FailedValue = "Not OK" };
        if (!tasteOk)
            return new VerdictResult { Verdict = "Reject", FailedParameter = "Taste", FailedValue = "Not OK" };

        if (cascade.Cob == "Positive")
            return new VerdictResult { Verdict = "Reject", FailedParameter = "COB", FailedValue = "Positive - clotted on boiling - overrides all" };

        if (kqColour.Trim().Equals("White", StringComparison.OrdinalIgnoreCase))
            return new VerdictResult { Verdict = "Reject", FailedParameter = "KQ", FailedValue = "White - Fail" };

        if (fatPercent < 3.0m)
            return new VerdictResult { Verdict = "Reject", FailedParameter = "FatPercent", FailedValue = $"{fatPercent} < 3.0 min" };
        if (fatPercent > 6.0m)
            return new VerdictResult { Verdict = "Reject", FailedParameter = "FatPercent", FailedValue = $"{fatPercent} > 6.0 max" };

        if (snf < 8.0m)
            return new VerdictResult { Verdict = "Reject", FailedParameter = "SNF", FailedValue = $"{snf:F2} < 8.0 min (Fat {fatPercent} + CLR {correctedClr:F2})" };

        if (waterPercent > 1.0m)
            return new VerdictResult { Verdict = "Reject", FailedParameter = "WaterPercent", FailedValue = $"{waterPercent} > 1.0 max" };

        if (correctedClr < 26m || correctedClr > 32m)
            return new VerdictResult { Verdict = "Reject", FailedParameter = "CorrectedCLR", FailedValue = $"{correctedClr:F2} out of 26-32 range" };

        return new VerdictResult { Verdict = "Accept" };
    }
}
