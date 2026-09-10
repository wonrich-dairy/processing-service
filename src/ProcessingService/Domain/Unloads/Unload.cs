using ProcessingService.Domain.Common;
using ProcessingService.Domain.Tanks;

namespace ProcessingService.Domain.Unloads;

/// <summary>
/// A bowser's load being unloaded into a storing tank at the factory (SCRUM-62).
/// </summary>
/// <remarks>
/// <para>
/// This is where milk enters the processing service. The bowser arrives against a dispatch note
/// raised at the chilling centre and screened at the factory gate, both of which live in the MCC
/// and Intake Service; the references are carried here as text rather than as foreign keys,
/// because the two services own separate databases and neither may reach into the other's.
/// </para>
/// <para>
/// The quantity is what the factory measured, not what the dispatch note claimed. The two are
/// compared by whoever reconciles them; recording the note's figure here would leave no
/// independent measurement to compare against.
/// </para>
/// </remarks>
public class Unload
{
    public const int MaxReferenceLength = 30;
    public const int MaxDispatchReferenceLength = 40;
    public const decimal MinTemperatureCelsius = -5m;
    public const decimal MaxTemperatureCelsius = 40m;
    public const decimal ExpectedTemperatureMinCelsius = 1m;
    public const decimal ExpectedTemperatureMaxCelsius = 3m;

    /// <summary>EF Core materialisation constructor.</summary>
    private Unload()
    {
        Reference = string.Empty;
        DispatchNoteReference = string.Empty;
    }

    private Unload(
        Guid id,
        string reference,
        string dispatchNoteReference,
        ProcessingTank tank,
        decimal quantityLitres,
        decimal temperatureCelsius,
        string? unloadedBy,
        DateTime unloadedAtLocal,
        DateTimeOffset recordedAtUtc)
    {
        Id = id;
        Reference = reference;
        DispatchNoteReference = dispatchNoteReference;
        StoringTankId = tank.Id;
        QuantityLitres = quantityLitres;
        TemperatureCelsius = temperatureCelsius;
        IsTemperatureDeviation =
            temperatureCelsius < ExpectedTemperatureMinCelsius
            || temperatureCelsius > ExpectedTemperatureMaxCelsius;
        UnloadedBy = unloadedBy;
        UnloadedAtLocal = unloadedAtLocal;
        UnloadDate = DateOnly.FromDateTime(unloadedAtLocal);
        RecordedAtUtc = recordedAtUtc.UtcDateTime;
    }

    public Guid Id { get; private set; }

    /// <summary>Factory reference for the unload, <c>UNL-YYYYMMDD-NN</c>.</summary>
    public string Reference { get; private set; }

    /// <summary>The dispatch note the bowser arrived against, raised at the chilling centre.</summary>
    public string DispatchNoteReference { get; private set; }

    public Guid StoringTankId { get; private set; }

    public ProcessingTank? StoringTank { get; private set; }

    /// <summary>Litres the factory measured off the bowser.</summary>
    public decimal QuantityLitres { get; private set; }

    /// <summary>Temperature the load arrived at.</summary>
    public decimal TemperatureCelsius { get; private set; }

    /// <summary>
    /// True when the load arrived outside the expected 1 to 3 °C band. A deviation warns but never
    /// blocks: the unload is still saved, and the flag is what marks it for follow-up instead of
    /// leaving the excursion visible only in the raw temperature figure.
    /// </summary>
    public bool IsTemperatureDeviation { get; private set; }

    public string? UnloadedBy { get; private set; }

    /// <summary>Wall-clock time at the factory when the load went into the tank.</summary>
    public DateTime UnloadedAtLocal { get; private set; }

    /// <summary>
    /// Date of the unload at the factory, so a day's unloads can be pulled without date
    /// arithmetic. Bucketed on local time, as the MCC service buckets its gate references.
    /// </summary>
    public DateOnly UnloadDate { get; private set; }

    public DateTime RecordedAtUtc { get; private set; }

    /// <summary>Records a load going into a storing tank.</summary>
    /// <param name="id">Identity for the unload.</param>
    /// <param name="reference">Allocated factory reference.</param>
    /// <param name="dispatchNoteReference">Dispatch note the bowser arrived against.</param>
    /// <param name="tank">The storing tank receiving the load.</param>
    /// <param name="quantityLitres">Litres the factory measured.</param>
    /// <param name="temperatureCelsius">Temperature the load arrived at.</param>
    /// <param name="unloadedBy">Who recorded it.</param>
    /// <param name="unloadedAtLocal">Wall-clock time at the factory.</param>
    /// <param name="recordedAtUtc">Instant the record was written.</param>
    /// <param name="heldLitres">What the tank already holds, so an overfill can be refused.</param>
    public static Unload Record(
        Guid id,
        string reference,
        string dispatchNoteReference,
        ProcessingTank tank,
        decimal quantityLitres,
        decimal temperatureCelsius,
        string? unloadedBy,
        DateTime unloadedAtLocal,
        DateTimeOffset recordedAtUtc,
        decimal heldLitres)
    {
        ArgumentNullException.ThrowIfNull(tank);

        tank.EnsureKind(TankKind.Storing);
        tank.EnsureAvailable();

        if (string.IsNullOrWhiteSpace(dispatchNoteReference))
        {
            throw new DomainValidationException("A dispatch note reference is required.");
        }

        if (quantityLitres <= 0)
        {
            throw new DomainValidationException("The quantity unloaded must be greater than zero.");
        }

        // The tank's working volume is a physical limit, so exceeding it is a mis-keyed figure
        // rather than a decision anyone is entitled to record.
        if (heldLitres + quantityLitres > tank.CapacityLitres)
        {
            throw new DomainValidationException(
                $"Tank {tank.Code} holds {heldLitres:0.##} L of {tank.CapacityLitres:0.##} L. "
                + $"Unloading {quantityLitres:0.##} L would overfill it.");
        }

        if (temperatureCelsius < MinTemperatureCelsius || temperatureCelsius > MaxTemperatureCelsius)
        {
            throw new DomainValidationException(
                $"An arrival temperature must be between {MinTemperatureCelsius:0.#} and "
                + $"{MaxTemperatureCelsius:0.#} °C.");
        }

        return new Unload(
            id,
            reference,
            dispatchNoteReference.Trim().ToUpperInvariant(),
            tank,
            quantityLitres,
            temperatureCelsius,
            unloadedBy,
            unloadedAtLocal,
            recordedAtUtc);
    }
}