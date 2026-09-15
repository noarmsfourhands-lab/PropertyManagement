using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.Infrastructure.Tests;

/// <summary>
/// A real relational database for one test, held in memory by SQLite.
///
/// These tests exist to check the things a rule test cannot: that the model actually produces a
/// schema, that the queries translate, and that the concurrency tokens do what they are configured
/// to do. SQLite is used because it enforces keys, unique indexes and concurrency the same way a
/// server does, while needing nothing installed. The application itself runs on SQL Server.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<PropertyManagementDbContext> _options;

    public TestDatabase()
    {
        // The in-memory database lives exactly as long as this connection.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<PropertyManagementDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    /// <summary>
    /// A fresh context over the same database. Two of these stand in for two people working at
    /// once, each with their own change tracker.
    /// </summary>
    public PropertyManagementDbContext CreateContext() => new(_options);

    /// <summary>The open connection this database lives on, for tests that build their own container.</summary>
    public SqliteConnection Connection => _connection;

    public void Dispose() => _connection.Dispose();
}
