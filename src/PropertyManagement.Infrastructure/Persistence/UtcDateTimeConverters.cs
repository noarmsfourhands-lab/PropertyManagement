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
    value => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(),
    value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

/// <inheritdoc cref="UtcDateTimeConverter"/>
public class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
    value => value == null
        ? null
        : value.Value.Kind == DateTimeKind.Utc ? value : value.Value.ToUniversalTime(),
    value => value == null
        ? null
        : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
