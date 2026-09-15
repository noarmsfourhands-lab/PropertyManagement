using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PropertyManagement.Infrastructure.Persistence;

/// <summary>
/// Keeps every stored instant unambiguously UTC.
///
/// The timestamps in this model are UTC by construction: they come from an injected
/// <see cref="TimeProvider"/> and their names say so. A database column, however, stores no kind,
/// so a value read back would arrive as <see cref="DateTimeKind.Unspecified"/> and any later
/// conversion to local time would be wrong by the machine's offset. Applying these converters by
/// convention means a value cannot be written without being normalised, or read without its kind
/// being restored. That removes the usual argument for storing an offset alongside a value that
/// never has one.
/// </summary>
public class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    value => Normalise(value),
    value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
{
    /// <summary>
    /// A Local value is converted; anything else is taken at face value as UTC.
    ///
    /// The tempting spelling is ToUniversalTime for everything that is not already UTC, but that
    /// reads an Unspecified value as server local time, so the instant stored would depend on the
    /// machine's timezone and two servers in different regions would write different rows for the
    /// same input. Every value in this model comes from an injected clock as UTC; the ones that
    /// arrive Unspecified are defaults and model-bound values, which are meant as UTC too.
    /// </summary>
    private static DateTime Normalise(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

/// <inheritdoc cref="UtcDateTimeConverter"/>
public class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
    value => value == null ? null : Normalise(value.Value),
    value => value == null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
{
    /// <inheritdoc cref="UtcDateTimeConverter"/>
    private static DateTime Normalise(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
