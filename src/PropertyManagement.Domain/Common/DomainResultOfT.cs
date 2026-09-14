namespace PropertyManagement.Domain.Common;

/// <summary>
/// A <see cref="DomainResult"/> that carries a value when it succeeds, for the operations whose
/// caller needs something back: the id of the application that was just started, for instance.
/// </summary>
public readonly record struct DomainResult<T>
{
    private DomainResult(bool succeeded, T? value, string? error)
    {
        Succeeded = succeeded;
        Value = value;
        Error = error;
    }

    public bool Succeeded { get; }

    public string? Error { get; }

    /// <summary>Set only when the result succeeded.</summary>
    public T? Value { get; }

    public bool Failed => !Succeeded;

    public static DomainResult<T> Success(T value) => new(true, value, null);

    public static DomainResult<T> Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new DomainResult<T>(false, default, error);
    }

    /// <summary>Carries a failure from a valueless check through to a valued caller.</summary>
    public static DomainResult<T> From(DomainResult result) =>
        result.Succeeded
            ? throw new InvalidOperationException("A successful result must supply a value.")
            : Failure(result.Error!);
}
