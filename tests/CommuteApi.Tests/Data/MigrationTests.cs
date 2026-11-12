using CommuteApi.Data;
using Microsoft.EntityFrameworkCore;

namespace CommuteApi.Tests.Data;

public sealed class MigrationTests
{
    /// <summary>
    /// Fails when someone changes the model but forgets to add a migration. Uses the real
    /// SQL Server provider, but never opens a connection.
    /// </summary>
    [Fact]
    public void Every_model_change_has_a_migration()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=unused")
            .Options;
        using var db = new AppDbContext(options);

        Assert.False(
            db.Database.HasPendingModelChanges(),
            "The model has changed since the last migration. Run: dotnet ef migrations add <Name> --project src/CommuteApi --output-dir Data/Migrations");
    }
}
