using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace PropertyManagement.Infrastructure.Persistence;

/// <summary>
/// Tells apart the database failures a service is expected to survive.
///
/// Every one of these is a race: the service checks, then writes, and something changed in between.
/// The check is still worth having, because it produces the good message almost every time; the
/// constraint is what actually guarantees the rule, and catching it here is what turns the losing
/// request into a sentence rather than a five hundred.
///
/// Matched on the error number rather than the message. SQL Server localises its messages, so
/// reading them would work on an English installation and quietly stop working on any other. The
/// message fallback covers providers that report no number, which is what the test database does.
/// </summary>
public static class DatabaseErrors
{
    /// <summary>A violated unique index and a violated unique constraint.</summary>
    private const int DuplicateKeyError = 2601;

    private const int UniqueConstraintError = 2627;

    /// <summary>A violated foreign key or check constraint. SQL Server reports both as 547.</summary>
    private const int ConstraintConflictError = 547;

    public static bool IsUniqueViolation(DbUpdateException exception) =>
        Matches(exception, [DuplicateKeyError, UniqueConstraintError], "unique");

    public static bool IsConstraintConflict(DbUpdateException exception) =>
        Matches(exception, [ConstraintConflictError], "constraint");

    private static bool Matches(DbUpdateException? exception, int[] numbers, string word) =>
        exception?.InnerException switch
        {
            SqlException sql => numbers.Contains(sql.Number),
            { } inner => inner.Message.Contains(word, StringComparison.OrdinalIgnoreCase)
                || inner.Message.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
}
