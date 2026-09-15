using ProcessingService.Domain.Entities;

namespace ProcessingService.Application.QualityTests;

/// <summary>
/// Abstraction for quality test service - mock now, real later.
/// Processing service never implements quality logic, only reads status and readonly panel.
/// </summary>
public interface IQualityTestClient
{
    Task<QualityTestStatus> GetStatusAsync(string dispatchNumber, CancellationToken cancellationToken);
    Task<QualityPanel?> GetResultAsync(string dispatchNumber, CancellationToken cancellationToken);
    Task<ProcessingRun?> GetRunAsync(string dispatchNumber, CancellationToken cancellationToken);
}

/// <summary>
/// Mock implementation - reads from Processing DB QualityPanel + ProcessingRun.QualityTestStatus
/// Real implementation later will call Quality Service API via HTTP.
/// No change needed in callers when switching.
/// </summary>
public interface IQualityTestMockClient : IQualityTestClient
{
    Task<ProcessingRun> StartTestAsync(string dispatchNumber, string userId, CancellationToken cancellationToken);
    Task<QualityPanel> SubmitResultAsync(string dispatchNumber, SubmitQualityResultRequest request, string userId, CancellationToken cancellationToken);
}

public sealed class SubmitQualityResultRequest
{
    public decimal FatPercent { get; set; }
    public decimal RawLactometerReading { get; set; }
    public decimal TemperatureCelsius { get; set; }
    public decimal WaterPercent { get; set; }
    public string KqColour { get; set; } = string.Empty;
    public string AlcoholOutcomesJson { get; set; } = string.Empty;
    public string AlcoholResult { get; set; } = string.Empty;
    public bool SmellOk { get; set; }
    public bool ColourOk { get; set; }
    public bool TasteOk { get; set; }
    public string Verdict { get; set; } = string.Empty; // Accept/Reject
    public string? FailedParameter { get; set; }
    public string? FailedValue { get; set; }
    public decimal Snf { get; set; }
    public decimal Ts { get; set; }
    public decimal Ph { get; set; }
}
