namespace ProcessingService.Domain.Common;

/// <summary>
/// Base type for rule violations raised by the domain model. Each exception carries a stable
/// <see cref="Code"/> so the API layer can map it onto a ProblemDetails response without
/// string-matching on messages.
/// </summary>
/// <remarks>
/// The same shape the MCC service settled on. Matching on an exception message there degraded a
/// 409 to a 400 as soon as anyone reworded it (SCRUM-100), so this service starts with codes.
/// </remarks>
public abstract class DomainException : Exception
{
    protected DomainException(string code, string message) : base(message)
    {
        Code = code;
    }

    /// <summary>Stable, machine-readable identifier for the violated rule.</summary>
    public string Code { get; }
}

/// <summary>Raised when a command carries values the domain cannot accept.</summary>
public sealed class DomainValidationException : DomainException
{
    public DomainValidationException(string message) : base("domain_validation_failed", message)
    {
    }
}

/// <summary>Raised when a referenced entity does not exist.</summary>
public sealed class EntityNotFoundException : DomainException
{
    public EntityNotFoundException(string entity, string identifier)
        : base("entity_not_found", $"{entity} '{identifier}' was not found.")
    {
        Entity = entity;
        Identifier = identifier;
    }

    public string Entity { get; }

    public string Identifier { get; }
}

/// <summary>Raised when a code the caller chose is already in use.</summary>
public sealed class DuplicateCodeException : DomainException
{
    public DuplicateCodeException(string entity, string conflictingCode)
        : base("duplicate_code", $"{entity} code '{conflictingCode}' is already in use.")
    {
        ConflictingCode = conflictingCode;
    }

    public string ConflictingCode { get; }
}

/// <summary>
/// Raised when a dispatch note that has already been unloaded is unloaded again (SCRUM-62). A
/// distinct type so the API answers 409 from the code rather than by matching on the message.
/// </summary>
public sealed class DispatchAlreadyUnloadedException : DomainException
{
    public DispatchAlreadyUnloadedException(string dispatchNoteReference)
        : base(
            "dispatch_already_unloaded",
            $"Dispatch note {dispatchNoteReference} has already been unloaded.")
    {
        DispatchNoteReference = dispatchNoteReference;
    }

    public string DispatchNoteReference { get; }
}
