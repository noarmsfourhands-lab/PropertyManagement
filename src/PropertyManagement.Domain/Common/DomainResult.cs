namespace PropertyManagement.Domain.Common;

/// <summary>
/// Outcome of a business rule check. Rules return a result rather than throwing so that
/// controllers can turn a rejection into a validation message instead of an exception.
/// </summary>
public readonly record struct DomainResult
{
    private DomainResult(bool succeeded, string? error, string? field)
    {
        Succeeded = succeeded;
        Error = error;
        Field = field;
    }

    public bool Succeeded { get; }

    public string? Error { get; }

    /// <summary>
    /// The field the rejection belongs to, or null when it is about the record as a whole.
    ///
    /// Carried here because the rule is the only thing that knows which value it refused. Without
    /// it a caller has to work that out by reading the message, and rewording a sentence then
    /// silently moves the error onto the wrong input, or off the form entirely.
    /// </summary>
    public string? Field { get; }

    public bool Failed => !Succeeded;

    public static DomainResult Success() => new(true, null, null);

    /// <param name="error">What is wrong, in the words the person reading it needs.</param>
    /// <param name="field">
    /// The property it belongs to, named as the rule's own type spells it. Omit it when the
    /// problem is about the record rather than one of its values.
    /// </param>
    public static DomainResult Failure(string error, string? field = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new DomainResult(false, error, field);
    }
}
