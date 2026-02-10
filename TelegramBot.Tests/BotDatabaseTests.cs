namespace TelegramBot.Tests;

public class BotDatabaseTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private BotDatabase _db = null!;

    public BotDatabaseTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.sqlite");
    }

    public async Task InitializeAsync()
    {
        _db = new BotDatabase(_dbPath);
        bool result = await _db.InitializeAsync();
        Assert.True(result);
    }

    public Task DisposeAsync()
    {
        ((IDisposable)_db).Dispose();
        // Clear SQLite connection pool to release file locks
        System.Data.SQLite.SQLiteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { /* best effort cleanup */ }
        }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RegisterUser_And_HasUser_Works()
    {
        bool registered = await _db.RegisterUserIdAsync(1, DateTime.UtcNow);
        Assert.True(registered);

        bool exists = await _db.HasUserIdAsync(1);
        Assert.True(exists);

        bool notExists = await _db.HasUserIdAsync(999);
        Assert.False(notExists);
    }

    [Fact]
    public async Task AddConsumed_ReturnsRowInfo()
    {
        await _db.RegisterUserIdAsync(1, DateTime.UtcNow);
        var now = new DateTime(2024, 6, 15, 12, 0, 0);

        ConsumedRowInfo? row = await _db.AddConsumedAsync(1, "bread", 150, now);

        Assert.NotNull(row);
        Assert.Equal("bread", row.Text);
        Assert.Equal(150, row.Kcal);
        Assert.Equal(1, row.UserId);
    }

    [Fact]
    public async Task RemoveConsumed_ReturnsRemovedRow()
    {
        await _db.RegisterUserIdAsync(1, DateTime.UtcNow);
        var now = new DateTime(2024, 6, 15, 12, 0, 0);

        ConsumedRowInfo? added = await _db.AddConsumedAsync(1, "bread", 150, now);
        Assert.NotNull(added);

        ConsumedRowInfo? removed = await _db.RemoveConsumedAsync(added.Id, 1);
        Assert.NotNull(removed);
        Assert.Equal(added.Id, removed.Id);
    }

    [Fact]
    public async Task RemoveConsumed_WrongUser_ReturnsNull()
    {
        await _db.RegisterUserIdAsync(1, DateTime.UtcNow);
        await _db.RegisterUserIdAsync(2, DateTime.UtcNow);
        var now = new DateTime(2024, 6, 15, 12, 0, 0);

        ConsumedRowInfo? added = await _db.AddConsumedAsync(1, "bread", 150, now);
        Assert.NotNull(added);

        ConsumedRowInfo? removed = await _db.RemoveConsumedAsync(added.Id, 2);
        Assert.Null(removed);
    }

    [Fact]
    public async Task RemoveConsumed_ForceMode_IgnoresUserId()
    {
        await _db.RegisterUserIdAsync(1, DateTime.UtcNow);
        var now = new DateTime(2024, 6, 15, 12, 0, 0);

        ConsumedRowInfo? added = await _db.AddConsumedAsync(1, "bread", 150, now);
        Assert.NotNull(added);

        ConsumedRowInfo? removed = await _db.RemoveConsumedAsync(added.Id, null);
        Assert.NotNull(removed);
    }

    [Fact]
    public async Task GetConsumedCal_SumsCorrectly()
    {
        await _db.RegisterUserIdAsync(1, DateTime.UtcNow);
        var time1 = new DateTime(2024, 6, 15, 10, 0, 0);
        var time2 = new DateTime(2024, 6, 15, 14, 0, 0);

        await _db.AddConsumedAsync(1, "bread", 100, time1);
        await _db.AddConsumedAsync(1, "milk", 200, time2);

        var begin = new DateTime(2024, 6, 15, 0, 0, 0);
        double total = await _db.GetConsumedCalAsync(begin, null, 1);

        Assert.Equal(300, total);
    }

    [Fact]
    public async Task GetConsumedCal_WithDateRange_FiltersCorrectly()
    {
        await _db.RegisterUserIdAsync(1, DateTime.UtcNow);
        var time1 = new DateTime(2024, 6, 14, 10, 0, 0);
        var time2 = new DateTime(2024, 6, 15, 14, 0, 0);

        await _db.AddConsumedAsync(1, "bread", 100, time1);
        await _db.AddConsumedAsync(1, "milk", 200, time2);

        var begin = new DateTime(2024, 6, 15, 0, 0, 0);
        double total = await _db.GetConsumedCalAsync(begin, null, 1);

        Assert.Equal(200, total);
    }

    [Fact]
    public async Task GetStat_ReturnsAllRows()
    {
        await _db.RegisterUserIdAsync(1, DateTime.UtcNow);
        var now = new DateTime(2024, 6, 15, 12, 0, 0);

        await _db.AddConsumedAsync(1, "bread", 100, now);
        await _db.AddConsumedAsync(1, "milk", 200, now);

        List<ConsumedRowInfo> rows = await _db.GetStatAsync(null, null, 1);

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task TimezoneOffset_DefaultIsZero()
    {
        await _db.RegisterUserIdAsync(1, DateTime.UtcNow);
        int tz = await _db.GetUserTimezoneOffsetAsync(1);
        Assert.Equal(0, tz);
    }

    [Fact]
    public async Task SetAndGetTimezoneOffset_Works()
    {
        await _db.RegisterUserIdAsync(1, DateTime.UtcNow);
        bool success = await _db.SetUserTimezoneOffsetAsync(1, 5);
        Assert.True(success);

        int tz = await _db.GetUserTimezoneOffsetAsync(1);
        Assert.Equal(5, tz);
    }

    [Fact]
    public async Task SetAndGetMaxKcal_Works()
    {
        await _db.RegisterUserIdAsync(1, DateTime.UtcNow);

        double? initialLimit = await _db.GetMaxKcalAsync(1);
        Assert.Null(initialLimit);

        bool success = await _db.SetMaxKcalAsync(1, 2000);
        Assert.True(success);

        double? limit = await _db.GetMaxKcalAsync(1);
        Assert.Equal(2000, limit);
    }

    [Fact]
    public async Task Notifications_DefaultOff_CanToggle()
    {
        await _db.RegisterUserIdAsync(1, DateTime.UtcNow);

        List<long> notified = await _db.GetNotifiedUsersIds();
        Assert.Empty(notified);

        bool success = await _db.SetUserNotificationsEnabled(1, true);
        Assert.True(success);

        notified = await _db.GetNotifiedUsersIds();
        Assert.Single(notified);
        Assert.Equal(1, notified[0]);

        await _db.SetUserNotificationsEnabled(1, false);
        notified = await _db.GetNotifiedUsersIds();
        Assert.Empty(notified);
    }
}
