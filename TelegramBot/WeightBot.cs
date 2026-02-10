using System.Globalization;
using System.Text.RegularExpressions;

namespace TelegramBot;

using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

public partial class WeightBot
{
    private const string ShortUserTimeFormat = "HH:mm";
    private const string LongUserTimeFormat = "dd MMM HH:mm";

    private static readonly Regex ParseCommandRegex = GetParseCommandRegex();
    private static readonly Regex AddConsumedRegex = GetAddConsumedRegex();

    private readonly long _adminId;
    private readonly CancellationTokenSource _cancelTokenSource;
    private readonly IBotDatabase _database;
    private readonly TimeProvider _timeProvider;

    private IBotClient _botClient = null!;

    public WeightBot(IBotDatabase database, long adminId, CancellationTokenSource cancelTokenSource,
        TimeProvider? timeProvider = null)
    {
        _database = database;
        _adminId = adminId;
        _cancelTokenSource = cancelTokenSource;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task Run(string token)
    {
        var telegramClient = new TelegramBotClient(token);
        _botClient = new TelegramBotClientAdapter(telegramClient);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [],
        };

        User me = await telegramClient.GetMe(cancellationToken: _cancelTokenSource.Token);
        Console.WriteLine($"Start listening @{me.Username}");


        IEnumerable<BotCommand> commands =
        [
            new("start", "Get the help"),
            new("stat", "Print consumed kcal by the current day"),
            new("daystat", "Print all consumed products by the current day"),
            new("longstat", "Print all consumed products by all the time"),
        ];
        await telegramClient.SetMyCommands(commands, cancellationToken: _cancelTokenSource.Token);

        Task notify = NotifyLoop(_botClient, _cancelTokenSource.Token);
        await telegramClient.ReceiveAsync(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: _cancelTokenSource.Token);
        await notify;
    }

    private readonly Dictionary<long, DateTime> _notifies = new();
    private readonly TimeSpan _notifyCheckPeriod = TimeSpan.FromMinutes(10);
    private readonly TimeSpan _notifyCooldownPeriod = TimeSpan.FromHours(1) + TimeSpan.FromMinutes(30);

    internal async Task NotifyLoop(IBotClient botClient, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
                List<long> notifiedUsersIds = await _database.GetNotifiedUsersIds();

                foreach (long userId in notifiedUsersIds)
                {
                    try
                    {
                        await TryNotify(botClient, now, userId, cancellationToken);
                    }
                    catch (TaskCanceledException _)
                    {
                        break;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }

                await Task.Delay(_notifyCheckPeriod, cancellationToken);
            }
        }
        catch (TaskCanceledException _)
        {
        }

        Console.WriteLine("Notify finished");
    }

    internal async Task TryNotify(IBotClient botClient, DateTime now, long userId, CancellationToken cancellationToken)
    {
        if (_notifies.TryGetValue(userId, out DateTime prevNotify))
        {
            if (now - prevNotify < _notifyCooldownPeriod)
            {
                return;
            }
        }

        int timezone = await _database.GetUserTimezoneOffsetAsync(userId);
        DateTime localTime = now.AddHours(timezone);
        int hour = localTime.Hour;
        bool needNotify = hour == 11 || hour == 16;

        if (!needNotify)
            return;

        _notifies[userId] = now;
        await PrintShortStatAsync(userId, botClient, cancellationToken);
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update,
        CancellationToken cancellationToken)
    {
        if (update.Message is { } message)
        {
            await HandleMessageAsync(message, _botClient, cancellationToken);
        }

        if (update.CallbackQuery is { } callbackQuery)
        {
            await HandleCallbackQueryAsync(callbackQuery, _botClient, cancellationToken);
        }
    }

    internal async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, IBotClient botClient,
        CancellationToken cancellationToken)
    {
        if (callbackQuery.From is not { Id: var userId }) return;

        if (!await RegisterUserIfNotRegisteredAsync(userId, botClient, cancellationToken))
        {
            return;
        }

        if (callbackQuery.Data is not { } queryData) return;

        await DispatchCallbackQueryAsync(userId, queryData, botClient, cancellationToken);
    }

    private async Task DispatchCallbackQueryAsync(long userId, string queryData, IBotClient botClient,
        CancellationToken cancellationToken)
    {
        await ParseAndDispatchUserMessageAsync(userId, queryData, botClient, cancellationToken);
    }

    internal async Task HandleMessageAsync(Message message, IBotClient botClient,
        CancellationToken cancellationToken)
    {
        if (message.From is not { Id: var userId }) return;

        if (!await RegisterUserIfNotRegisteredAsync(userId, botClient, cancellationToken))
        {
            return;
        }

        if (message.Text is not { } userText) return;

        await ParseAndDispatchUserMessageAsync(userId, userText.Trim(), botClient, cancellationToken);
    }

    internal async Task ParseAndDispatchUserMessageAsync(long userId, string userText, IBotClient botClient,
        CancellationToken cancellationToken)
    {
        Match m = ParseCommandRegex.Match(userText);

        if (!m.Success)
        {
            const string invalidCommandMessage =
                "Sorry, I didn't understand that. Type /start to see a list of available commands.";
            await botClient.SendMessageAsync(userId, invalidCommandMessage, cancellationToken: cancellationToken);
            return;
        }

        string cmd = m.Groups["cmd"].Value;
        string args = m.Groups["args"].Value;

        await DispatchUserMessageAsync(cmd, args, userId, botClient, cancellationToken);
    }

    internal async Task DispatchUserMessageAsync(string cmd, string args, long userId, IBotClient botClient,
        CancellationToken cancellationToken)
    {
        string cmdLower = cmd.ToLower();

        if (IsAdmin(userId))
        {
            switch (cmdLower)
            {
                case "notified":
                    await PrintNotifiedAsync(userId, botClient, cancellationToken);
                    return;
                case "removeforce":
                    await RemoveConsumedAsync(args, userId, force: true, botClient, cancellationToken);
                    return;
                case "superstat":
                    await PrintSuperStatAsync(userId, botClient, cancellationToken);
                    return;
                case "kill":
                    await ShutdownBot(userId, botClient, cancellationToken);
                    return;
            }
        }

        switch (cmdLower)
        {
            case "start":
            case "help":
                await PrintHelpAsync(userId, botClient, cancellationToken);
                return;
            case "add":
            case "добавить":
            case "адд":
            case "доб":
            case "д":
                await AddConsumedAsync(args, userId, botClient, cancellationToken);
                return;
            case "remove":
            case "delete":
            case "удал":
            case "удали":
            case "удалить":
            case "делит":
            case "дел":
                await RemoveConsumedAsync(args, userId, force: false, botClient, cancellationToken);
                return;
            case "stat":
            case "стат":
                await PrintShortStatAsync(userId, botClient, cancellationToken);
                return;
            case "daystat":
            case "дейстат":
                await PrintDayStatAsync(userId, botClient, cancellationToken);
                return;
            case "longstat":
            case "лонгстат":
                await PrintAllStatAsync(userId, botClient, cancellationToken);
                return;
            case "timezone":
            case "зона":
                await SetUserTimezoneOffsetAsync(args, userId, botClient, cancellationToken);
                return;
            case "limit":
            case "лимит":
                await SetMaxKcalAsync(args, userId, botClient, cancellationToken);
                return;
            case "notifyon":
                await SetNotificationsEnabled(userId, botClient, true, cancellationToken);
                return;
            case "notifyoff":
                await SetNotificationsEnabled(userId, botClient, false, cancellationToken);
                return;
            default:
                string message = $"Unknown command: {cmd}. Type /start to see a list of available commands.";
                await botClient.SendMessageAsync(userId, message, cancellationToken: cancellationToken);
                return;
        }
    }

    private async Task ShutdownBot(long userId, IBotClient botClient,
        CancellationToken cancellationToken)
    {
        if (!IsAdmin(userId))
        {
            return;
        }

        string message = "Shutdown...";
        await botClient.SendMessageAsync(userId, message, cancellationToken: cancellationToken);
        _cancelTokenSource.CancelAfter(1000);
    }

    private async Task PrintHelpAsync(long userId, IBotClient botClient,
        CancellationToken cancellationToken)
    {
        await botClient.SendMessageAsync(userId, GetHelpMessage(), cancellationToken: cancellationToken);
    }

    internal async Task AddConsumedAsync(string args, long userId, IBotClient botClient,
        CancellationToken cancellationToken)
    {
        Match m = AddConsumedRegex.Match(args);

        if (!m.Success)
        {
            string invalidCommandMessage =
                $"Sorry, I didn't understand your 'add' command. Invalid arguments: '{args}'. Type /start to see a list of available commands.";
            await botClient.SendMessageAsync(userId, invalidCommandMessage, cancellationToken: cancellationToken);
            return;
        }

        string name = m.Groups["name"].Value;
        string kcalString = m.Groups["kcal"].Value;

        double kcal = 0;
        if (!string.IsNullOrEmpty(kcalString) && !TryParseDouble(kcalString, out kcal))
        {
            string invalidCommandMessage =
                $"Sorry, I didn't understand your 'add' command. Invalid kcal number: '{kcalString}'. Type /start to see a list of available commands.";
            await botClient.SendMessageAsync(userId, invalidCommandMessage, cancellationToken: cancellationToken);
            return;
        }

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

        ConsumedRowInfo? row = await _database.AddConsumedAsync(userId, name, kcal, now);

        if (row == null)
        {
            string errorMessage =
                $"Database error. Can't add the row. User ID: '{userId}', name: '{name}', kcal: '{kcal}'";
            await botClient.SendMessageAsync(userId, errorMessage, cancellationToken: cancellationToken);
            return;
        }

        int timeZone = await _database.GetUserTimezoneOffsetAsync(userId);

        string message = $"""
                          🍽️ Added
                          🍽️ {row.Text}
                          🔥 {row.Kcal} kcal
                          📅 {FromDatabaseToUserTimeFormat(row.Date, timeZone, LongUserTimeFormat)}
                          🆔 {row.Id}
                          """;
        await botClient.SendMessageAsync(userId, message, cancellationToken: cancellationToken);

        await PrintShortStatAsync(userId, botClient, cancellationToken);
    }

    internal async Task RemoveConsumedAsync(string args, long userId, bool force, IBotClient botClient,
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(args.Trim(), out long consumedId) || consumedId < 0)
        {
            string invalidCommandMessage =
                $"Sorry, I didn't understand your 'remove' command. Invalid id: '{args}'. Type /start to see a list of available commands.";
            await botClient.SendMessageAsync(userId, invalidCommandMessage, cancellationToken: cancellationToken);
            return;
        }

        ConsumedRowInfo? row = await _database.RemoveConsumedAsync(consumedId, force ? null : userId);

        if (row == null)
        {
            string errorMessage =
                $"Database error. Can't remove the row. User ID: '{userId}', consumed ID: '{consumedId}'";
            await botClient.SendMessageAsync(userId, errorMessage, cancellationToken: cancellationToken);
            return;
        }

        int timeZone = await _database.GetUserTimezoneOffsetAsync(userId);

        string message = $"""
                          🗑️ Removed
                          🍽 {row.Text}
                          🔥 {row.Kcal} kcal
                          📅 {FromDatabaseToUserTimeFormat(row.Date, timeZone, LongUserTimeFormat)}
                          🆔 {row.Id}
                          """;
        await botClient.SendMessageAsync(userId, message, cancellationToken: cancellationToken);

        await PrintShortStatAsync(userId, botClient, cancellationToken);
    }

    internal async Task PrintShortStatAsync(long userId,
        IBotClient botClient,
        CancellationToken cancellationToken)
    {
        int timeZone = await _database.GetUserTimezoneOffsetAsync(userId);
        DateTime dayBeginUtc = GetUserDayBeginUtc(timeZone);

        double consumed = await _database.GetConsumedCalAsync(dayBeginUtc, null, userId);
        double? limit = await _database.GetMaxKcalAsync(userId);

        string message;
        if (limit != null)
        {
            int consumedPercents = (int)(consumed / limit * 100);
            message = $"🔥 Consumed Today: {consumed} / {limit} kcal ({consumedPercents} %)\n";
            if (consumed < limit)
            {
                message += $"✅ {limit - consumed} kcal left\n";
            }
            else
            {
                message += $"❌ {consumed - limit} kcal overeat!\n";
            }
        }
        else
        {
            message = $"🔥 Consumed Today: {consumed} kcal (no limit set)\n";
        }

        await botClient.SendMessageAsync(userId, message, cancellationToken: cancellationToken);
    }

    private async Task PrintDayStatAsync(long userId,
        IBotClient botClient,
        CancellationToken cancellationToken)
    {
        int timeZone = await _database.GetUserTimezoneOffsetAsync(userId);
        DateTime dayBeginUtc = GetUserDayBeginUtc(timeZone);
        await PrintStatAsync(dayBeginUtc, null, "📊 Day Statistics", ShortUserTimeFormat, timeZone, userId, botClient,
            cancellationToken);
    }

    private async Task PrintAllStatAsync(long userId,
        IBotClient botClient,
        CancellationToken cancellationToken)
    {
        int timeZone = await _database.GetUserTimezoneOffsetAsync(userId);
        await PrintStatAsync(null, null, "📈 Total Statistics", LongUserTimeFormat, timeZone, userId, botClient,
            cancellationToken);
    }

    private async Task PrintStatAsync(DateTime? begin, DateTime? end, string titleMessage, string timeFormat,
        int timeZone, long userId,
        IBotClient botClient,
        CancellationToken cancellationToken)
    {
        List<ConsumedRowInfo> dbRows = await _database.GetStatAsync(begin, end, userId);

        const string idRowName = "ID";
        const string nameRowName = "Name";
        const string kcalRowName = "Kcal";
        const string timeRowName = "Time";

        List<ConsumedRowInfoStrings> strRows = dbRows
            .Select(row => DbRowToUserStringRow(row, timeZone, timeFormat))
            .ToList();
        int dateSize = timeFormat.Length;
        int kcalSize = kcalRowName.Length;
        int idSize = idRowName.Length;
        foreach (ConsumedRowInfoStrings row in strRows)
        {
            kcalSize = Int32.Max(kcalSize, row.Kcal.Length);
            idSize = Int32.Max(idSize, row.Id.Length);
        }

        double consumedToday = dbRows.Any() ? dbRows.Sum(row => row.Kcal) : 0;

        int tableLengthBudget = strRows.Count >= 5 ? 36 : 30;
        int nameSize = int.Max(6, tableLengthBudget - kcalSize - dateSize - idSize);

        string rowFormat = $"{{0, {idSize}}}| {{1, -{nameSize}}}| {{2, {kcalSize}}}| {{3, {dateSize}}}\n";

        string message = "";
        message += titleMessage + '\n';
        message += $"🌍 Time Zone: {timeZone:+#;-#;0}\n";
        message += $"🔥 Consumed: {consumedToday} kcal\n";

        message += "<pre>";
        message += string.Format(rowFormat, idRowName, nameRowName, kcalRowName, timeRowName);

        foreach (ConsumedRowInfoStrings row in strRows)
        {
            bool firstRow = true;
            foreach (string chunk in Utils.SplitStringByChunks(row.Text, nameSize))
            {
                if (firstRow)
                {
                    message += string.Format(rowFormat, row.Id, chunk, row.Kcal, row.Date);
                    firstRow = false;
                }
                else
                {
                    message += string.Format(rowFormat, string.Empty, chunk, string.Empty, string.Empty);
                }
            }
        }

        message += "</pre>";

        await botClient.SendMessageAsync(userId, message, ParseMode.Html, cancellationToken);
    }

    private async Task PrintSuperStatAsync(long userId,
        IBotClient botClient,
        CancellationToken cancellationToken)
    {
        if (!IsAdmin(userId))
        {
            return;
        }

        List<ConsumedRowInfo> dbRows = await _database.GetStatAsync(null, null, null);

        const string idRowName = "ID";
        const string userRowName = "User";
        const string nameRowName = "Name";
        const string kcalRowName = "Kcal";
        const string timeRowName = "Time";

        const string timeFormat = LongUserTimeFormat;

        List<ConsumedRowInfoStrings> strRows = dbRows
            .Select(row => DbRowToUserStringRow(row, 0, timeFormat))
            .ToList();
        int idSize = idRowName.Length;
        int userIdSize = userRowName.Length;
        int kcalSize = kcalRowName.Length;
        int dateSize = timeRowName.Length;
        foreach (ConsumedRowInfoStrings row in strRows)
        {
            idSize = Int32.Max(idSize, row.Id.Length);
            userIdSize = Int32.Max(userIdSize, row.UserId.Length);
            kcalSize = Int32.Max(kcalSize, row.Kcal.Length);
            dateSize = Int32.Max(dateSize, row.Date.Length);
        }

        int tableLengthBudget = strRows.Count >= 5 ? 36 : 30;
        int nameSize = int.Max(6, tableLengthBudget - kcalSize - dateSize - idSize - userIdSize);

        string rowFormat =
            $"{{0, {idSize}}}| {{1, {userIdSize}}}| {{2, -{nameSize}}}| {{3, {kcalSize}}}| {{4, {dateSize}}}\n";

        string message = "";

        message += "<pre>";
        message += string.Format(rowFormat, idRowName, userRowName, nameRowName, kcalRowName, timeRowName);

        foreach (ConsumedRowInfoStrings row in strRows)
        {
            bool firstRow = true;
            foreach (string chunk in Utils.SplitStringByChunks(row.Text, nameSize))
            {
                if (firstRow)
                {
                    message += string.Format(rowFormat, row.Id, row.UserId, chunk, row.Kcal, row.Date);
                    firstRow = false;
                }
                else
                {
                    message += string.Format(rowFormat, string.Empty, string.Empty, chunk, string.Empty, string.Empty);
                }
            }
        }

        message += "</pre>";

        await botClient.SendMessageAsync(userId, message, ParseMode.Html, cancellationToken);
    }

    private async Task PrintNotifiedAsync(long userId,
        IBotClient botClient,
        CancellationToken cancellationToken)
    {
        if (!IsAdmin(userId))
        {
            return;
        }

        List<long> ids = await _database.GetNotifiedUsersIds();
        string message = "";
        foreach (long id in ids)
        {
            message += $"{id}\n";
        }

        await botClient.SendMessageAsync(userId, message, cancellationToken: cancellationToken);
    }

    private async Task SetUserTimezoneOffsetAsync(string args, long userId, IBotClient botClient,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(args.Trim(), out int timezone) || timezone < 0)
        {
            string invalidCommandMessage =
                $"Sorry, I didn't understand your 'timezone' command. Invalid offset: '{args}'. Type /start to see a list of available commands.";
            await botClient.SendMessageAsync(userId, invalidCommandMessage, cancellationToken: cancellationToken);
            return;
        }

        bool success = await _database.SetUserTimezoneOffsetAsync(userId, timezone);
        if (!success)
        {
            string errorMessage =
                $"Database error. Can't set the timezone. User ID: '{userId}', timezone: '{timezone}'";
            await botClient.SendMessageAsync(userId, errorMessage, cancellationToken: cancellationToken);
            return;
        }

        string message = $"Timezone updated: {timezone:+#;-#;0}";
        await botClient.SendMessageAsync(userId, message, cancellationToken: cancellationToken);
    }

    private async Task SetMaxKcalAsync(string args, long userId, IBotClient botClient,
        CancellationToken cancellationToken)
    {
        if (!double.TryParse(args.Trim(), out double limit) || limit < 0)
        {
            string invalidCommandMessage =
                $"Sorry, I didn't understand your 'limit' command. Invalid value: '{args}'. Type /start to see a list of available commands.";
            await botClient.SendMessageAsync(userId, invalidCommandMessage, cancellationToken: cancellationToken);
            return;
        }

        bool success = await _database.SetMaxKcalAsync(userId, limit);
        if (!success)
        {
            string errorMessage =
                $"Database error. Can't set the limit. User ID: '{userId}', limit: '{limit}'";
            await botClient.SendMessageAsync(userId, errorMessage, cancellationToken: cancellationToken);
            return;
        }

        string message = $"Limit updated: {limit} kcal";
        await botClient.SendMessageAsync(userId, message, cancellationToken: cancellationToken);
    }

    private async Task SetNotificationsEnabled(long userId, IBotClient botClient, bool enabled,
        CancellationToken cancellationToken)
    {
        bool success = await _database.SetUserNotificationsEnabled(userId, enabled);
        if (!success)
        {
            string errorMessage = $"Database error. Can't update notifications. User ID: '{userId}'";
            await botClient.SendMessageAsync(userId, errorMessage, cancellationToken: cancellationToken);
            return;
        }

        string message = enabled ? "Notifications enabled" : "Notifications disabled";
        await botClient.SendMessageAsync(userId, message, cancellationToken: cancellationToken);
    }

    internal async Task<bool> RegisterUserIfNotRegisteredAsync(long userId, IBotClient botClient,
        CancellationToken cancellationToken)
    {
        if (await _database.HasUserIdAsync(userId))
        {
            return true;
        }

        DateTime registerDate = _timeProvider.GetUtcNow().UtcDateTime;

        bool registered = await _database.RegisterUserIdAsync(userId, registerDate);
        if (!registered)
        {
            Console.WriteLine($"Failed to register user. Id = {userId}");
            return false;
        }

        Console.WriteLine($"Registered user. Id = {userId}");

        const string message = "You are registered! Welcome!";
        await botClient.SendMessageAsync(userId, message, cancellationToken: cancellationToken);
        await botClient.SendMessageAsync(userId, GetHelpMessage(), cancellationToken: cancellationToken);
        return true;
    }

    private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Error: {exception.Message}");
        return Task.CompletedTask;
    }

    internal bool IsAdmin(long userId)
    {
        return userId == _adminId;
    }

    internal static string GetHelpMessage()
    {
        return """
               🍽️ Add a consumed product:
               add молочная каша, 12
               добавить молочная каша, 12
               добавить молочная каша 12
               доб 12

               🗑️ Remove a consumed product by id:
               remove 6
               delete 6
               удалить 6
               дел 6

               🔥 Print consumed kcal by the current day:
               /stat
               стат

               📅 Print all consumed products by the current day starting from 00:00:
               /daystat
               дейстат

               📊 Print all consumed products by all the time:
               /longstat
               лонгстат

               🎯 Set kcal limit:
               limit 123.45
               лимит 123.45

               🌍 Set the time zone offset:
               timezone +7
               пояс 7

               🔔 Enable or disable notifications:
               notifyon
               notifyoff

               ❓ Print this help:
               /start
               help
               """;
    }

    internal DateTime GetUserDayBeginUtc(int timeZone)
    {
        DateTime curDateUser = _timeProvider.GetUtcNow().UtcDateTime.AddHours(+timeZone);
        DateTime dayBeginUser = new DateTime(curDateUser.Year, curDateUser.Month, curDateUser.Day, 0, 0, 0);
        return dayBeginUser.AddHours(-timeZone);
    }

    internal static ConsumedRowInfoStrings DbRowToUserStringRow(ConsumedRowInfo row, int timezone, string timeFormat)
    {
        return new ConsumedRowInfoStrings(
            Id: row.Id.ToString(),
            UserId: row.UserId.ToString(),
            Date: FromDatabaseToUserTimeFormat(row.Date, timezone, timeFormat),
            Text: row.Text,
            Kcal: row.Kcal.ToString("F"));
    }

    internal static string FromDatabaseToUserTimeFormat(DateTime date, int timeZone, string format)
    {
        date = date.AddHours(timeZone);
        return date.ToString(format, CultureInfo.InvariantCulture);
    }

    // TODO: shit?
    internal static bool TryParseDouble(string s, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        char sep = Convert.ToChar(
            CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);

        var normalized = s
            .Replace(",", sep.ToString())
            .Replace(".", sep.ToString());

        return double.TryParse(normalized,
            NumberStyles.Number,
            CultureInfo.CurrentCulture,
            out result);
    }

    [GeneratedRegex(@"^\s*/?(?<cmd>[а-яА-Яa-zA-Z0-9_]+)(?:\s+(?<args>\S(?:.*\S)?))?\s*$")]
    private static partial Regex GetParseCommandRegex();

    [GeneratedRegex(@"^\s*(?<name>.+?)\s*,?\s*(?<kcal>\d+[,.]?\d*)?\s*$")]
    private static partial Regex GetAddConsumedRegex();

    // Expose regex for testing
    internal static Regex CommandRegex => ParseCommandRegex;
    internal static Regex AddRegex => AddConsumedRegex;

    public record ConsumedRowInfoStrings(string Id, string UserId, string Date, string Text, string Kcal);
}
