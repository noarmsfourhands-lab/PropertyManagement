namespace PropertyManagement.Domain.Common;

/// <summary>
/// Outcome of a business rule check. Rules return a result rather than throwing so that
/// controllers can turn a rejection into a validation message instead of an exception.
/// </summary>
public readonly record struct DomainResult
{
    private DomainResult(bool succeeded, string? error)
    {
        Succeeded = succeeded;
        Error = error;
    }

    public bool Succeeded { get; }

    public string? Error { get; }

    public bool Failed => !Succeeded;

    public static DomainResult Success() => new(true, null);

    public static DomainResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new DomainResult(false, error);
    }
}
