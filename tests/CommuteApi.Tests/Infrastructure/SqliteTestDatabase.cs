using CommuteApi.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace CommuteApi.Tests.Infrastructure;

/// <summary>A throwaway relational database with a controllable clock, for data-layer tests.</summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public SqliteTestDatabase()
    {
        _connection.Open();
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 12, 22, 0, 0, TimeSpan.Zero));

    /// <summary>A fresh context each time, so reads come from the database, not the change tracker.</summary>
    public AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new TimestampInterceptor(Clock))
            .Options);

    public void Dispose() => _connection.Dispose();
}
