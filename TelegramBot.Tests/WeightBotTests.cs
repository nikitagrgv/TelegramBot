using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Telegram.Bot.Types.Enums;

namespace TelegramBot.Tests;

public class WeightBotTests
{
    private readonly IBotDatabase _db = Substitute.For<IBotDatabase>();
    private readonly IBotClient _client = Substitute.For<IBotClient>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero));
    private readonly CancellationTokenSource _cts = new();
    private const long AdminId = 100;
    private const long UserId = 42;

    private WeightBot CreateBot() => new(_db, AdminId, _cts, _time);

    private void SetupRegisteredUser(long userId = UserId)
    {
        _db.HasUserIdAsync(userId).Returns(true);
        _db.GetUserTimezoneOffsetAsync(userId).Returns(0);
        _db.GetConsumedCalAsync(Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), userId).Returns(0.0);
        _db.GetMaxKcalAsync(userId).Returns((double?)null);
    }

    [Fact]
    public void IsAdmin_CorrectId_ReturnsTrue()
    {
        var bot = CreateBot();
        Assert.True(bot.IsAdmin(AdminId));
        Assert.False(bot.IsAdmin(UserId));
    }

    [Fact]
    public async Task DispatchUserMessage_HelpCommand_SendsHelpMessage()
    {
        var bot = CreateBot();
        await bot.DispatchUserMessageAsync("start", "", UserId, _client, CancellationToken.None);

        await _client.Received(1).SendMessageAsync(UserId, Arg.Is<string>(s => s.Contains("Add a consumed product")),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchUserMessage_UnknownCommand_SendsErrorMessage()
    {
        var bot = CreateBot();
        await bot.DispatchUserMessageAsync("foobar", "", UserId, _client, CancellationToken.None);

        await _client.Received(1).SendMessageAsync(UserId,
            Arg.Is<string>(s => s.Contains("Unknown command: foobar")),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ParseAndDispatch_InvalidInput_SendsDidntUnderstand()
    {
        var bot = CreateBot();
        await bot.ParseAndDispatchUserMessageAsync(UserId, "!@#$", _client, CancellationToken.None);

        await _client.Received(1).SendMessageAsync(UserId,
            Arg.Is<string>(s => s.Contains("didn't understand")),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddConsumed_ValidArgs_AddsAndSendsConfirmation()
    {
        SetupRegisteredUser();
        var bot = CreateBot();
        var row = new ConsumedRowInfo(1, UserId, new DateTime(2024, 6, 15, 12, 0, 0), "bread", 150);
        _db.AddConsumedAsync(UserId, "bread", 150, Arg.Any<DateTime>()).Returns(row);

        await bot.AddConsumedAsync("bread, 150", UserId, _client, CancellationToken.None);

        await _db.Received(1).AddConsumedAsync(UserId, "bread", 150, Arg.Any<DateTime>());
        await _client.Received().SendMessageAsync(UserId,
            Arg.Is<string>(s => s.Contains("Added") && s.Contains("bread")),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddConsumed_InvalidArgs_SendsError()
    {
        var bot = CreateBot();
        await bot.AddConsumedAsync("", UserId, _client, CancellationToken.None);

        await _client.Received(1).SendMessageAsync(UserId,
            Arg.Is<string>(s => s.Contains("didn't understand your 'add' command")),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddConsumed_DatabaseFails_SendsDbError()
    {
        var bot = CreateBot();
        _db.AddConsumedAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<double>(), Arg.Any<DateTime>())
            .Returns((ConsumedRowInfo?)null);
        _db.GetUserTimezoneOffsetAsync(UserId).Returns(0);

        await bot.AddConsumedAsync("bread, 150", UserId, _client, CancellationToken.None);

        await _client.Received(1).SendMessageAsync(UserId,
            Arg.Is<string>(s => s.Contains("Database error")),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveConsumed_ValidId_RemovesAndSendsConfirmation()
    {
        SetupRegisteredUser();
        var bot = CreateBot();
        var row = new ConsumedRowInfo(5, UserId, new DateTime(2024, 6, 15, 12, 0, 0), "bread", 150);
        _db.RemoveConsumedAsync(5, UserId).Returns(row);

        await bot.RemoveConsumedAsync("5", UserId, false, _client, CancellationToken.None);

        await _db.Received(1).RemoveConsumedAsync(5, UserId);
        await _client.Received().SendMessageAsync(UserId,
            Arg.Is<string>(s => s.Contains("Removed")),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveConsumed_InvalidId_SendsError()
    {
        var bot = CreateBot();
        await bot.RemoveConsumedAsync("abc", UserId, false, _client, CancellationToken.None);

        await _client.Received(1).SendMessageAsync(UserId,
            Arg.Is<string>(s => s.Contains("Invalid id")),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveConsumed_ForceMode_PassesNullUserId()
    {
        SetupRegisteredUser();
        var bot = CreateBot();
        var row = new ConsumedRowInfo(5, UserId, new DateTime(2024, 6, 15, 12, 0, 0), "bread", 150);
        _db.RemoveConsumedAsync(5, null).Returns(row);

        await bot.RemoveConsumedAsync("5", UserId, true, _client, CancellationToken.None);

        await _db.Received(1).RemoveConsumedAsync(5, null);
    }

    [Fact]
    public async Task PrintShortStat_WithLimit_ShowsPercentage()
    {
        var bot = CreateBot();
        _db.GetUserTimezoneOffsetAsync(UserId).Returns(0);
        _db.GetConsumedCalAsync(Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), UserId).Returns(1500.0);
        _db.GetMaxKcalAsync(UserId).Returns(2000.0);

        await bot.PrintShortStatAsync(UserId, _client, CancellationToken.None);

        await _client.Received(1).SendMessageAsync(UserId,
            Arg.Is<string>(s => s.Contains("1500") && s.Contains("2000") && s.Contains("75 %") && s.Contains("left")),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PrintShortStat_OverLimit_ShowsOvereat()
    {
        var bot = CreateBot();
        _db.GetUserTimezoneOffsetAsync(UserId).Returns(0);
        _db.GetConsumedCalAsync(Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), UserId).Returns(2500.0);
        _db.GetMaxKcalAsync(UserId).Returns(2000.0);

        await bot.PrintShortStatAsync(UserId, _client, CancellationToken.None);

        await _client.Received(1).SendMessageAsync(UserId,
            Arg.Is<string>(s => s.Contains("overeat")),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PrintShortStat_NoLimit_ShowsNoLimitSet()
    {
        var bot = CreateBot();
        _db.GetUserTimezoneOffsetAsync(UserId).Returns(0);
        _db.GetConsumedCalAsync(Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), UserId).Returns(500.0);
        _db.GetMaxKcalAsync(UserId).Returns((double?)null);

        await bot.PrintShortStatAsync(UserId, _client, CancellationToken.None);

        await _client.Received(1).SendMessageAsync(UserId,
            Arg.Is<string>(s => s.Contains("no limit set")),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdminCommand_Kill_NonAdmin_DoesNotShutdown()
    {
        var bot = CreateBot();
        await bot.DispatchUserMessageAsync("kill", "", UserId, _client, CancellationToken.None);

        // Non-admin gets "Unknown command" instead of shutdown
        await _client.Received(1).SendMessageAsync(UserId,
            Arg.Is<string>(s => s.Contains("Unknown command")),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdminCommand_Kill_Admin_SendsShutdown()
    {
        var bot = CreateBot();
        await bot.DispatchUserMessageAsync("kill", "", AdminId, _client, CancellationToken.None);

        await _client.Received(1).SendMessageAsync(AdminId,
            Arg.Is<string>(s => s.Contains("Shutdown")),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterUser_NewUser_SendsWelcome()
    {
        var bot = CreateBot();
        _db.HasUserIdAsync(UserId).Returns(false);
        _db.RegisterUserIdAsync(UserId, Arg.Any<DateTime>()).Returns(true);

        bool result = await bot.RegisterUserIfNotRegisteredAsync(UserId, _client, CancellationToken.None);

        Assert.True(result);
        await _client.Received(1).SendMessageAsync(UserId,
            Arg.Is<string>(s => s.Contains("registered")),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterUser_ExistingUser_ReturnsTrueWithoutMessage()
    {
        var bot = CreateBot();
        _db.HasUserIdAsync(UserId).Returns(true);

        bool result = await bot.RegisterUserIfNotRegisteredAsync(UserId, _client, CancellationToken.None);

        Assert.True(result);
        await _client.DidNotReceive().SendMessageAsync(Arg.Any<long>(), Arg.Any<string>(),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryNotify_AtCorrectHour_SendsNotification()
    {
        var bot = CreateBot();
        _db.GetUserTimezoneOffsetAsync(UserId).Returns(0);
        _db.GetConsumedCalAsync(Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), UserId).Returns(500.0);
        _db.GetMaxKcalAsync(UserId).Returns((double?)null);

        var now = new DateTime(2024, 6, 15, 11, 0, 0); // 11:00 UTC, timezone 0

        await bot.TryNotify(_client, now, UserId, CancellationToken.None);

        await _client.Received(1).SendMessageAsync(UserId, Arg.Any<string>(),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryNotify_AtWrongHour_DoesNotSend()
    {
        var bot = CreateBot();
        _db.GetUserTimezoneOffsetAsync(UserId).Returns(0);

        var now = new DateTime(2024, 6, 15, 13, 0, 0); // 13:00 UTC

        await bot.TryNotify(_client, now, UserId, CancellationToken.None);

        await _client.DidNotReceive().SendMessageAsync(Arg.Any<long>(), Arg.Any<string>(),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryNotify_WithinCooldown_DoesNotSend()
    {
        var bot = CreateBot();
        _db.GetUserTimezoneOffsetAsync(UserId).Returns(0);
        _db.GetConsumedCalAsync(Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), UserId).Returns(0.0);
        _db.GetMaxKcalAsync(UserId).Returns((double?)null);

        var first = new DateTime(2024, 6, 15, 11, 0, 0);
        await bot.TryNotify(_client, first, UserId, CancellationToken.None);

        _client.ClearReceivedCalls();

        // 30 mins later - within cooldown
        var second = new DateTime(2024, 6, 15, 11, 30, 0);
        await bot.TryNotify(_client, second, UserId, CancellationToken.None);

        await _client.DidNotReceive().SendMessageAsync(Arg.Any<long>(), Arg.Any<string>(),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryNotify_AfterCooldown_SendsAgain()
    {
        var bot = CreateBot();
        _db.GetUserTimezoneOffsetAsync(UserId).Returns(0);
        _db.GetConsumedCalAsync(Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), UserId).Returns(0.0);
        _db.GetMaxKcalAsync(UserId).Returns((double?)null);

        var first = new DateTime(2024, 6, 15, 11, 0, 0);
        await bot.TryNotify(_client, first, UserId, CancellationToken.None);

        _client.ClearReceivedCalls();

        // 16:00 same day - after 1.5h cooldown
        var second = new DateTime(2024, 6, 15, 16, 0, 0);
        await bot.TryNotify(_client, second, UserId, CancellationToken.None);

        await _client.Received(1).SendMessageAsync(UserId, Arg.Any<string>(),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryNotify_WithTimezoneOffset_UsesLocalTime()
    {
        var bot = CreateBot();
        _db.GetUserTimezoneOffsetAsync(UserId).Returns(3); // UTC+3
        _db.GetConsumedCalAsync(Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), UserId).Returns(0.0);
        _db.GetMaxKcalAsync(UserId).Returns((double?)null);

        // 8:00 UTC = 11:00 local time for UTC+3
        var now = new DateTime(2024, 6, 15, 8, 0, 0);

        await bot.TryNotify(_client, now, UserId, CancellationToken.None);

        await _client.Received(1).SendMessageAsync(UserId, Arg.Any<string>(),
            Arg.Any<ParseMode?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void GetUserDayBeginUtc_TimezoneZero_ReturnsMidnightUtc()
    {
        var bot = CreateBot();
        DateTime dayBegin = bot.GetUserDayBeginUtc(0);

        Assert.Equal(new DateTime(2024, 6, 15, 0, 0, 0), dayBegin);
    }

    [Fact]
    public void GetUserDayBeginUtc_PositiveTimezone_AdjustsCorrectly()
    {
        var bot = CreateBot();
        // At 12:00 UTC, user with +5 sees 17:00 local. Day begin local = 00:00 local = 19:00 UTC previous day
        DateTime dayBegin = bot.GetUserDayBeginUtc(5);

        Assert.Equal(new DateTime(2024, 6, 14, 19, 0, 0), dayBegin);
    }

    [Fact]
    public void DbRowToUserStringRow_FormatsCorrectly()
    {
        var row = new ConsumedRowInfo(1, 42, new DateTime(2024, 6, 15, 12, 30, 0), "bread", 150.0);
        var result = WeightBot.DbRowToUserStringRow(row, 3, "HH:mm");

        Assert.Equal("1", result.Id);
        Assert.Equal("42", result.UserId);
        Assert.Equal("bread", result.Text);
        Assert.Equal(150.0.ToString("F"), result.Kcal);
        Assert.Equal("15:30", result.Date); // 12:30 UTC + 3h = 15:30
    }

    [Fact]
    public void FromDatabaseToUserTimeFormat_AppliesTimezone()
    {
        var date = new DateTime(2024, 6, 15, 10, 0, 0);
        string result = WeightBot.FromDatabaseToUserTimeFormat(date, 5, "HH:mm");
        Assert.Equal("15:00", result);
    }

    [Theory]
    [InlineData("add", "bread, 100")]
    [InlineData("добавить", "каша, 50")]
    [InlineData("доб", "хлеб 200")]
    [InlineData("д", "50")]
    public async Task DispatchUserMessage_AddAliases_CallsAddConsumed(string cmd, string args)
    {
        SetupRegisteredUser();
        var bot = CreateBot();
        _db.AddConsumedAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<double>(), Arg.Any<DateTime>())
            .Returns(new ConsumedRowInfo(1, UserId, DateTime.UtcNow, "test", 100));

        await bot.DispatchUserMessageAsync(cmd, args, UserId, _client, CancellationToken.None);

        await _db.Received(1).AddConsumedAsync(UserId, Arg.Any<string>(), Arg.Any<double>(), Arg.Any<DateTime>());
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("delete")]
    [InlineData("удалить")]
    [InlineData("дел")]
    public async Task DispatchUserMessage_RemoveAliases_CallsRemoveConsumed(string cmd)
    {
        SetupRegisteredUser();
        var bot = CreateBot();
        var row = new ConsumedRowInfo(5, UserId, DateTime.UtcNow, "bread", 100);
        _db.RemoveConsumedAsync(5, UserId).Returns(row);

        await bot.DispatchUserMessageAsync(cmd, "5", UserId, _client, CancellationToken.None);

        await _db.Received(1).RemoveConsumedAsync(5, UserId);
    }
}
