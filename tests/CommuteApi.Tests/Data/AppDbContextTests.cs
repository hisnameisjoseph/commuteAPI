using CommuteApi.Models;
using CommuteApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CommuteApi.Tests.Data;

public sealed class AppDbContextTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    private static Place NewPlace(string name = "Highpoint", TransportMode mode = TransportMode.Bus) => new()
    {
        Name = name,
        StopId = 12345,
        RouteId = 678,
        RouteType = mode,
    };

    [Fact]
    public async Task Adding_a_place_sets_both_timestamps_to_the_current_utc_time()
    {
        await using (var db = _database.CreateContext())
        {
            db.Places.Add(NewPlace());
            await db.SaveChangesAsync();
        }

        await using var read = _database.CreateContext();
        var place = await read.Places.SingleAsync();
        var now = _database.Clock.GetUtcNow().UtcDateTime;
        Assert.Equal(now, place.CreatedAtUtc);
        Assert.Equal(now, place.UpdatedAtUtc);
    }

    [Fact]
    public async Task Timestamps_read_back_from_the_database_are_marked_utc()
    {
        await using (var db = _database.CreateContext())
        {
            db.Places.Add(NewPlace());
            await db.SaveChangesAsync();
        }

        await using var read = _database.CreateContext();
        var place = await read.Places.SingleAsync();
        Assert.Equal(DateTimeKind.Utc, place.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, place.UpdatedAtUtc.Kind);
    }

    [Fact]
    public async Task Updating_a_place_moves_UpdatedAtUtc_but_not_CreatedAtUtc()
    {
        var created = _database.Clock.GetUtcNow().UtcDateTime;
        await using (var db = _database.CreateContext())
        {
            db.Places.Add(NewPlace());
            await db.SaveChangesAsync();
        }

        _database.Clock.Advance(TimeSpan.FromHours(3));
        await using (var db = _database.CreateContext())
        {
            var place = await db.Places.SingleAsync();
            place.LeadMinutes = 30;
            place.CreatedAtUtc = DateTime.UnixEpoch; // must be ignored
            await db.SaveChangesAsync();
        }

        await using var read = _database.CreateContext();
        var updated = await read.Places.SingleAsync();
        Assert.Equal(created, updated.CreatedAtUtc);
        Assert.Equal(created.AddHours(3), updated.UpdatedAtUtc);
    }

    [Fact]
    public async Task Place_names_are_unique()
    {
        await using var db = _database.CreateContext();
        db.Places.Add(NewPlace("Highpoint"));
        db.Places.Add(NewPlace("Highpoint"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(1)] // tram
    [InlineData(3)] // V/Line
    [InlineData(4)] // night bus
    public async Task The_database_rejects_modes_the_board_cannot_show(int routeType)
    {
        await using var db = _database.CreateContext();
        db.Places.Add(NewPlace(mode: (TransportMode)routeType));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Deleting_a_place_keeps_its_match_history_without_the_link()
    {
        int logId;
        await using (var db = _database.CreateContext())
        {
            var place = NewPlace();
            var log = new MatchLog
            {
                EventHash = new string('a', 64),
                EventStartUtc = DateTime.UtcNow,
                Place = place,
                Method = MatchMethod.Title,
            };
            db.MatchLogs.Add(log);
            await db.SaveChangesAsync();
            logId = log.Id;
        }

        await using (var db = _database.CreateContext())
        {
            db.Places.Remove(await db.Places.SingleAsync());
            await db.SaveChangesAsync();
        }

        await using var read = _database.CreateContext();
        var kept = await read.MatchLogs.SingleAsync(m => m.Id == logId);
        Assert.Null(kept.PlaceId);
        Assert.Equal(MatchMethod.Title, kept.Method);
        Assert.Equal(_database.Clock.GetUtcNow().UtcDateTime, kept.CreatedAtUtc);
    }

    public void Dispose() => _database.Dispose();
}
