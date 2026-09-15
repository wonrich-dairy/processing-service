namespace ProcessingService.Application.MccDispatch;

/// <summary>
/// Abstraction for MCC dispatch validation - mock now, real MCC API later.
/// Processing service must validate dispatch ID exists in MCC else reject.
/// </summary>
public interface IMccDispatchClient
{
    Task<bool> ExistsAsync(string dispatchNumber, CancellationToken cancellationToken);
    Task<MccDispatchDto?> GetAsync(string dispatchNumber, CancellationToken cancellationToken);
    Task<IReadOnlyList<MccDispatchDto>> ListRecentAsync(int take, CancellationToken cancellationToken);
}

public sealed class MccDispatchDto
{
    public string Reference { get; set; } = string.Empty;
    public string BowserRegistration { get; set; } = string.Empty;
    public string DispatchDate { get; set; } = string.Empty;
    public decimal TotalQuantityLitres { get; set; }
    public string DispatchedBy { get; set; } = string.Empty;
    public DateTime RecordedAtUtc { get; set; }
}
